using Xunit;
using DualPulse.Core.Features;

namespace DualPulse.Core.Tests;

public class BatteryNotifierTests
{
	private const DualSenseChargingState Discharging = DualSenseChargingState.Discharging;

	[Fact]
	public void NoWarningAboveLowThreshold()
	{
		BatteryNotifier notifier = new BatteryNotifier();

		Assert.Null(notifier.Evaluate(100, Discharging));
		Assert.Null(notifier.Evaluate(30, Discharging));
	}

	[Fact]
	public void WarnsOnceAtLowThenOnceAtCritical()
	{
		BatteryNotifier notifier = new BatteryNotifier();

		Assert.Contains("low", notifier.Evaluate(20, Discharging));
		Assert.Null(notifier.Evaluate(20, Discharging));
		Assert.Contains("critical", notifier.Evaluate(10, Discharging));
		Assert.Null(notifier.Evaluate(10, Discharging));
		Assert.Null(notifier.Evaluate(0, Discharging));
	}

	[Fact]
	public void FlickeringAroundThresholdDoesNotRepeat()
	{
		BatteryNotifier notifier = new BatteryNotifier();

		Assert.NotNull(notifier.Evaluate(20, Discharging));
		Assert.Null(notifier.Evaluate(30, Discharging));
		Assert.Null(notifier.Evaluate(20, Discharging));
	}

	[Fact]
	public void DroppingStraightToCriticalGivesCriticalOnly()
	{
		BatteryNotifier notifier = new BatteryNotifier();

		Assert.Contains("critical", notifier.Evaluate(10, Discharging));
		Assert.Null(notifier.Evaluate(20, Discharging));
	}

	[Theory]
	[InlineData(DualSenseChargingState.Charging)]
	[InlineData(DualSenseChargingState.Full)]
	public void NoWarningWhileOnPowerAndRearmsAfterwards(DualSenseChargingState onPower)
	{
		BatteryNotifier notifier = new BatteryNotifier();
		Assert.NotNull(notifier.Evaluate(20, Discharging));

		Assert.Null(notifier.Evaluate(10, onPower));

		Assert.NotNull(notifier.Evaluate(20, Discharging));
	}

	[Fact]
	public void RearmsAfterRecoveringToForty()
	{
		BatteryNotifier notifier = new BatteryNotifier();
		Assert.NotNull(notifier.Evaluate(20, Discharging));

		Assert.Null(notifier.Evaluate(40, Discharging));

		Assert.NotNull(notifier.Evaluate(20, Discharging));
	}

	[Theory]
	[InlineData(70, DualSenseChargingState.Discharging, "DualPulse - 70%")]
	[InlineData(40, DualSenseChargingState.Charging, "DualPulse - 40% (charging)")]
	[InlineData(100, DualSenseChargingState.Full, "DualPulse - 100% (fully charged)")]
	public void TooltipText_IncludesChargingState(int level, DualSenseChargingState state, string expected)
	{
		Assert.Equal(expected, BatteryNotifier.TooltipText(level, state));
	}
}
