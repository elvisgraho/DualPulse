using DualPulse.Core.Protocol;

namespace DualPulse.Core.Mapping;

[Flags]
public enum XboxButtons : ushort
{
	None = 0,
	A = 1 << 0,
	B = 1 << 1,
	X = 1 << 2,
	Y = 1 << 3,
	LeftShoulder = 1 << 4,
	RightShoulder = 1 << 5,
	Back = 1 << 6,
	Start = 1 << 7,
	Guide = 1 << 8,
	LeftThumb = 1 << 9,
	RightThumb = 1 << 10,
	Up = 1 << 11,
	Down = 1 << 12,
	Left = 1 << 13,
	Right = 1 << 14
}

// Xbox 360 pad report: thumbsticks -32768..32767 with Y up positive, triggers 0..255.
public readonly record struct XboxPadState(short LeftX, short LeftY, short RightX, short RightY, byte LeftTrigger, byte RightTrigger, XboxButtons Buttons);

// DualShock 4 pad report: same axis orientation as the DualSense; Create is the DS4's Share button.
public readonly record struct Ds4PadState(byte LeftX, byte LeftY, byte RightX, byte RightY, byte L2, byte R2, Buttons Buttons);
