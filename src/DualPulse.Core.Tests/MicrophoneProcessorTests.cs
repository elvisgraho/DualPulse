using DualPulse.Core.Audio;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class MicrophoneProcessorTests
{
	private static short[] Stereo(int frames, short value) => Enumerable.Repeat(value, frames * 2).ToArray();

	private static short[] Run(MicrophoneProcessor processor, short[] stereo)
	{
		List<short> output = [];
		processor.Process(stereo, block => output.AddRange(block));
		return [.. output];
	}

	[Fact]
	public void NoBoost_PassesThroughUnchanged()
	{
		short[] input = [1, 2, 3, 4];

		Assert.Equal(input, Run(new MicrophoneProcessor(), input));
	}

	[Fact]
	public void Boost_6dB_DoublesQuietSound()
	{
		short[] output = Run(new MicrophoneProcessor { BoostDb = 6 }, Stereo(4, 1000));

		Assert.All(output, s => Assert.InRange(s, 1990, 2000));
	}

	[Fact]
	public void Boost_NeverClips()
	{
		short[] output = Run(new MicrophoneProcessor { BoostDb = 12 }, Stereo(4, 30000));

		Assert.All(output, s => Assert.InRange(s, 30000, short.MaxValue));
		Assert.All(output, s => Assert.NotEqual(short.MaxValue, s));
	}
}
