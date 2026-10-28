namespace DualPulse.Core.Audio;

// A noise suppressor working on 10 ms mono frames at 48 kHz (480 samples, scaled like 16-bit sound), cleaned in place;
// returns how likely the frame is speech (0-1). RNNoise on Windows.
public interface INoiseSuppressor : IDisposable
{
	float Process(Span<float> frame);
}

// What games hear from the virtual DualSense's microphone, cleaned up (48 kHz 16-bit stereo in and out):
// - Noise suppression: the sound goes through the suppressor in 10 ms mono frames (fans, keyboard and hum removed).
// - Voice gate: silent until the suppressor hears speech, open again for 300 ms after the last of it; the gain
//   ramps over a frame so it never clicks. With the gate alone, the suppressor only listens.
// - Boost: up to +12 dB, through a soft limit that never reaches full scale.
// Both frame-based steps add 10 ms; without them the sound passes at once. Without a suppressor (none available),
// suppression and the gate do nothing.
public sealed class MicrophoneProcessor : IDisposable
{
	public const int FrameLength = 480;

	private const float SpeechLikely = 0.6f;

	private const int HoldFrames = 30;

	private const double LimitFrom = 0.75;

	private const double LimitCeiling = 0.98;

	private readonly Func<INoiseSuppressor?> createSuppressor;

	private INoiseSuppressor? suppressor;

	private bool suppressorTried;

	private readonly float[] frame = new float[FrameLength];

	private readonly float[] listened = new float[FrameLength];

	private int filled;

	private readonly short[] output = new short[FrameLength * 2];

	private float gateGain;

	private int sinceSpeech = int.MaxValue;

	private short[] passed = [];

	public MicrophoneProcessor(Func<INoiseSuppressor?> createSuppressor)
	{
		this.createSuppressor = createSuppressor;
	}

	public bool NoiseSuppression { get; set; }

	public bool VoiceGate { get; set; }

	// 0-12.
	public int BoostDb { get; set; }

	public void Process(ReadOnlySpan<short> stereo, MicrophoneBlock blocks)
	{
		double boost = Math.Pow(10, Math.Clamp(BoostDb, 0, 12) / 20.0);
		if ((NoiseSuppression || VoiceGate) && Suppressor() != null)
		{
			for (int i = 0; i + 1 < stereo.Length; i += 2)
			{
				frame[filled++] = (stereo[i] + stereo[i + 1]) / 2f;
				if (filled == FrameLength)
				{
					filled = 0;
					ProcessFrame(boost);
					blocks(output);
				}
			}
			return;
		}
		filled = 0;
		gateGain = 0;
		if (boost == 1)
		{
			blocks(stereo);
			return;
		}
		if (passed.Length < stereo.Length)
		{
			passed = new short[stereo.Length];
		}
		for (int i = 0; i < stereo.Length; i++)
		{
			passed[i] = Boosted(stereo[i], boost);
		}
		blocks(passed.AsSpan(0, stereo.Length));
	}

	private void ProcessFrame(double boost)
	{
		float voice;
		if (NoiseSuppression)
		{
			voice = suppressor!.Process(frame);
		}
		else
		{
			frame.CopyTo(listened, 0);
			voice = suppressor!.Process(listened);
		}
		float from = VoiceGate ? gateGain : 1;
		float to = 1;
		if (VoiceGate)
		{
			sinceSpeech = voice >= SpeechLikely ? 0 : sinceSpeech == int.MaxValue ? sinceSpeech : sinceSpeech + 1;
			to = sinceSpeech <= HoldFrames ? 1 : 0;
		}
		for (int i = 0; i < FrameLength; i++)
		{
			float gain = from == to ? to : from + (to - from) * (i + 1) / FrameLength;
			short sample = Boosted(frame[i] * gain, boost);
			output[2 * i] = sample;
			output[2 * i + 1] = sample;
		}
		gateGain = to;
	}

	private static short Boosted(double sample, double boost)
	{
		double x = sample * boost / short.MaxValue;
		double magnitude = Math.Abs(x);
		if (magnitude > LimitFrom)
		{
			magnitude = LimitFrom + (LimitCeiling - LimitFrom) * Math.Tanh((magnitude - LimitFrom) / (LimitCeiling - LimitFrom));
		}
		return (short)Math.Round(Math.Sign(x) * magnitude * short.MaxValue);
	}

	private INoiseSuppressor? Suppressor()
	{
		if (!suppressorTried)
		{
			suppressorTried = true;
			try
			{
				suppressor = createSuppressor();
			}
			catch (Exception)
			{
				suppressor = null;
			}
		}
		return suppressor;
	}

	public void Dispose()
	{
		suppressor?.Dispose();
		suppressor = null;
	}
}
