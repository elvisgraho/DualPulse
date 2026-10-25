using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class UsbInputReportTests
{
	private static InputSnapshot RoundTrip(InputSnapshot snapshot)
	{
		byte[] report = new byte[64];
		Assert.Equal(64, UsbInputReport.Build(snapshot, counter: 7, report));
		Assert.Equal(0x01, report[0]);
		Assert.True(DualSenseInputParser.TryParse(report, out InputSnapshot parsed));
		return parsed;
	}

	[Theory]
	[InlineData("idle-bt")]
	[InlineData("cross-bt")]
	[InlineData("dpad-upright-bt")]
	[InlineData("l2-full-bt")]
	[InlineData("left-stick-upleft-bt")]
	public void Build_RoundTripsCapturedBluetoothInput(string fixture)
	{
		Assert.True(DualSenseInputParser.TryParse(TestReports.Fixture(fixture), out InputSnapshot captured));

		Assert.Equal(captured with { Transport = Transport.Usb, BluetoothSequence = null }, RoundTrip(captured));
	}

	[Fact]
	public void Build_RoundTripsEveryButtonTouchAndTheJack()
	{
		InputSnapshot snapshot = new()
		{
			Transport = Transport.Usb,
			LeftX = 1, LeftY = 2, RightX = 250, RightY = 128, L2 = 33, R2 = 44,
			Buttons = Buttons.Square | Buttons.Triangle | Buttons.L1 | Buttons.R3 | Buttons.Ps | Buttons.Mute | Buttons.DpadDown | Buttons.DpadLeft | Buttons.Fn2 | Buttons.RightPaddle,
			GyroX = -100, GyroY = 200, GyroZ = -300, AccelX = 400, AccelY = 8192, AccelZ = -500,
			SensorTimestamp = 123456789,
			Touch0 = new TouchPoint(true, 5, 1919, 1079),
			Touch1 = new TouchPoint(false, 6, 10, 20),
			BatteryStatus = 0x16,
			HeadphonesPlugged = true
		};

		Assert.Equal(snapshot, RoundTrip(snapshot));
	}
}
