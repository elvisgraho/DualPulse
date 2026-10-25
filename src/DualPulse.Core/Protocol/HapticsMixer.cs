namespace DualPulse.Core.Protocol;

// What the haptics stream plays: buffered audio frames (system audio) plus the rumble waveform, summed and clamped.
// The buffer holds at most six reports; audio that runs ahead of the stream drops its oldest frames (latency stays
// low), audio that runs behind plays as silence. Thread-safe: audio is pushed on the capture thread, rumble set from
// the read thread, frames taken on the haptics thread.
public sealed class HapticsMixer
{
	public const int MaxBufferedFrames = HapticsReport.Frames * 6;

	private readonly object gate = new();

	private readonly (sbyte Left, sbyte Right)[] buffer = new (sbyte, sbyte)[MaxBufferedFrames];

	private readonly RumbleWaveform waveform = new();

	private readonly sbyte[] rumbleLeft = new sbyte[HapticsReport.Frames];

	private readonly sbyte[] rumbleRight = new sbyte[HapticsReport.Frames];

	private int head;

	private int count;

	private byte large;

	private byte small;

	public int Buffered
	{
		get
		{
			lock (gate)
			{
				return count;
			}
		}
	}

	public void Push(sbyte left, sbyte right)
	{
		lock (gate)
		{
			if (count == MaxBufferedFrames)
			{
				head = (head + 1) % MaxBufferedFrames;
				count--;
			}
			buffer[(head + count) % MaxBufferedFrames] = (left, right);
			count++;
		}
	}

	public void SetRumble(byte large, byte small)
	{
		lock (gate)
		{
			this.large = large;
			this.small = small;
		}
	}

	// Mixes one report's frames; returns whether anything plays (rumble on, or audio above silence).
	public bool Fill(Span<sbyte> left, Span<sbyte> right)
	{
		lock (gate)
		{
			bool rumbling = large != 0 || small != 0;
			waveform.Fill(large, small, rumbleLeft, rumbleRight);
			bool sound = rumbling;
			for (int frame = 0; frame < HapticsReport.Frames; frame++)
			{
				(sbyte audioLeft, sbyte audioRight) = (0, 0);
				if (count > 0)
				{
					(audioLeft, audioRight) = buffer[head];
					head = (head + 1) % MaxBufferedFrames;
					count--;
				}
				sound |= audioLeft != 0 || audioRight != 0;
				left[frame] = (sbyte)Math.Clamp(audioLeft + rumbleLeft[frame], -127, 127);
				right[frame] = (sbyte)Math.Clamp(audioRight + rumbleRight[frame], -127, 127);
			}
			return sound;
		}
	}
}

public enum HapticsAudioStyle
{
	// For music: the loudness of the low end, with beats emphasised and the level evened out, played on a carrier
	// at the actuators' sweet spot.
	Punchy,
	// The filtered audio as it is (games and film, where texture matters).
	Natural
}

// System audio as haptic frames: each channel goes through a two-pole low-pass at 400 Hz (what the actuators can
// play) and is averaged down to 3 kHz (any source rate, by a fractional step). Natural plays that directly; Punchy
// reshapes it (see PunchyChannel). Then Intensity and a gate (near silence becomes silence, so the stream can stop).
// Mono feeds both sides; only the first two channels are used.
public sealed class HapticsAudioConverter
{
	private const double DefaultCutoffHz = 400;

	private readonly int channels;

	private readonly double step;

	private readonly LowPass leftFilter;

	private readonly LowPass rightFilter;

	private double phase;

	private double sumLeft;

	private double sumRight;

	private int summed;

	private readonly Punchy punchy = new();

	// cutoffHz: the low-pass in front of the 3 kHz output; 400 Hz keeps music to its bass, game haptics take more.
	public HapticsAudioConverter(int sampleRate, int channels, double cutoffHz = DefaultCutoffHz)
	{
		this.channels = Math.Max(1, channels);
		step = sampleRate / HapticsReport.SampleRate;
		leftFilter = new LowPass(sampleRate, cutoffHz);
		rightFilter = new LowPass(sampleRate, cutoffHz);
	}

	// 0-2; 1 maps full-scale audio to full haptics. With SoftLimit, 0-6, through a tanh curve.
	public double Intensity { get; set; } = 1;

	// For game haptics: the level goes through 127 * tanh(Intensity * x) instead of a straight line, so a boost lifts
	// soft effects while strong ones saturate smoothly instead of clipping.
	public bool SoftLimit { get; set; }

	public HapticsAudioStyle Style { get; set; } = HapticsAudioStyle.Natural;

	public void Process(ReadOnlySpan<float> interleaved, HapticsMixer mixer)
	{
		double gain = Math.Clamp(Intensity, 0, SoftLimit ? 6 : 2);
		for (int i = 0; i + channels <= interleaved.Length; i += channels)
		{
			sumLeft += leftFilter.Next(interleaved[i]);
			sumRight += rightFilter.Next(interleaved[channels > 1 ? i + 1 : i]);
			summed++;
			phase += 1;
			if (phase >= step)
			{
				phase -= step;
				double left = sumLeft / summed;
				double right = sumRight / summed;
				if (Style == HapticsAudioStyle.Punchy)
				{
					(left, right) = punchy.Next(left, right);
				}
				mixer.Push(ToSample(Shape(left, gain)), ToSample(Shape(right, gain)));
				sumLeft = 0;
				sumRight = 0;
				summed = 0;
			}
		}
	}

	private double Shape(double value, double gain) => 127 * (SoftLimit ? Math.Tanh(gain * value) : gain * value);

	private static sbyte ToSample(double value)
	{
		// Noise gate: anything below one step of the 8-bit range is silence.
		return Math.Abs(value) < 1 ? (sbyte)0 : (sbyte)Math.Clamp(Math.Round(value), -127, 127);
	}

	// Punchy shaping at 3 kHz:
	// - Per side, the low band (150 Hz) feeds a fast envelope (5 ms attack, 60 ms release) and a slow one that
	//   smooths it (300 ms).
	// - Level = half the fast envelope, plus a boost for what rises above the slow one, so kicks and impacts thump
	//   and sustained bass is held back.
	// - Automatic level, one for both sides so the panning survives: divided by the louder side's peak, which
	//   decays over about 20 s, boosting at most about 3x. Quiet tracks come up, while a soft intro or a calm
	//   passage after a heavy one stays softer.
	// - The result is played on a shared 100 Hz carrier, where the actuators respond best, so sub-bass is felt too.
	// - Below the gate a side is silent.
	private sealed class Punchy
	{
		private const double Rate = HapticsReport.SampleRate;

		private const double CarrierHz = 100;

		private const double Target = 0.9;

		// Level of a full-scale kick is about 2; the floor caps the boost at 0.9 / 0.3 = 3x.
		private const double PeakFloor = 0.3;

		private static readonly double PeakDecay = Math.Exp(-1 / (20.0 * Rate));

		private readonly Side left = new();

		private readonly Side right = new();

		private double peak;

		private double phase;

		public (double Left, double Right) Next(double leftSample, double rightSample)
		{
			(double leftLevel, bool leftOpen) = left.Level(leftSample);
			(double rightLevel, bool rightOpen) = right.Level(rightSample);
			peak = Math.Max(Math.Max(leftLevel, rightLevel), peak * PeakDecay);
			phase = (phase + 2 * Math.PI * CarrierHz / Rate) % (2 * Math.PI);
			double scale = Target / Math.Max(peak, PeakFloor);
			double carrier = Math.Sin(phase);
			return (leftOpen ? Math.Min(1, leftLevel * scale) * carrier : 0, rightOpen ? Math.Min(1, rightLevel * scale) * carrier : 0);
		}

		private sealed class Side
		{
			private const double Gate = 0.01;

			private static readonly double FastAttack = Math.Exp(-1 / (0.005 * Rate));

			private static readonly double FastRelease = Math.Exp(-1 / (0.060 * Rate));

			private static readonly double Slow = Math.Exp(-1 / (0.300 * Rate));

			private readonly LowPass band = new((int)Rate, 150);

			private double fast;

			private double slow;

			// The side's level before the automatic level, and whether it is above the gate.
			public (double Level, bool Open) Level(double sample)
			{
				double rectified = Math.Abs(band.Next(sample));
				double fastCoefficient = rectified > fast ? FastAttack : FastRelease;
				fast = fastCoefficient * fast + (1 - fastCoefficient) * rectified;
				// The slow envelope smooths the fast one (not the raw signal), so on a steady note they meet and the
				// boost ends.
				slow = Slow * slow + (1 - Slow) * fast;
				return (0.5 * fast + 2.0 * Math.Max(0, fast - slow), fast >= Gate);
			}
		}
	}

	// Second-order Butterworth low-pass (bilinear transform).
	private sealed class LowPass
	{
		private readonly double b0;

		private readonly double b1;

		private readonly double b2;

		private readonly double a1;

		private readonly double a2;

		private double x1;

		private double x2;

		private double y1;

		private double y2;

		public LowPass(int sampleRate, double cutoff)
		{
			double k = Math.Tan(Math.PI * cutoff / sampleRate);
			double norm = 1 / (1 + Math.Sqrt(2) * k + k * k);
			b0 = k * k * norm;
			b1 = 2 * b0;
			b2 = b0;
			a1 = 2 * (k * k - 1) * norm;
			a2 = (1 - Math.Sqrt(2) * k + k * k) * norm;
		}

		public double Next(double x)
		{
			double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
			x2 = x1;
			x1 = x;
			y2 = y1;
			y1 = y;
			return y;
		}
	}
}
