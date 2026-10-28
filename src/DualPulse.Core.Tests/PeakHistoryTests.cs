using DualPulse.Core.Audio;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class PeakHistoryTests
{
	private static short[] Stereo(int frames, short value) => Enumerable.Repeat(value, frames * 2).ToArray();

	[Fact]
	public void Empty_IsAllSilence()
	{
		PeakHistory history = new(binsPerSecond: 40, seconds: 5);

		float[] peaks = history.Snapshot();

		Assert.Equal(200, peaks.Length);
		Assert.All(peaks, p => Assert.Equal(0f, p));
		Assert.Equal(0, history.FramesSeen);
	}

	[Fact]
	public void NewestSoundIsAtTheEnd()
	{
		PeakHistory history = new(binsPerSecond: 40, seconds: 5);

		// 25 ms per bin at 48 kHz = 1200 frames: one loud bin, then one quiet bin.
		history.Push(Stereo(1200, short.MaxValue));
		history.Push(Stereo(1200, 3277));

		float[] peaks = history.Snapshot();
		Assert.Equal(1f, peaks[^2], 3);
		Assert.Equal(0.1f, peaks[^1], 3);
		Assert.Equal(0f, peaks[^3]);
		Assert.Equal(2400, history.FramesSeen);
	}

	[Fact]
	public void OlderThanTheWindow_FallsOff()
	{
		PeakHistory history = new(binsPerSecond: 40, seconds: 5);
		history.Push(Stereo(1200, short.MaxValue));

		history.Push(Stereo(1200 * 200, 0));

		Assert.All(history.Snapshot(), p => Assert.Equal(0f, p));
	}

	[Fact]
	public void TakesTheLouderSide()
	{
		PeakHistory history = new(binsPerSecond: 40, seconds: 5);

		history.Push([0, -16384, 0, 0]);
		history.Push(Stereo(1198, 0));

		Assert.Equal(0.5f, history.Snapshot()[^1], 3);
	}
}
