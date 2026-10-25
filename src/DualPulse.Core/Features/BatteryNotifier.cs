namespace DualPulse.Core.Features;

// Decides when to show a low-battery notification: once when the level drops to each threshold while
// discharging, re-armed only after charging or recovering well above the highest threshold
// (so a reading that flickers between 20% and 30% does not notify repeatedly).
public sealed class BatteryNotifier
{
	public const int LowThreshold = 20;

	public const int CriticalThreshold = 10;

	private const int RearmLevel = 40;

	private int lowestNotifiedThreshold = int.MaxValue;

	public string? Evaluate(int level, DualSenseChargingState state)
	{
		if (state != DualSenseChargingState.Discharging || level >= RearmLevel)
		{
			lowestNotifiedThreshold = int.MaxValue;
			return null;
		}
		if (level <= CriticalThreshold && lowestNotifiedThreshold > CriticalThreshold)
		{
			lowestNotifiedThreshold = CriticalThreshold;
			return $"Controller battery critical ({level}%). Charge it soon.";
		}
		if (level <= LowThreshold && lowestNotifiedThreshold > LowThreshold)
		{
			lowestNotifiedThreshold = LowThreshold;
			return $"Controller battery low ({level}%).";
		}
		return null;
	}

	public static string TooltipText(int level, DualSenseChargingState state)
	{
		string suffix = state switch
		{
			DualSenseChargingState.Charging => " (charging)",
			DualSenseChargingState.Full => " (fully charged)",
			DualSenseChargingState.Error => " (charging error)",
			_ => ""
		};
		return $"DualPulse - {level}%{suffix}";
	}
}
