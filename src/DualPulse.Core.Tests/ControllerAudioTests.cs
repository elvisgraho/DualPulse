using System.Buffers.Binary;
using System.IO.Hashing;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class ControllerAudioTests
{
	private static float[] Tone(double seconds, int sampleRate = 48000, float amplitude = 0.3f)
	{
		int frames = (int)(sampleRate * seconds);
		float[] samples = new float[frames * 2];
		for (int i = 0; i < frames; i++)
		{
			samples[2 * i] = samples[2 * i + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * 440 * i / sampleRate));
		}
		return samples;
	}

	[Fact]
	public void Report_Layout()
	{
		sbyte[] left = new sbyte[64];
		sbyte[] right = new sbyte[64];
		left[0] = 11;
		right[0] = -11;
		left[32] = 22;
		byte[] opus = new byte[400];
		opus[0] = 0xAA;
		opus[399] = 0xBB;
		byte[] report = new byte[547];

		int length = AudioReport.Build(report, sequence: 3, counter: 8, bufferLength: 100, left, right, headset: false, opus);

		Assert.Equal(547, length);
		Assert.Equal(new byte[] { 0x39, 0x30, 0x91, 0x06, 0x7E, 100, 100, 100, 100, 8, 0xD2, 0x40 }, report[..12]);
		Assert.Equal(11, (sbyte)report[12]);
		Assert.Equal(-11, (sbyte)report[13]);
		Assert.Equal(22, (sbyte)report[12 + 64]);
		Assert.Equal(new byte[] { 0xD3, 200 }, report[140..142]);
		Assert.Equal(0xAA, report[142]);
		Assert.Equal(0xBB, report[541]);
		Assert.Equal(0, report[542]);
	}

	[Fact]
	public void Report_Headset_UsesItsMarker_AndTheCrcCoversSeedAndFirst543Bytes()
	{
		byte[] report = new byte[547];
		AudioReport.Build(report, 0, 0, 100, new sbyte[64], new sbyte[64], headset: true, new byte[400]);

		Assert.Equal(0xD6, report[140]);
		byte[] input = new byte[544];
		input[0] = 0xA2;
		report.AsSpan(0, 543).CopyTo(input.AsSpan(1));
		Assert.Equal(Crc32.HashToUInt32(input), BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(543)));
	}

	[Fact]
	public void Speaker_ResamplesTo45kHz_FromAnySourceRate()
	{
		SpeakerStream from48 = new();
		SpeakerStream from44 = new();

		from48.Push(Tone(0.05), 48000, 2);
		from44.Push(Tone(0.05, 44100), 44100, 2);

		Assert.InRange(from48.Buffered, 2248, 2252);
		Assert.InRange(from44.Buffered, 2248, 2252);
	}

	[Fact]
	public void Speaker_WaitsForAHeadStart_ThenPlaysTheAudio()
	{
		SpeakerStream speaker = new();
		byte[] opus = new byte[400];

		speaker.Push(Tone(0.03), 48000, 2);
		Assert.False(speaker.Next(opus));

		speaker.Push(Tone(0.03), 48000, 2);
		Assert.True(speaker.Next(opus));
	}

	[Fact]
	public void Speaker_CapsTheBuffer_DroppingTheOldest()
	{
		SpeakerStream speaker = new();

		speaker.Push(Tone(0.3), 48000, 2);

		Assert.Equal(SpeakerStream.MaxBufferedFrames, speaker.Buffered);
	}

	[Fact]
	public void Speaker_CountsUnderruns_OnlyWhenItRunsDryWhilePlaying()
	{
		SpeakerStream speaker = new();
		byte[] opus = new byte[400];
		speaker.Next(opus);
		Assert.Equal(0, speaker.Underruns);

		speaker.Push(Tone(0.05), 48000, 2);
		Assert.True(speaker.Next(opus));
		Assert.True(speaker.Next(opus));
		Assert.False(speaker.Next(opus));
		Assert.False(speaker.Next(opus));

		Assert.Equal(1, speaker.Underruns);
	}

	[Fact]
	public void Speaker_CountsTheFramesItDrops_WhenTheBufferOverflows()
	{
		SpeakerStream speaker = new();

		speaker.Push(Tone(0.3), 48000, 2);

		Assert.InRange(speaker.Overflowed, 13500 - SpeakerStream.MaxBufferedFrames - 2, 13500 - SpeakerStream.MaxBufferedFrames + 2);
	}

	[Fact]
	public void Speaker_GoesIdle_AfterASecondWithoutSound()
	{
		SpeakerStream speaker = new();
		byte[] opus = new byte[400];
		Assert.False(speaker.IsActive);

		speaker.Push(Tone(0.05), 48000, 2);
		Assert.True(speaker.IsActive);
		for (int i = 0; i < 50; i++)
		{
			speaker.Next(opus);
		}

		Assert.False(speaker.IsActive);
	}

	[Fact]
	public void Speaker_EncodesTwoValidOpusFrames_Of200Bytes()
	{
		SpeakerStream speaker = new();
		byte[] opus = new byte[400];
		speaker.Push(Tone(0.05), 48000, 2);
		Assert.True(speaker.Next(opus));

		Concentus.IOpusDecoder decoder = Concentus.OpusCodecFactory.CreateDecoder(48000, 2);
		short[] pcm = new short[480 * 2 * 2];
		Assert.Equal(480, decoder.Decode(opus.AsSpan(0, 200), pcm, 480));
		Assert.Equal(480, decoder.Decode(opus.AsSpan(200, 200), pcm, 480));
	}
}
