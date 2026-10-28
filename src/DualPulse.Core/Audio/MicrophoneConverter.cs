namespace DualPulse.Core.Audio;

// A captured microphone (interleaved float, any rate and channel count) as 16-bit stereo at 48 kHz, the virtual
// DualSense's microphone format: mono is copied to both sides, beyond two channels only the first two are kept, and
// other rates are resampled linearly.
public sealed class MicrophoneConverter
{
	private const int OutputRate = 48000;

	private readonly int channels;

	private readonly double step;

	// Resampling: the previous input frame and how far past it the next output frame lies (in input frames).
	private float previousLeft;

	private float previousRight;

	private double position;

	private short[] output = [];

	public MicrophoneConverter(int sampleRate, int channels)
	{
		this.channels = Math.Max(1, channels);
		step = sampleRate / (double)OutputRate;
	}

	public void Process(ReadOnlySpan<float> interleaved, MicrophoneBlock blocks)
	{
		int frames = interleaved.Length / channels;
		int most = step == 1 ? frames : (int)Math.Ceiling(frames / step) + 1;
		if (output.Length < most * 2)
		{
			output = new short[most * 2];
		}
		int written = 0;
		for (int frame = 0; frame < frames; frame++)
		{
			float left = interleaved[frame * channels];
			float right = channels > 1 ? interleaved[frame * channels + 1] : left;
			if (step == 1)
			{
				output[written++] = ToShort(left);
				output[written++] = ToShort(right);
				continue;
			}
			while (position < 1)
			{
				output[written++] = ToShort(previousLeft + (left - previousLeft) * (float)position);
				output[written++] = ToShort(previousRight + (right - previousRight) * (float)position);
				position += step;
			}
			position -= 1;
			previousLeft = left;
			previousRight = right;
		}
		if (written > 0)
		{
			blocks(output.AsSpan(0, written));
		}
	}

	private static short ToShort(float sample) => (short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
}
