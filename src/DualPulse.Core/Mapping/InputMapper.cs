using DualPulse.Core.Protocol;

namespace DualPulse.Core.Mapping;

public static class InputMapper
{
	private static readonly (Buttons From, XboxButtons To)[] XboxButtonMap =
	[
		(Buttons.Cross, XboxButtons.A),
		(Buttons.Circle, XboxButtons.B),
		(Buttons.Square, XboxButtons.X),
		(Buttons.Triangle, XboxButtons.Y),
		(Buttons.L1, XboxButtons.LeftShoulder),
		(Buttons.R1, XboxButtons.RightShoulder),
		(Buttons.Create, XboxButtons.Back),
		(Buttons.Options, XboxButtons.Start),
		(Buttons.Ps, XboxButtons.Guide),
		(Buttons.L3, XboxButtons.LeftThumb),
		(Buttons.R3, XboxButtons.RightThumb),
		(Buttons.DpadUp, XboxButtons.Up),
		(Buttons.DpadDown, XboxButtons.Down),
		(Buttons.DpadLeft, XboxButtons.Left),
		(Buttons.DpadRight, XboxButtons.Right)
	];

	public static XboxPadState ToXbox(in InputSnapshot s)
	{
		XboxButtons buttons = XboxButtons.None;
		foreach ((Buttons from, XboxButtons to) in XboxButtonMap)
		{
			if ((s.Buttons & from) != 0)
			{
				buttons |= to;
			}
		}
		return new XboxPadState(Axis(s.LeftX), InvertedAxis(s.LeftY), Axis(s.RightX), InvertedAxis(s.RightY), s.L2, s.R2, buttons);
	}

	public static Ds4PadState ToDs4(in InputSnapshot s)
	{
		return new Ds4PadState(s.LeftX, s.LeftY, s.RightX, s.RightY, s.L2, s.R2, s.Buttons);
	}

	// 0..255 -> -32768..32767 (repeating the byte spreads it over the full 16-bit range).
	private static short Axis(byte value)
	{
		return (short)(((value << 8) | value) - 32768);
	}

	// DualSense Y grows downwards; Xbox Y grows upwards.
	private static short InvertedAxis(byte value)
	{
		return (short)(32767 - ((value << 8) | value));
	}
}
