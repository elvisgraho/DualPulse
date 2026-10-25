namespace DualPulse.Core.Features;

public enum DualSenseChargingState
{
	Discharging,
	Charging,
	Full,
	Error
}

public readonly struct DualSenseBattery
{
	// 0-100 in steps of 10; the UI picks its battery icons by these exact values.
	public int Level { get; }

	public DualSenseChargingState State { get; }

	public bool IsCharging => State == DualSenseChargingState.Charging;

	public bool IsOnPower => State == DualSenseChargingState.Charging || State == DualSenseChargingState.Full;

	public DualSenseBattery(int level, DualSenseChargingState state)
	{
		Level = level;
		State = state;
	}
}

// Decodes the battery byte of the DualSense input report (status[0]: USB report byte 53, Bluetooth 0x31 report byte 54).
// Low nibble = capacity 0-10, high nibble = charging state (0 discharging, 1 charging, 2 full, 0xA/0xB out of range, 0xF error).
public static class DualSenseBatteryStatus
{
	public static DualSenseBattery Parse(byte status)
	{
		int capacity = Math.Min(status & 0x0F, 10);
		switch (status >> 4)
		{
		case 0x0:
			return new DualSenseBattery(capacity * 10, DualSenseChargingState.Discharging);
		case 0x1:
			return new DualSenseBattery(capacity * 10, DualSenseChargingState.Charging);
		case 0x2:
			return new DualSenseBattery(100, DualSenseChargingState.Full);
		default:
			return new DualSenseBattery(capacity * 10, DualSenseChargingState.Error);
		}
	}

	// DS4 report battery byte (bBatteryLvlSpecial): low nibble = level 0-10, bit 4 = on cable power.
	public static byte ToDualShock4BatteryByte(DualSenseBattery battery)
	{
		byte level = (byte)(battery.Level / 10);
		return (byte)(battery.IsOnPower ? (level | 0x10) : level);
	}
}
