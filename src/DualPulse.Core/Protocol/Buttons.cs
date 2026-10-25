namespace DualPulse.Core.Protocol;

[Flags]
public enum Buttons : uint
{
	None = 0,
	Square = 1u << 0,
	Cross = 1u << 1,
	Circle = 1u << 2,
	Triangle = 1u << 3,
	L1 = 1u << 4,
	R1 = 1u << 5,
	L2 = 1u << 6,
	R2 = 1u << 7,
	Create = 1u << 8,
	Options = 1u << 9,
	L3 = 1u << 10,
	R3 = 1u << 11,
	Ps = 1u << 12,
	Touchpad = 1u << 13,
	Mute = 1u << 14,
	DpadUp = 1u << 15,
	DpadDown = 1u << 16,
	DpadLeft = 1u << 17,
	DpadRight = 1u << 18,
	Fn1 = 1u << 19,
	Fn2 = 1u << 20,
	LeftPaddle = 1u << 21,
	RightPaddle = 1u << 22
}
