using Xunit;
using DualPulse.Core.Features;

namespace DualPulse.Core.Tests;

public class DualSenseBatteryStatusTests
{
	[Theory]
	[InlineData(0x00, 0, DualSenseChargingState.Discharging)]
	[InlineData(0x07, 70, DualSenseChargingState.Discharging)]
	[InlineData(0x0A, 100, DualSenseChargingState.Discharging)]
	[InlineData(0x13, 30, DualSenseChargingState.Charging)]
	[InlineData(0x22, 100, DualSenseChargingState.Full)]
	[InlineData(0xA5, 50, DualSenseChargingState.Error)]
	[InlineData(0xB5, 50, DualSenseChargingState.Error)]
	[InlineData(0xF1, 10, DualSenseChargingState.Error)]
	public void Parse_DecodesCapacityAndChargingState(byte status, int level, DualSenseChargingState state)
	{
		DualSenseBattery battery = DualSenseBatteryStatus.Parse(status);

		Assert.Equal(level, battery.Level);
		Assert.Equal(state, battery.State);
	}

	[Fact]
	public void Parse_ClampsCapacityAboveTen()
	{
		Assert.Equal(100, DualSenseBatteryStatus.Parse(0x0F).Level);
	}

	[Fact]
	public void Parse_ChargingFlagClearsWhenDischargingAgain()
	{
		// Regression: the old code set isCharging on 0x1x but never cleared it.
		Assert.True(DualSenseBatteryStatus.Parse(0x15).IsCharging);
		Assert.False(DualSenseBatteryStatus.Parse(0x05).IsCharging);
	}

	[Fact]
	public void Parse_FullIsOnPowerButNotCharging()
	{
		DualSenseBattery battery = DualSenseBatteryStatus.Parse(0x2A);

		Assert.False(battery.IsCharging);
		Assert.True(battery.IsOnPower);
	}

	[Theory]
	[InlineData(0x08, 0x08)]
	[InlineData(0x14, 0x14)]
	[InlineData(0x2A, 0x1A)]
	[InlineData(0xF3, 0x03)]
	public void ToDualShock4BatteryByte_EncodesLevelAndCableBit(byte status, byte expected)
	{
		Assert.Equal(expected, DualSenseBatteryStatus.ToDualShock4BatteryByte(DualSenseBatteryStatus.Parse(status)));
	}
}
