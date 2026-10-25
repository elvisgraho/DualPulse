namespace DualPulse.Core.Protocol;

// X is 0-1919, Y is 0-1079 on the touchpad surface.
public readonly record struct TouchPoint(bool Active, byte Id, ushort X, ushort Y);
