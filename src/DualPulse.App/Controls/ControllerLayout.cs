using Avalonia;

namespace DualPulse.App.Controls;

// Maps raw input values onto the controller drawing.
public static class ControllerLayout
{
	private const double TouchpadWidth = 1919;

	private const double TouchpadHeight = 1079;

	// Stick axes are 0-255 with 128 at rest; 0 is left/up, matching screen coordinates.
	public static Point StickDot(byte x, byte y, Point centre, double radius)
	{
		return new Point(centre.X + (x - 128) / 128.0 * radius, centre.Y + (y - 128) / 128.0 * radius);
	}

	public static double TriggerFill(byte value)
	{
		return value / 255.0;
	}

	// The part of the pad a press clicks, matching PC control: the half under the finger, or the whole pad without one.
	public static Rect PressedTouchpadArea(Core.Protocol.TouchPoint first, Core.Protocol.TouchPoint second, Rect pad)
	{
		Core.Protocol.TouchPoint finger = first.Active ? first : second;
		if (!finger.Active)
		{
			return pad;
		}
		double half = pad.Width / 2;
		return finger.X >= 960 ? new Rect(pad.X + half, pad.Y, half, pad.Height) : new Rect(pad.X, pad.Y, half, pad.Height);
	}

	public static Point TouchPoint(Core.Protocol.TouchPoint point, Rect pad)
	{
		return new Point(pad.X + point.X / TouchpadWidth * pad.Width, pad.Y + point.Y / TouchpadHeight * pad.Height);
	}
}
