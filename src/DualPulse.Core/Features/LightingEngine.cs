using DualPulse.Core.Protocol;

namespace DualPulse.Core.Features;

public enum LightbarMode
{
	Static,
	Breathing,
	Rainbow,
	Battery
}

public enum PlayerLedPattern
{
	Off,
	Player1,
	Player2,
	Player3,
	Player4,
	All,
	// The LEDs fill from the left with the battery level.
	Battery
}

// Lightbar colour for each mode at a point in time. Pure functions: the caller supplies the elapsed time and the
// battery level, so animations are testable with a fake clock.
public static class LightingEngine
{
	public static readonly TimeSpan BreathingPeriod = TimeSpan.FromSeconds(4);

	public static readonly TimeSpan RainbowPeriod = TimeSpan.FromSeconds(8);

	public static readonly Rgb BatteryLow = new(0xFF, 0x3B, 0x3B);

	public static readonly Rgb BatteryHalf = new(0xFF, 0xB0, 0x20);

	public static readonly Rgb BatteryFull = new(0x3D, 0xDC, 0x84);

	// Breathing never goes fully dark, so the controller still reads as on.
	private const double BreathingFloor = 0.15;

	// brightness is 0-100; batteryLevel is 0-100.
	public static Rgb Lightbar(LightbarMode mode, Rgb colour, int brightness, TimeSpan elapsed, int batteryLevel)
	{
		double scale = Math.Clamp(brightness, 0, 100) / 100.0;
		return mode switch
		{
			LightbarMode.Breathing => Scale(colour, scale * Breathe(elapsed)),
			LightbarMode.Rainbow => Scale(FromHueSaturation(360.0 * Phase(elapsed, RainbowPeriod), 100), scale),
			LightbarMode.Battery => Scale(BatteryColour(batteryLevel), scale),
			_ => Scale(colour, scale)
		};
	}

	// hue 0-360, saturation 0-100, full value.
	public static Rgb FromHueSaturation(double hue, double saturation)
	{
		double s = Math.Clamp(saturation, 0, 100) / 100.0;
		double h = (hue % 360 + 360) % 360 / 60.0;
		double x = 1 - Math.Abs(h % 2 - 1);
		(double r, double g, double b) = (int)h switch
		{
			0 => (1.0, x, 0.0),
			1 => (x, 1.0, 0.0),
			2 => (0.0, 1.0, x),
			3 => (0.0, x, 1.0),
			4 => (x, 0.0, 1.0),
			_ => (1.0, 0.0, x)
		};
		return new Rgb(Channel(1 - s + s * r), Channel(1 - s + s * g), Channel(1 - s + s * b));
	}

	// The DualSense's five player LEDs, bit 0 leftmost.
	// The player LEDs at a point in time: Battery fills one LED per 20% (the next one blinks while charging), and with
	// lowFlash a discharging battery at 10% or below flashes the LEDs (all five when the pattern is Off).
	public static byte PlayerLedBits(PlayerLedPattern pattern, DualSenseBattery battery, bool lowFlash, TimeSpan elapsed)
	{
		byte bits = pattern == PlayerLedPattern.Battery ? BatteryBits(battery, elapsed) : PlayerLedBits(pattern);
		if (lowFlash && battery.Level <= 10 && battery.State == DualSenseChargingState.Discharging)
		{
			bool on = elapsed.TotalMilliseconds % 1000 < 500;
			return on ? (bits == 0 ? (byte)0b11111 : bits) : (byte)0;
		}
		return bits;
	}

	public static byte PlayerLedBits(PlayerLedPattern pattern)
	{
		return pattern switch
		{
			PlayerLedPattern.Player1 => 0b00100,
			PlayerLedPattern.Player2 => 0b01010,
			PlayerLedPattern.Player3 => 0b10101,
			PlayerLedPattern.Player4 => 0b11011,
			PlayerLedPattern.All => 0b11111,
			_ => 0
		};
	}

	private static byte BatteryBits(DualSenseBattery battery, TimeSpan elapsed)
	{
		int lit = Math.Clamp((battery.Level + 19) / 20, 1, 5);
		if (battery.IsCharging && lit < 5 && elapsed.TotalMilliseconds % 2000 < 1000)
		{
			lit++;
		}
		return (byte)((1 << lit) - 1);
	}

	// Red at 10% and below, amber at 50%, green at 90% and above, blended in between.
	private static Rgb BatteryColour(int level)
	{
		if (level <= 10)
		{
			return BatteryLow;
		}
		if (level >= 90)
		{
			return BatteryFull;
		}
		return level <= 50 ? Blend(BatteryLow, BatteryHalf, (level - 10) / 40.0) : Blend(BatteryHalf, BatteryFull, (level - 50) / 40.0);
	}

	private static double Breathe(TimeSpan elapsed)
	{
		double wave = 0.5 - 0.5 * Math.Cos(2 * Math.PI * Phase(elapsed, BreathingPeriod));
		return BreathingFloor + (1 - BreathingFloor) * wave;
	}

	private static double Phase(TimeSpan elapsed, TimeSpan period)
	{
		return elapsed.Ticks % period.Ticks / (double)period.Ticks;
	}

	private static Rgb Blend(Rgb from, Rgb to, double t)
	{
		return new Rgb((byte)Math.Round(from.R + (to.R - from.R) * t), (byte)Math.Round(from.G + (to.G - from.G) * t), (byte)Math.Round(from.B + (to.B - from.B) * t));
	}

	private static Rgb Scale(Rgb colour, double factor)
	{
		return new Rgb((byte)Math.Round(colour.R * factor), (byte)Math.Round(colour.G * factor), (byte)Math.Round(colour.B * factor));
	}

	private static byte Channel(double value)
	{
		return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
	}
}
