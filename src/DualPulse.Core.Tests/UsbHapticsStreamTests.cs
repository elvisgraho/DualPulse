using DualPulse.Core.Audio;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class UsbHapticsStreamTests
{
	[Fact]
	public void Frames_BecomeFourChannelAudio_WithHapticsOnChannelsThreeAndFour()
	{
		UsbHapticsStream stream = new();
		stream.Push(64, -64);

		short[] samples = stream.Drain();

		// One 3 kHz frame is 16 samples at 48 kHz, 4 channels each.
		Assert.Equal(16 * 4, samples.Length);
		for (int i = 0; i < samples.Length; i += 4)
		{
			Assert.Equal(0, samples[i]);
			Assert.Equal(0, samples[i + 1]);
		}
		// Ramps from silence up to the frame's level, scaled from 8 to 16 bits.
		Assert.Equal(64 * 256, samples[^2]);
		Assert.Equal(-64 * 256, samples[^1]);
		Assert.InRange(samples[2], 0, 64 * 256 / 2);
	}

	[Fact]
	public void Drain_EmptiesWhatWasPushed()
	{
		UsbHapticsStream stream = new();
		stream.Push(10, 10);
		stream.Push(20, 20);

		Assert.Equal(2 * 16 * 4, stream.Drain().Length);
		Assert.Empty(stream.Drain());
	}
}
