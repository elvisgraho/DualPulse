using System.Buffers.Binary;
using System.IO.Hashing;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class HapticsTests
{
	[Fact]
	public void Report_Layout()
	{
		sbyte[] left = new sbyte[HapticsReport.Frames];
		sbyte[] right = new sbyte[HapticsReport.Frames];
		left[0] = 100;
		right[0] = -100;
		right[31] = 7;
		byte[] report = new byte[547];

		int length = HapticsReport.Build(report, sequence: 5, counter: 9, bufferDepth: 0x20, left, right);

		Assert.Equal(142, length);
		Assert.Equal(0x32, report[0]);
		Assert.Equal(0x50, report[1]);
		Assert.Equal(new byte[] { 0x91, 0x07, 0xFE, 0, 0, 0, 0, 0x20, 9, 0x92, 0x40 }, report[2..13]);
		Assert.Equal(100, (sbyte)report[13]);
		Assert.Equal(-100, (sbyte)report[14]);
		Assert.Equal(7, (sbyte)report[13 + 63]);
		Assert.All(report[77..138], b => Assert.Equal(0, b));
	}

	[Fact]
	public void Report_Crc_CoversSeedAndFirst138Bytes()
	{
		byte[] report = new byte[142];
		HapticsReport.Build(report, 1, 2, 0x20, new sbyte[32], new sbyte[32]);

		byte[] input = new byte[139];
		input[0] = 0xA2;
		report.AsSpan(0, 138).CopyTo(input.AsSpan(1));
		Assert.Equal(Crc32.HashToUInt32(input), BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(138)));
	}

	[Fact]
	public void Waveform_Silent_WhenBothMotorsAreOff()
	{
		RumbleWaveform waveform = new();
		sbyte[] left = new sbyte[32];
		sbyte[] right = new sbyte[32];

		waveform.Fill(0, 0, left, right);

		Assert.All(left, s => Assert.Equal(0, s));
		Assert.All(right, s => Assert.Equal(0, s));
	}

	[Fact]
	public void Waveform_HeavyMotorDrivesTheLeft_LightMotorTheRight_ByLevel()
	{
		sbyte[] left = new sbyte[32];
		sbyte[] right = new sbyte[32];
		sbyte[] halfLeft = new sbyte[32];
		sbyte[] unused = new sbyte[32];

		new RumbleWaveform().Fill(255, 0, left, right);
		new RumbleWaveform().Fill(128, 0, halfLeft, unused);

		Assert.InRange(left.Max(s => Math.Abs((int)s)), 120, 127);
		Assert.All(right, s => Assert.Equal(0, s));
		Assert.InRange(halfLeft.Max(s => Math.Abs((int)s)), 58, 66);
	}

	[Fact]
	public void Waveform_PhaseCarriesOver_BetweenReports()
	{
		// Two consecutive fills equal one continuous 64-frame wave: no restart (click) at the report boundary.
		RumbleWaveform split = new();
		sbyte[] first = new sbyte[32];
		sbyte[] second = new sbyte[32];
		split.Fill(255, 0, first, new sbyte[32]);
		split.Fill(255, 0, second, new sbyte[32]);

		double step = 2 * Math.PI * RumbleWaveform.HeavyHz / HapticsReport.SampleRate;
		Assert.InRange(second[0], (sbyte)(Math.Round(127 * Math.Sin(32 * step)) - 1), (sbyte)(Math.Round(127 * Math.Sin(32 * step)) + 1));
		Assert.NotEqual(first[0], second[0]);
	}

	private static readonly TimeSpan Tick = HapticsReport.Interval;

	[Fact]
	public void Stream_SendsNothingWhileIdle()
	{
		Assert.False(new HapticsStream().Next(new HapticsMixer(), TimeSpan.Zero, new byte[142]));
	}

	[Fact]
	public void Stream_SendsWhileRumbling_ThenSilenceFor250ms_ThenStops()
	{
		HapticsStream stream = new();
		HapticsMixer mixer = new();
		byte[] report = new byte[142];
		TimeSpan now = TimeSpan.Zero;

		mixer.SetRumble(200, 0);
		Assert.True(stream.Next(mixer, now, report));
		Assert.NotEqual(0, report[13 + 2]);
		mixer.SetRumble(0, 0);
		now += Tick;
		Assert.True(stream.Next(mixer, now, report));
		Assert.All(report[13..77], b => Assert.Equal(0, b));

		// The tail runs 250 ms from the last report with sound (t = 0).
		now += TimeSpan.FromMilliseconds(230);
		Assert.True(stream.Next(mixer, now, report));
		now += TimeSpan.FromMilliseconds(20);
		Assert.False(stream.Next(mixer, now, report));
		Assert.False(stream.IsStreaming);
	}

	[Fact]
	public void Stream_CounterRestarts_ForEachNewStream()
	{
		HapticsStream stream = new();
		HapticsMixer mixer = new();
		byte[] report = new byte[142];
		mixer.SetRumble(200, 0);
		stream.Next(mixer, TimeSpan.Zero, report);
		stream.Next(mixer, Tick, report);
		Assert.Equal(1, report[10]);
		mixer.SetRumble(0, 0);
		stream.Next(mixer, TimeSpan.FromSeconds(1), report);

		mixer.SetRumble(200, 0);
		stream.Next(mixer, TimeSpan.FromSeconds(2), report);

		Assert.Equal(0, report[10]);
	}

	[Fact]
	public void Stream_PlaysPushedAudio()
	{
		HapticsStream stream = new();
		HapticsMixer mixer = new();
		for (int i = 0; i < 32; i++)
		{
			mixer.Push(50, -50);
		}
		byte[] report = new byte[142];

		Assert.True(stream.Next(mixer, TimeSpan.Zero, report));

		Assert.Equal(50, (sbyte)report[13]);
		Assert.Equal(-50, (sbyte)report[14]);
	}
}
