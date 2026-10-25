namespace DualPulse.Core.Mapping;

public enum MouseButton
{
	Left,
	Right,
	Middle
}

// Keys a controller button can send. Modifiers are separate keys, so e.g. Alt on one button and Tab on another give
// Alt+Tab.
public enum PcKey
{
	Enter,
	Escape,
	Space,
	Tab,
	Backspace,
	Delete,
	Up,
	Down,
	Left,
	Right,
	PageUp,
	PageDown,
	Home,
	End,
	Windows,
	Alt,
	Shift,
	Control,
	F11,
	PlayPause,
	NextTrack,
	PreviousTrack,
	VolumeUp,
	VolumeDown,
	VolumeMute
}

// Mouse and keyboard input sent to Windows. Wheel ticks are positive to scroll up.
public interface IPcOutput
{
	void MoveMouse(int dx, int dy);

	void MouseButton(MouseButton button, bool down);

	void Wheel(int ticks);

	void Key(PcKey key, bool down);
}
