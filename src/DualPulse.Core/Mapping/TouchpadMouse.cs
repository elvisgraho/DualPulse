using DualPulse.Core.Protocol;

namespace DualPulse.Core.Mapping;

// The touchpad as a laptop-style touchpad: one finger moves the pointer, a short tap clicks, a two-finger tap
// right-clicks, two fingers dragged scroll (the content follows the fingers), and pressing the pad holds a mouse
// button so it can drag: the right half presses the right button, the left half (or no finger) the left one.
// Touch coordinates are 0-1919 x 0-1079; Speed is pointer pixels per touch unit.
public sealed class TouchpadMouse
{
	private static readonly TimeSpan TapMaxDuration = TimeSpan.FromMilliseconds(200);

	private const int TapMaxTravel = 40;

	private const double ScrollUnitsPerTick = 60;

	private const int RightZoneStart = 960;

	private int previousCount;

	private byte previousId;

	private double previousX;

	private double previousY;

	private TimeSpan gestureStart;

	private double gestureTravel;

	private int gestureFingers;

	private bool pressedDuringGesture;

	private bool pressed;

	private MouseButton pressedButton;

	private double pendingX;

	private double pendingY;

	private double pendingScroll;

	public double Speed { get; set; } = 0.6;

	public void Process(in InputSnapshot input, TimeSpan now, IPcOutput output)
	{
		bool press = (input.Buttons & Buttons.Touchpad) != 0;
		if (press != pressed)
		{
			pressed = press;
			if (press)
			{
				TouchPoint finger = input.Touch0.Active ? input.Touch0 : input.Touch1;
				pressedButton = finger.Active && finger.X >= RightZoneStart ? MouseButton.Right : MouseButton.Left;
			}
			output.MouseButton(pressedButton, press);
			pressedDuringGesture |= press;
		}

		int count = (input.Touch0.Active ? 1 : 0) + (input.Touch1.Active ? 1 : 0);
		TouchPoint first = input.Touch0.Active ? input.Touch0 : input.Touch1;
		double x = count == 2 ? (input.Touch0.X + input.Touch1.X) / 2.0 : first.X;
		double y = count == 2 ? (input.Touch0.Y + input.Touch1.Y) / 2.0 : first.Y;

		if (count > 0 && previousCount == 0)
		{
			gestureStart = now;
			gestureTravel = 0;
			gestureFingers = count;
			pressedDuringGesture = press;
		}
		gestureFingers = Math.Max(gestureFingers, count);

		if (count == 1 && previousCount == 1 && first.Id == previousId)
		{
			Move(x - previousX, y - previousY, output);
		}
		else if (count == 2 && previousCount == 2)
		{
			Scroll(x - previousX, y - previousY, output);
		}
		else if (count == 0 && previousCount > 0)
		{
			EndGesture(now, output);
		}

		previousCount = count;
		previousId = first.Id;
		previousX = x;
		previousY = y;
	}

	private void Move(double dx, double dy, IPcOutput output)
	{
		gestureTravel += Math.Abs(dx) + Math.Abs(dy);
		pendingX += dx * Speed;
		pendingY += dy * Speed;
		int moveX = WholePart(pendingX);
		int moveY = WholePart(pendingY);
		if (moveX != 0 || moveY != 0)
		{
			pendingX -= moveX;
			pendingY -= moveY;
			output.MoveMouse(moveX, moveY);
		}
	}

	private void Scroll(double dx, double dy, IPcOutput output)
	{
		gestureTravel += Math.Abs(dx) + Math.Abs(dy);
		pendingScroll += dy;
		int ticks = WholePart(pendingScroll / ScrollUnitsPerTick);
		if (ticks != 0)
		{
			pendingScroll -= ticks * ScrollUnitsPerTick;
			output.Wheel(ticks);
		}
	}

	// A short, still touch without pressing the pad is a tap: one finger clicks, two fingers right-click.
	private void EndGesture(TimeSpan now, IPcOutput output)
	{
		pendingScroll = 0;
		if (pressedDuringGesture || now - gestureStart > TapMaxDuration || gestureTravel > TapMaxTravel)
		{
			return;
		}
		MouseButton button = gestureFingers >= 2 ? MouseButton.Right : MouseButton.Left;
		output.MouseButton(button, true);
		output.MouseButton(button, false);
	}

	// Truncates towards zero, tolerating floating-point error (5 x 0.6 is 3, not 2.999...).
	private static int WholePart(double value)
	{
		return (int)Math.Truncate(value + (value >= 0 ? 1e-9 : -1e-9));
	}
}
