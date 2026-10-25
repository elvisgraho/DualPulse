using System;
using Xunit;
using DualPulse.Core.Features;

namespace DualPulse.Core.Tests;

public class FirmwareInfoTests
{
	// Feature report 0x20 as read from a real DualSense (firmware 06.30, built Jul 4 2025).
	private static readonly byte[] RealFirmwareReport = Hex("20-4A-75-6C-20-20-34-20-32-30-32-35-31-30-3A-33-38-3A-34-30-03-00-0B-00-11-08-00-00-2A-00-10-01-00-28-03-00-00-00-00-00-00-00-00-00-30-06-00-00-01-00-03-00-10-10-03-00-06-00-00-00-49-9F-80-30");

	[Fact]
	public void Parse_ReadsVersionsAndBuildDate()
	{
		FirmwareInfo info = FirmwareInfo.Parse(RealFirmwareReport)!;

		Assert.Equal(0x0811u, info.HardwareVersion);
		Assert.Equal(0x0110002Au, info.FirmwareVersion);
		Assert.Equal(0x0630, info.UpdateVersion);
		Assert.Equal("06.30", info.UpdateVersionText);
		Assert.Equal("Jul 4 2025 10:38:40", info.BuildDate);
	}

	[Fact]
	public void Parse_RejectsWrongOrShortReports()
	{
		byte[] wrongId = (byte[])RealFirmwareReport.Clone();
		wrongId[0] = 0x09;

		Assert.Null(FirmwareInfo.Parse(wrongId));
		Assert.Null(FirmwareInfo.Parse(new byte[10] { 0x20, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
		Assert.Null(FirmwareInfo.Parse(null));
	}

	private static byte[] Hex(string dashed)
	{
		string[] parts = dashed.Split('-');
		byte[] bytes = new byte[parts.Length];
		for (int i = 0; i < parts.Length; i++)
		{
			bytes[i] = Convert.ToByte(parts[i], 16);
		}
		return bytes;
	}
}

public class LinkQualityMeterTests
{
	private static long Ms(double milliseconds) => (long)(milliseconds * TimeSpan.TicksPerMillisecond);

	[Fact]
	public void NoSnapshotBeforeAFullWindow()
	{
		LinkQualityMeter meter = new LinkQualityMeter();
		meter.Observe(0, Ms(0));
		meter.Observe(1, Ms(500));

		Assert.Null(meter.Current);
	}

	[Fact]
	public void MeasuresRateWithoutLoss()
	{
		LinkQualityMeter meter = new LinkQualityMeter();
		for (int i = 0; i <= 500; i++)
		{
			meter.Observe(i & 0xF, Ms(i * 2.0));
		}

		Assert.NotNull(meter.Current);
		Assert.InRange(meter.Current!.ReportsPerSecond, 495, 505);
		Assert.Equal(0.0, meter.Current!.LossPercent);
		Assert.InRange(meter.Current!.MaxGapMilliseconds, 1.99, 2.01);
	}

	[Fact]
	public void CountsSequenceGapsAsLossAndRecordsWorstGap()
	{
		LinkQualityMeter meter = new LinkQualityMeter();
		int sequence = 0;
		double time = 0;
		for (int i = 0; i < 100; i++)
		{
			meter.Observe(sequence & 0xF, Ms(time));
			// Every 10th report, three reports go missing and the gap is 4x longer.
			int step = (i % 10 == 9) ? 4 : 1;
			sequence += step;
			time += 10.0 * step;
		}
		meter.Observe(sequence & 0xF, Ms(time));

		Assert.NotNull(meter.Current);
		Assert.InRange(meter.Current!.LossPercent!.Value, 20.0, 25.0);
		Assert.InRange(meter.Current!.MaxGapMilliseconds, 39.9, 40.1);
	}

	[Fact]
	public void UsbHasNoLossFigure()
	{
		LinkQualityMeter meter = new LinkQualityMeter();
		for (int i = 0; i <= 300; i++)
		{
			meter.Observe(null, Ms(i * 4.0));
		}

		Assert.Null(meter.Current!.LossPercent);
		Assert.Equal($"{meter.Current!.ReportsPerSecond:F0} Hz", meter.Current!.Summary);
	}
}
