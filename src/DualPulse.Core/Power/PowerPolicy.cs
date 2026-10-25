using DualPulse.Core.Features;
using DualPulse.Core.Protocol;

namespace DualPulse.Core.Power;

// Decides when to turn a Bluetooth controller off: PS+Triangle (once per press), or after IdleTimeoutMinutes without
// meaningful input while not charging (at most one attempt per minute). Never over USB.
public sealed class PowerPolicy
{
	private static readonly TimeSpan IdleRetryInterval = TimeSpan.FromMinutes(1);

	private bool comboWasHeld;

	private DateTimeOffset nextIdleAttempt = DateTimeOffset.MinValue;

	public PowerAction Evaluate(in InputSnapshot snapshot, DualSenseBattery battery, InputActivityTracker activity, DateTimeOffset now, PowerSettings settings)
	{
		bool comboHeld = (snapshot.Buttons & (Buttons.Ps | Buttons.Triangle)) == (Buttons.Ps | Buttons.Triangle);
		bool comboPressed = comboHeld && !comboWasHeld;
		comboWasHeld = comboHeld;
		if (snapshot.Transport != Transport.Bluetooth)
		{
			return PowerAction.None;
		}
		if (comboPressed && settings.ShortcutEnabled)
		{
			return PowerAction.ShortcutPowerOff;
		}
		if (settings.IdleTimeoutMinutes > 0 && !battery.IsOnPower && now >= nextIdleAttempt
			&& activity.IsIdle(TimeSpan.FromMinutes(settings.IdleTimeoutMinutes), now.UtcDateTime))
		{
			nextIdleAttempt = now + IdleRetryInterval;
			return PowerAction.IdlePowerOff;
		}
		return PowerAction.None;
	}
}
