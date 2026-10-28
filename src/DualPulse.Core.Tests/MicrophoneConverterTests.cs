using DualPulse.Core.Audio;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class MicrophoneConverterTests
{
	private static short[] Convert(MicrophoneConverter converter, float[] samples)
	{
		List<short> output = [];
		converter.Process(samples, stereo => output.AddRange(stereo));
		return [.. output];
	}

	[Fact]
	public void Stereo48k_BecomesShorts()
	{
		short[] output = Convert(new MicrophoneConverter(48000, 2), [0.5f, -0.5f, 1f, -1f]);

		Assert.Equal([16384, -16384, 32767, -32767], output);
	}

	[Fact]
	public void Mono_IsCopiedToBothChannels()
	{
		short[] output = Convert(new MicrophoneConverter(48000, 1), [0.5f, -0.25f]);

		Assert.Equal([16384, 16384, -8192, -8192], output);
	}

	[Fact]
	public void MoreChannels_KeepTheFirstTwo()
	{
		short[] output = Convert(new MicrophoneConverter(48000, 4), [0.5f, -0.5f, 0.9f, 0.9f]);

		Assert.Equal([16384, -16384], output);
	}

	[Fact]
	public void OtherRates_AreResampledTo48k()
	{
		// One second at 16 kHz comes out as one second at 48 kHz.
		MicrophoneConverter converter = new(16000, 1);
		int frames = 0;
		for (int block = 0; block < 100; block++)
		{
			frames += Convert(converter, new float[160]).Length / 2;
		}

		Assert.InRange(frames, 47990, 48000);
	}
}
