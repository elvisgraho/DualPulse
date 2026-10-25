namespace DualPulse.Core.Protocol;

// Reshapes a stick (bytes 0-255, 128 = centre) the way games see it, keeping its direction: inside Deadzone (0-1 of
// the full throw) it reads centred, and from there it ramps up to the edge at FullTiltAt, so the first movement past
// the deadzone is small instead of a jump.
public static class StickShape
{
	private const double Centre = 127.5;

	public static (byte X, byte Y) Apply(byte x, byte y, double deadzone, double fullTiltAt)
	{
		if (deadzone <= 0 && fullTiltAt >= 1)
		{
			return (x, y);
		}
		double dx = (x - Centre) / Centre;
		double dy = (y - Centre) / Centre;
		double magnitude = Math.Sqrt(dx * dx + dy * dy);
		if (magnitude <= deadzone)
		{
			return (128, 128);
		}
		double scaled = Math.Min(1, (magnitude - deadzone) / Math.Max(0.01, fullTiltAt - deadzone));
		// Scaling keeps the direction; a diagonal beyond the circle is clamped per axis.
		double factor = scaled / magnitude;
		return (ToByte(dx * factor), ToByte(dy * factor));
	}

	private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(Centre + value * Centre), 0, 255);
}
