using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

// Fixtures are real reports captured from a DualSense (see Fixtures/README.md).
public class InputParserTests
{
	public static TheoryData<string> Transports => new() { "usb", "bt" };

	private static InputSnapshot Load(string fixture, string transport)
	{
		string hex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{fixture}-{transport}.hex")).Trim();
		byte[] report = hex.Split(' ').Select(b => Convert.ToByte(b, 16)).ToArray();
		Assert.True(DualSenseInputParser.TryParse(report, out InputSnapshot snapshot));
		return snapshot;
	}

	[Theory]
	[MemberData(nameof(Transports))]
	public void Idle_SticksNearCentre_NoButtons(string transport)
	{
		InputSnapshot s = Load("idle", transport);

		Assert.All(new[] { s.LeftX, s.LeftY, s.RightX, s.RightY }, axis => Assert.InRange(axis, 108, 148));
		Assert.Equal(Buttons.None, s.Buttons);
		Assert.True(s.L2 < 10 && s.R2 < 10);
		Assert.Equal(transport == "bt" ? Transport.Bluetooth : Transport.Usb, s.Transport);
	}

	[Theory]
	[MemberData(nameof(Transports))]
	public void Cross_SetsCrossOnly(string transport)
	{
		Assert.Equal(Buttons.Cross, Load("cross", transport).Buttons);
	}

	[Theory]
	[MemberData(nameof(Transports))]
	public void DpadUpRight_DecodesHatToTwoFlags(string transport)
	{
		Assert.Equal(Buttons.DpadUp | Buttons.DpadRight, Load("dpad-upright", transport).Buttons);
	}

	[Theory]
	[MemberData(nameof(Transports))]
	public void PsCreateOptions_SetsThreeFlags(string transport)
	{
		Assert.Equal(Buttons.Ps | Buttons.Create | Buttons.Options, Load("ps-create-options", transport).Buttons);
	}

	[Theory]
	[MemberData(nameof(Transports))]
	public void L2Full_ReportsAnalogAndDigital(string transport)
	{
		InputSnapshot s = Load("l2-full", transport);

		Assert.True(s.L2 >= 250);
		Assert.True(s.Buttons.HasFlag(Buttons.L2));
	}

	[Theory]
	[MemberData(nameof(Transports))]
	public void LeftStickUpLeft_ReportsMinimumAxes(string transport)
	{
		InputSnapshot s = Load("left-stick-upleft", transport);

		// Circular gate: the full diagonal is about 37 per axis.
		Assert.True(s.LeftX < 64);
		Assert.True(s.LeftY < 64);
	}

	[Theory]
	[MemberData(nameof(Transports))]
	public void TouchOne_ReportsActivePointOnLeftHalf(string transport)
	{
		InputSnapshot s = Load("touch-one", transport);

		Assert.True(s.Touch0.Active);
		Assert.True(s.Touch0.X < 960);
		Assert.InRange(s.Touch0.Y, 0, 1079);
		Assert.False(s.Touch1.Active);
	}

	[Fact]
	public void Bluetooth_HasSequence_Usb_DoesNot()
	{
		Assert.InRange(Load("idle", "bt").BluetoothSequence!.Value, 0, 15);
		Assert.Null(Load("idle", "usb").BluetoothSequence);
	}

	[Fact]
	public void Rejects_WrongIdOrShortReport()
	{
		byte[] wrongId = new byte[64];
		wrongId[0] = 0x05;
		byte[] shortBluetooth = new byte[10];
		shortBluetooth[0] = 0x31;

		Assert.False(DualSenseInputParser.TryParse(wrongId, out _));
		Assert.False(DualSenseInputParser.TryParse(shortBluetooth, out _));
		Assert.False(DualSenseInputParser.TryParse(ReadOnlySpan<byte>.Empty, out _));
	}

	[Fact]
	public void Parse_HeadphoneJack_FromTheStatusByte()
	{
		byte[] report = TestReports.Fixture("idle-usb");
		Assert.True(DualSenseInputParser.TryParse(report, out InputSnapshot unplugged));
		report[1 + 53] |= 0x01;
		Assert.True(DualSenseInputParser.TryParse(report, out InputSnapshot plugged));

		Assert.False(unplugged.HeadphonesPlugged);
		Assert.True(plugged.HeadphonesPlugged);
	}
}
