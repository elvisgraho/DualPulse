namespace DualPulse.Core.Features;

// tracks when the player last used the controller, for idle auto power-off. Sticks and triggers must move
// a noticeable amount from where they last settled (so drift does not count); any button change counts.
// Motion sensors are ignored so a controller lying on a desk counts as idle.
public sealed class InputActivityTracker
{
	public const int AxisThreshold = 16;

	private readonly byte[] reference = new byte[9];

	private bool hasReference;

	public DateTime LastActivityUtc { get; private set; }

	public InputActivityTracker(DateTime nowUtc)
	{
		LastActivityUtc = nowUtc;
	}

	public void Observe(byte lx, byte ly, byte rx, byte ry, byte l2, byte r2, byte buttons0, byte buttons1, byte buttons2, DateTime nowUtc)
	{
		byte[] current = new byte[9] { lx, ly, rx, ry, l2, r2, buttons0, buttons1, buttons2 };
		bool active = !hasReference;
		for (int i = 0; i < 6 && !active; i++)
		{
			active = Math.Abs(current[i] - reference[i]) >= AxisThreshold;
		}
		for (int i = 6; i < 9 && !active; i++)
		{
			active = current[i] != reference[i];
		}
		if (active)
		{
			Array.Copy(current, reference, current.Length);
			hasReference = true;
			LastActivityUtc = nowUtc;
		}
	}

	public bool IsIdle(TimeSpan timeout, DateTime nowUtc)
	{
		return timeout > TimeSpan.Zero && nowUtc - LastActivityUtc >= timeout;
	}
}
