using DualPulse.Core.Features;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class LightingEngineTests
{
	private static readonly Rgb Colour = new(200, 100, 50);

	private static void AssertNear(Rgb expected, Rgb actual, int tolerance = 2)
	{
		Assert.InRange(actual.R, Math.Max(0, expected.R - tolerance), Math.Min(255, expected.R + tolerance));
		Assert.InRange(actual.G, Math.Max(0, expected.G - tolerance), Math.Min(255, expected.G + tolerance));
		Assert.InRange(actual.B, Math.Max(0, expected.B - tolerance), Math.Min(255, expected.B + tolerance));
	}

	[Fact]
	public void Static_ColourScaledByBrightness()
	{
		Assert.Equal(Colour, LightingEngine.Lightbar(LightbarMode.Static, Colour, 100, TimeSpan.FromSeconds(3), 50));
		AssertNear(new Rgb(100, 50, 25), LightingEngine.Lightbar(LightbarMode.Static, Colour, 50, TimeSpan.Zero, 50));
		Assert.Equal(new Rgb(0, 0, 0), LightingEngine.Lightbar(LightbarMode.Static, Colour, 0, TimeSpan.Zero, 50));
	}

	[Fact]
	public void Breathing_DimmestAtStart_FullAtHalfPeriod_BackAtFullPeriod()
	{
		AssertNear(new Rgb(30, 15, 8), LightingEngine.Lightbar(LightbarMode.Breathing, Colour, 100, TimeSpan.Zero, 50));
		AssertNear(Colour, LightingEngine.Lightbar(LightbarMode.Breathing, Colour, 100, LightingEngine.BreathingPeriod / 2, 50));
		AssertNear(new Rgb(30, 15, 8), LightingEngine.Lightbar(LightbarMode.Breathing, Colour, 100, LightingEngine.BreathingPeriod, 50));
	}

	[Fact]
	public void Rainbow_CyclesThroughHues()
	{
		TimeSpan period = LightingEngine.RainbowPeriod;
		AssertNear(new Rgb(255, 0, 0), LightingEngine.Lightbar(LightbarMode.Rainbow, Colour, 100, TimeSpan.Zero, 50));
		AssertNear(new Rgb(0, 255, 0), LightingEngine.Lightbar(LightbarMode.Rainbow, Colour, 100, period / 3, 50));
		AssertNear(new Rgb(0, 0, 255), LightingEngine.Lightbar(LightbarMode.Rainbow, Colour, 100, period * 2 / 3, 50));
		AssertNear(new Rgb(255, 0, 0), LightingEngine.Lightbar(LightbarMode.Rainbow, Colour, 100, period, 50));
	}

	[Fact]
	public void Battery_RedLow_AmberHalf_GreenFull()
	{
		Assert.Equal(LightingEngine.BatteryLow, LightingEngine.Lightbar(LightbarMode.Battery, Colour, 100, TimeSpan.Zero, 0));
		Assert.Equal(LightingEngine.BatteryLow, LightingEngine.Lightbar(LightbarMode.Battery, Colour, 100, TimeSpan.Zero, 10));
		Assert.Equal(LightingEngine.BatteryHalf, LightingEngine.Lightbar(LightbarMode.Battery, Colour, 100, TimeSpan.Zero, 50));
		Assert.Equal(LightingEngine.BatteryFull, LightingEngine.Lightbar(LightbarMode.Battery, Colour, 100, TimeSpan.Zero, 90));
		Assert.Equal(LightingEngine.BatteryFull, LightingEngine.Lightbar(LightbarMode.Battery, Colour, 100, TimeSpan.Zero, 100));
		Rgb thirty = LightingEngine.Lightbar(LightbarMode.Battery, Colour, 100, TimeSpan.Zero, 30);
		Assert.InRange(thirty.G, LightingEngine.BatteryLow.G + 1, LightingEngine.BatteryHalf.G - 1);
	}

	[Fact]
	public void FromHueSaturation_PrimaryAndGrey()
	{
		Assert.Equal(new Rgb(255, 0, 0), LightingEngine.FromHueSaturation(0, 100));
		Assert.Equal(new Rgb(0, 255, 0), LightingEngine.FromHueSaturation(120, 100));
		Assert.Equal(new Rgb(255, 255, 255), LightingEngine.FromHueSaturation(200, 0));
	}

	[Theory]
	[InlineData(PlayerLedPattern.Off, 0b00000)]
	[InlineData(PlayerLedPattern.Player1, 0b00100)]
	[InlineData(PlayerLedPattern.Player2, 0b01010)]
	[InlineData(PlayerLedPattern.Player3, 0b10101)]
	[InlineData(PlayerLedPattern.Player4, 0b11011)]
	[InlineData(PlayerLedPattern.All, 0b11111)]
	public void PlayerLeds_PatternBits(PlayerLedPattern pattern, int bits)
	{
		Assert.Equal((byte)bits, LightingEngine.PlayerLedBits(pattern));
	}

	private static readonly TimeSpan OnPhase = TimeSpan.FromSeconds(10.2);

	private static readonly TimeSpan OffPhase = TimeSpan.FromSeconds(11.7);

	private static DualSenseBattery Discharging(int level) => new(level, DualSenseChargingState.Discharging);

	[Theory]
	[InlineData(0, 0b00001)]
	[InlineData(20, 0b00001)]
	[InlineData(30, 0b00011)]
	[InlineData(60, 0b00111)]
	[InlineData(80, 0b01111)]
	[InlineData(90, 0b11111)]
	[InlineData(100, 0b11111)]
	public void PlayerLeds_Battery_FillFromTheLeft(int level, int bits)
	{
		Assert.Equal((byte)bits, LightingEngine.PlayerLedBits(PlayerLedPattern.Battery, Discharging(level), lowFlash: false, OnPhase));
	}

	[Fact]
	public void PlayerLeds_Battery_Charging_BlinksTheNextLed()
	{
		DualSenseBattery charging = new(50, DualSenseChargingState.Charging);

		Assert.Equal(0b01111, LightingEngine.PlayerLedBits(PlayerLedPattern.Battery, charging, lowFlash: true, TimeSpan.FromSeconds(10.5)));
		Assert.Equal(0b00111, LightingEngine.PlayerLedBits(PlayerLedPattern.Battery, charging, lowFlash: true, TimeSpan.FromSeconds(11.5)));
	}

	[Fact]
	public void PlayerLeds_LowBattery_FlashesWhateverThePattern()
	{
		Assert.Equal(0b00100, LightingEngine.PlayerLedBits(PlayerLedPattern.Player1, Discharging(10), lowFlash: true, OnPhase));
		Assert.Equal(0, LightingEngine.PlayerLedBits(PlayerLedPattern.Player1, Discharging(10), lowFlash: true, OffPhase));
		Assert.Equal(0b11111, LightingEngine.PlayerLedBits(PlayerLedPattern.Off, Discharging(10), lowFlash: true, OnPhase));
	}

	[Fact]
	public void PlayerLeds_LowBattery_NoFlash_WhenDisabledCharging_OrAbove10()
	{
		Assert.Equal(0b00100, LightingEngine.PlayerLedBits(PlayerLedPattern.Player1, Discharging(10), lowFlash: false, OffPhase));
		Assert.Equal(0b00100, LightingEngine.PlayerLedBits(PlayerLedPattern.Player1, new DualSenseBattery(10, DualSenseChargingState.Charging), lowFlash: true, OffPhase));
		Assert.Equal(0b00100, LightingEngine.PlayerLedBits(PlayerLedPattern.Player1, Discharging(20), lowFlash: true, OffPhase));
	}
}
