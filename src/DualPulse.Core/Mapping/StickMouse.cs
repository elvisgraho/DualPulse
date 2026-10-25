using DualPulse.Core.Protocol;

namespace DualPulse.Core.Mapping;

// The right stick moves the pointer and the left stick scrolls. A radial deadzone ignores resting drift, and a squared
// response curve keeps small deflections slow for precise pointing. Speeds are per second at full deflection,
// multiplied by Speed / ScrollSpeed; time between reports is capped so a stalled report can't jump the pointer.
public sealed class StickMouse
{
	public const double PixelsPerSecond = 1200;

	public const double TicksPerSecond = 12;

	private const double Deadzone = 0.12;

	private static readonly TimeSpan MaxStep = TimeSpan.FromMilliseconds(50);

	private TimeSpan? previous;

	private double pendingX;

	private double pendingY;

	private double pendingScroll;

	public double Speed { get; set; } = 1;

	public double ScrollSpeed { get; set; } = 1;

	public bool MoveEnabled { get; set; } = true;

	public bool ScrollEnabled { get; set; } = true;

	public void Process(in InputSnapshot input, TimeSpan now, IPcOutput output)
	{
		double seconds = previous is { } last ? Math.Min((now - last).TotalSeconds, MaxStep.TotalSeconds) : 0;
		previous = now;
		if (seconds <= 0)
		{
			return;
		}
		if (MoveEnabled)
		{
			(double x, double y) = Response(input.RightX, input.RightY);
			pendingX += x * PixelsPerSecond * Speed * seconds;
			pendingY += y * PixelsPerSecond * Speed * seconds;
			int moveX = WholePart(pendingX);
			int moveY = WholePart(pendingY);
			if (moveX != 0 || moveY != 0)
			{
				pendingX -= moveX;
				pendingY -= moveY;
				output.MoveMouse(moveX, moveY);
			}
		}
		if (ScrollEnabled)
		{
			// Stick up scrolls up (positive wheel ticks).
			(_, double y) = Response(128, input.LeftY);
			pendingScroll -= y * TicksPerSecond * ScrollSpeed * seconds;
			int ticks = WholePart(pendingScroll);
			if (ticks != 0)
			{
				pendingScroll -= ticks;
				output.Wheel(ticks);
			}
		}
	}

	// Deflection -1..1 per axis after the deadzone and curve.
	private static (double X, double Y) Response(byte rawX, byte rawY)
	{
		double x = Math.Clamp((rawX - 128) / 127.5, -1, 1);
		double y = Math.Clamp((rawY - 128) / 127.5, -1, 1);
		double magnitude = Math.Min(1, Math.Sqrt(x * x + y * y));
		if (magnitude <= Deadzone)
		{
			return (0, 0);
		}
		double scaled = (magnitude - Deadzone) / (1 - Deadzone);
		double curved = scaled * scaled;
		return (x / magnitude * curved, y / magnitude * curved);
	}

	private static int WholePart(double value)
	{
		return (int)Math.Truncate(value + (value >= 0 ? 1e-9 : -1e-9));
	}
}
