using DualPulse.Core.Features;
using Xunit;

namespace DualPulse.Core.Tests;

public class FirmwareRumbleSupportTests
{
	[Theory]
	[InlineData(0x0223, false)]
	[InlineData(0x0224, true)]
	[InlineData(0x0630, true)]
	public void SupportsImprovedRumble_FromUpdateVersion0224(int updateVersion, bool expected)
	{
		byte[] report = new byte[64];
		report[0] = FirmwareInfo.ReportId;
		report[44] = (byte)(updateVersion & 0xFF);
		report[45] = (byte)(updateVersion >> 8);

		Assert.Equal(expected, FirmwareInfo.Parse(report)!.SupportsImprovedRumble);
	}
}
