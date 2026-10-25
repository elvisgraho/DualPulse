using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class GameOutputMonitorTests
{
	private readonly FakeTimeProvider time = new();

	private readonly ListLogger<EmulationService> logger = new();

	private GameOutputMonitor Monitor() => new(logger, time);

	private static byte[] Block(byte flag0, byte flag1 = 0, byte flag2 = 0, byte right = 0, byte left = 0)
	{
		byte[] block = new byte[47];
		block[0] = flag0;
		block[1] = flag1;
		block[38] = flag2;
		block[2] = right;
		block[3] = left;
		return block;
	}

	private List<string> Lines => [.. logger.Entries.Select(e => e.Message)];

	[Fact]
	public void EachNewFlagCombination_IsLoggedOnce_WithTheMotorsAndAudioBytes()
	{
		GameOutputMonitor monitor = Monitor();

		monitor.Output(Block(0x0C));
		monitor.Output(Block(0x0C));
		monitor.Output(Block(0x03, flag2: 0x04, right: 0x80, left: 0xFF));

		Assert.Equal(["Game output flags 0C 00 00, motors R 0 L 0, audio 00 00 00 00", "Game output flags 03 00 04, motors R 128 L 255, audio 00 00 00 00"], Lines);
	}

	[Fact]
	public void RumbleUnderAKnownCombination_IsLoggedTheFirstTimeTheMotorsMove()
	{
		GameOutputMonitor monitor = Monitor();
		monitor.Output(Block(0x03));

		monitor.Output(Block(0x03, right: 10));
		monitor.Output(Block(0x03, right: 20));

		Assert.Equal(["Game output flags 03 00 00, motors R 0 L 0, audio 00 00 00 00", "Game rumble motors R 10 L 0"], Lines);
	}

	[Fact]
	public void Summary_CountsTheGamesBlocks_AndTheSpeakerBuffersTrouble()
	{
		DualPulse.Core.Protocol.SpeakerStream speaker = new();
		GameOutputMonitor monitor = new(logger, time, speaker);
		monitor.Output(Block(0x0C));
		monitor.Output(Block(0x0C));
		speaker.Push(new float[2 * 48000 * 3 / 10], 48000, 2);

		monitor.Haptics([3000]);
		time.Advance(TimeSpan.FromSeconds(5));
		monitor.Haptics([3000]);

		Assert.Matches(@"^Game audio, last 5 s: haptics 2 blocks \(peak 3000\), speaker 0 blocks \(peak 0\), output 2 blocks; speaker buffer 0 underruns, \d{4} frames dropped$", Lines[^1]);
	}

	[Fact]
	public void GameAudio_IsSummedUpEveryFiveSeconds_WhileItFlows()
	{
		GameOutputMonitor monitor = Monitor();

		monitor.Haptics([100, -3000]);
		monitor.Speaker([5, 6, 7, -8]);
		time.Advance(TimeSpan.FromSeconds(5));
		monitor.Haptics([1, 2]);

		Assert.Equal(["Game audio, last 5 s: haptics 2 blocks (peak 3000), speaker 1 blocks (peak 8)"], Lines);
		// Windows streams silence while a game holds the speaker open: silent windows aren't logged.
		time.Advance(TimeSpan.FromSeconds(10));
		monitor.Speaker([1, -1]);
		Assert.Single(Lines);
	}
}
