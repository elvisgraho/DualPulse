namespace DualPulse.Core.Power;

// IdleTimeoutMinutes 0 disables the idle power-off.
public sealed record PowerSettings(bool ShortcutEnabled = true, bool PowerOffOnExit = false, int IdleTimeoutMinutes = 15);

public enum PowerAction
{
	None,
	ShortcutPowerOff,
	IdlePowerOff
}
