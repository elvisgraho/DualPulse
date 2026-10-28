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

	// Gate tests run at 48 kHz: 48 frames per millisecond.
	private const int Ms = 48;

	private static readonly Random Noise = new(3);

	// Room noise (about -64 dBFS) or speech-like sound (about -24 dBFS), stereo.
	private static short[] Sound(int ms, int amplitude)
	{
		short[] stereo = new short[ms * Ms * 2];
		for (int i = 0; i < stereo.Length; i += 2)
		{
			stereo[i] = stereo[i + 1] = (short)Noise.Next(-amplitude, amplitude + 1);
		}
		return stereo;
	}

	private static short[] Concat(params short[][] parts) => [.. parts.SelectMany(p => p)];

	// Output frames from start ms to end ms (left channel).
	private static short[] Between(short[] output, int startMs, int endMs) =>
		[.. Enumerable.Range(startMs * Ms, (endMs - startMs) * Ms).Select(f => output[2 * f])];

	private static MicrophoneProcessor Gate() => new() { NoiseGate = true };

	[Fact]
	public void Gate_QuietRoom_StaysSilent()
	{
		short[] output = Run(Gate(), Sound(3000, 20));

		Assert.All(Between(output, 2000, 2900), s => Assert.Equal(0, s));
	}

	[Fact]
	public void Gate_Speech_Opens_KeepingTheStartOfTheWord()
	{
		short[] input = Concat(Sound(2000, 20), Sound(300, 3000), Sound(100, 20));

		short[] output = Run(Gate(), input);

		// Output runs 30 ms behind; the word starts at 2000 ms and its first 10 ms come through already.
		short[] start = Between(output, 2030, 2040);
		Assert.True(start.Count(s => s != 0) > start.Length / 2);
		// Fully open after that: the word passes as it was.
		Assert.Equal(Between(input, 2020, 2270), Between(output, 2050, 2300));
	}

	[Fact]
	public void Gate_MouthClick_DoesNotOpenIt()
	{
		short[] click = Sound(4, 12000);

		short[] output = Run(Gate(), Concat(Sound(2000, 20), click, Sound(500, 20)));

		Assert.All(Between(output, 1990, 2400), s => Assert.Equal(0, s));
	}

	[Fact]
	public void Gate_StaysOpenBetweenWords()
	{
		short[] input = Concat(Sound(2000, 20), Sound(300, 3000), Sound(120, 20), Sound(300, 3000), Sound(100, 20));

		short[] output = Run(Gate(), input);

		// The 120 ms pause (shifted 30 ms) passes as it was.
		Assert.Equal(Between(input, 2310, 2410), Between(output, 2340, 2440));
	}

	[Fact]
	public void Gate_ClosesAfterHoldAndFade()
	{
		short[] output = Run(Gate(), Concat(Sound(2000, 20), Sound(300, 3000), Sound(1000, 20)));

		Assert.All(Between(output, 2800, 3300), s => Assert.Equal(0, s));
	}

	[Fact]
	public void Gate_StartingWithDigitalSilence_StillLearnsTheRoom()
	{
		// A microphone often starts with a moment of exact zeros; that isn't the room.
		short[] output = Run(Gate(), Concat(new short[200 * Ms * 2], Sound(3000, 20)));

		Assert.All(Between(output, 2500, 3100), s => Assert.Equal(0, s));
	}

	[Fact]
	public void Gate_Off_AddsNoDelay()
	{
		short[] input = Sound(50, 3000);

		Assert.Equal(input, Run(new MicrophoneProcessor(), input));
	}
}
