using Microsoft.Extensions.Logging.Abstractions;
using DualPulse.Core.Devices;
using DualPulse.Core.Features;
using DualPulse.Core.Power;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public class PowerPolicyTests
{
	private static readonly DateTimeOffset Start = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

	private static readonly DualSenseBattery Discharging = new(60, DualSenseChargingState.Discharging);

	private static readonly PowerSettings Defaults = new();

	private static InputSnapshot Snapshot(Buttons buttons = Buttons.None, Transport transport = Transport.Bluetooth)
	{
		return new InputSnapshot(transport, 128, 128, 128, 128, 0, 0, buttons, 0, 0, 0, 0, 0, 0, 0, default, default, 0, transport == Transport.Bluetooth ? 0 : null);
	}

	[Fact]
	public void Policy_ShortcutFiresOncePerPress()
	{
		PowerPolicy policy = new();
		InputActivityTracker activity = new(Start.UtcDateTime);
		InputSnapshot held = Snapshot(Buttons.Ps | Buttons.Triangle);

		List<PowerAction> actions = [];
		for (int i = 0; i < 3; i++)
		{
			actions.Add(policy.Evaluate(held, Discharging, activity, Start, Defaults));
		}
		actions.Add(policy.Evaluate(Snapshot(), Discharging, activity, Start, Defaults));
		actions.Add(policy.Evaluate(held, Discharging, activity, Start, Defaults));

		Assert.Equal([PowerAction.ShortcutPowerOff, PowerAction.None, PowerAction.None, PowerAction.None, PowerAction.ShortcutPowerOff], actions);
	}

	[Fact]
	public void Policy_Shortcut_IgnoredOverUsb()
	{
		PowerPolicy policy = new();

		Assert.Equal(PowerAction.None, policy.Evaluate(Snapshot(Buttons.Ps | Buttons.Triangle, Transport.Usb), Discharging, new InputActivityTracker(Start.UtcDateTime), Start, Defaults));
	}

	[Fact]
	public void Policy_ShortcutDisabled_DoesNothing()
	{
		PowerPolicy policy = new();

		Assert.Equal(PowerAction.None, policy.Evaluate(Snapshot(Buttons.Ps | Buttons.Triangle), Discharging, new InputActivityTracker(Start.UtcDateTime), Start, Defaults with { ShortcutEnabled = false }));
	}

	[Fact]
	public void Policy_Idle_FiresAfterTimeout_NotWhileCharging_AtMostOncePerMinute()
	{
		PowerPolicy policy = new();
		InputActivityTracker activity = new(Start.UtcDateTime);
		InputSnapshot idle = Snapshot();

		Assert.Equal(PowerAction.None, policy.Evaluate(idle, Discharging, activity, Start.AddMinutes(14), Defaults));
		Assert.Equal(PowerAction.None, policy.Evaluate(idle, new DualSenseBattery(60, DualSenseChargingState.Charging), activity, Start.AddMinutes(15), Defaults));
		Assert.Equal(PowerAction.None, policy.Evaluate(idle, new DualSenseBattery(100, DualSenseChargingState.Full), activity, Start.AddMinutes(15), Defaults));
		Assert.Equal(PowerAction.IdlePowerOff, policy.Evaluate(idle, Discharging, activity, Start.AddMinutes(15), Defaults));
		Assert.Equal(PowerAction.None, policy.Evaluate(idle, Discharging, activity, Start.AddMinutes(15.5), Defaults));
		Assert.Equal(PowerAction.IdlePowerOff, policy.Evaluate(idle, Discharging, activity, Start.AddMinutes(16), Defaults));
		Assert.Equal(PowerAction.None, policy.Evaluate(idle, Discharging, activity, Start.AddMinutes(30), Defaults with { IdleTimeoutMinutes = 0 }));
	}

	[Fact]
	public void Policy_Idle_IgnoredOverUsb()
	{
		PowerPolicy policy = new();

		Assert.Equal(PowerAction.None, policy.Evaluate(Snapshot(transport: Transport.Usb), Discharging, new InputActivityTracker(Start.UtcDateTime), Start.AddHours(1), Defaults));
	}
}

public class BluetoothPowerTests
{
	[Fact]
	public void ParseMacAddress_ReadsLeastSignificantByteFirst()
	{
		long address = BluetoothPower.ParseMacAddress(TestReports.Pairing);

		Assert.Equal(0x123456789ABCL, address);
		Assert.Equal("12:34:56:78:9A:BC", BluetoothPower.FormatMacAddress(address));
	}

	[Fact]
	public void ParseMacAddress_RejectsOtherReports()
	{
		byte[] wrongId = (byte[])TestReports.Pairing.Clone();
		wrongId[0] = 0x05;

		Assert.Equal(0, BluetoothPower.ParseMacAddress(wrongId));
		Assert.Equal(0, BluetoothPower.ParseMacAddress([0x09, 1, 2]));
	}

	[Fact]
	public void Session_ReadBluetoothAddress_FromPairingReport()
	{
		FakeHidTransport transport = new();
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		using ControllerSession session = new(device.Info, transport, BluetoothLayout.Sequenced, NullLogger.Instance);
		session.Start();

		Assert.Equal(0x123456789ABCL, session.ReadBluetoothAddress());
	}
}
