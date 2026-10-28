using DualPulse.Core.Audio;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

// The real native RNNoise shipped with the app.
public sealed class RNNoiseSuppressorTests
{
	[Fact]
	public void Loads_AndQuietsSteadyHiss()
	{
		using INoiseSuppressor? rnnoise = RNNoiseSuppressor.TryCreate();
		Assert.NotNull(rnnoise);
		Random random = new(7);
		double before = 0, after = 0;
		float[] frame = new float[MicrophoneProcessor.FrameLength];

		// Three seconds of white noise at about -35 dBFS; RNNoise needs a moment to learn it, so measure the last second.
		for (int block = 0; block < 300; block++)
		{
			for (int i = 0; i < frame.Length; i++)
			{
				frame[i] = (float)((random.NextDouble() * 2 - 1) * 1000);
			}
			double input = frame.Sum(s => (double)s * s);
			float voice = rnnoise.Process(frame);
			Assert.InRange(voice, 0f, 1f);
			if (block >= 200)
			{
				before += input;
				after += frame.Sum(s => (double)s * s);
			}
		}

		// RNNoise is trained on real-world noise; flat white noise is its weak spot, but it still clearly drops.
		Assert.True(after < before * 0.75, $"hiss only dropped to {after / before:P1} of its energy");
	}
}
