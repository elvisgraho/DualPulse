namespace DualPulse.Core.Mapping;

// Shapes an analog trigger value for games: the first Deadzone of the pull sends nothing, and past it the output
// starts at StartAt, so a light touch registers in games with a sluggish start. Both are fractions, clamped to 0-0.3.
public static class TriggerRange
{
	public const double Max = 0.3;

	public static byte Apply(byte value, double deadzone, double startAt)
	{
		double dz = Math.Clamp(deadzone, 0, Max) * 255;
		double start = Math.Clamp(startAt, 0, Max) * 255;
		if (value == 0 || value <= dz)
		{
			return 0;
		}
		double travel = (value - dz) / (255 - dz);
		return (byte)Math.Clamp(Math.Round(start + travel * (255 - start)), 1, 255);
	}
}
