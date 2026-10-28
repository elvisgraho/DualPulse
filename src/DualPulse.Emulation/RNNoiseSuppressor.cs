using DualPulse.Core.Audio;
using RNNoise.NET;

namespace DualPulse.Emulation;

// Xiph's RNNoise: a small neural network that removes noise from 10 ms frames of 48 kHz mono and tells how likely
// each frame is speech. Frames are cleaned in place (RNNoise copies its input before writing the output).
public sealed unsafe class RNNoiseSuppressor : INoiseSuppressor
{
	private IntPtr state;

	private RNNoiseSuppressor()
	{
		state = Native.rnnoise_create(IntPtr.Zero);
		if (state == IntPtr.Zero)
		{
			throw new InvalidOperationException("RNNoise couldn't start");
		}
	}

	// Null when the native library can't be loaded.
	public static INoiseSuppressor? TryCreate()
	{
		try
		{
			return new RNNoiseSuppressor();
		}
		catch (Exception)
		{
			return null;
		}
	}

	public float Process(Span<float> frame)
	{
		if (frame.Length != MicrophoneProcessor.FrameLength || state == IntPtr.Zero)
		{
			return 0;
		}
		fixed (float* samples = frame)
		{
			return Native.rnnoise_process_frame(state, samples, samples);
		}
	}

	public void Dispose()
	{
		if (state != IntPtr.Zero)
		{
			Native.rnnoise_destroy(state);
			state = IntPtr.Zero;
		}
	}
}
