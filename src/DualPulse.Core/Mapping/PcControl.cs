using DualPulse.Core.Protocol;

namespace DualPulse.Core.Mapping;

// What a controller button does in PC control: a mouse button or a key (names match PcKey).
public enum PcAction
{
	None,
	LeftClick,
	RightClick,
	MiddleClick,
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

// "PC control": the controller drives the mouse and keyboard. The touchpad and sticks move and scroll the pointer, and
// buttons send keys or mouse buttons (pressed and released with the button, so holding works). PS is reserved for
// the PS+Options switch: while it is held, buttons don't start sending anything.
//
// Two hold layers (Steam Input calls them mode shifts) change what other controls do while their button is held:
// - L1, window switcher: pressing it opens Alt+Tab; the D-pad or right stick (in steps) moves through windows, R1 is
//   next and L2 previous, Cross picks, Circle cancels, and letting go of L1 switches. A quick tap returns to the last
//   window.
// - R1, media: D-pad up/down and the right stick change the volume, left/right skip tracks, Cross plays/pauses and
//   Square mutes.
// While a layer is held the pointer and the button table are paused.
public sealed class PcControl
{
	public static readonly IReadOnlyDictionary<Buttons, PcAction> DefaultButtons = new Dictionary<Buttons, PcAction>
	{
		[Buttons.Cross] = PcAction.Enter,
		[Buttons.Circle] = PcAction.Escape,
		[Buttons.Square] = PcAction.Space,
		[Buttons.Triangle] = PcAction.Backspace,
		[Buttons.DpadUp] = PcAction.Up,
		[Buttons.DpadDown] = PcAction.Down,
		[Buttons.DpadLeft] = PcAction.Left,
		[Buttons.DpadRight] = PcAction.Right,
		[Buttons.R2] = PcAction.LeftClick,
		[Buttons.L2] = PcAction.RightClick,
		[Buttons.L3] = PcAction.MiddleClick,
		[Buttons.Options] = PcAction.Windows,
		[Buttons.Create] = PcAction.PlayPause,
		[Buttons.Mute] = PcAction.VolumeMute
	};

	// Buttons in report order, so presses in one report are sent in a stable order.
	private static readonly Buttons[] ButtonOrder = Enum.GetValues<Buttons>().Where(b => b != Buttons.None).ToArray();

	private const double StickStep = 0.6;

	private readonly List<(Buttons Button, PcAction Action)> held = [];

	private readonly Dictionary<PcKey, Repeater> repeaters = [];

	private Buttons previous;

	private bool switcherOpen;

	// Cross or Circle already closed the switcher; ignore the rest of this L1 hold.
	private bool switcherClosed;

	public TouchpadMouse Touchpad { get; } = new();

	public StickMouse Sticks { get; } = new();

	public bool TouchpadEnabled { get; set; } = true;

	public bool WindowSwitcherEnabled { get; set; } = true;

	public bool MediaLayerEnabled { get; set; } = true;

	public IReadOnlyDictionary<Buttons, PcAction> ButtonActions { get; set; } = DefaultButtons;

	public void Process(in InputSnapshot input, TimeSpan now, IPcOutput output)
	{
		Buttons buttons = input.Buttons;
		bool layerHeld = ProcessWindowSwitcher(input, now, output) || ProcessMediaLayer(input, now, output);
		if (!layerHeld)
		{
			if (TouchpadEnabled)
			{
				Touchpad.Process(input, now, output);
			}
			Sticks.Process(input, now, output);
			ProcessButtons(buttons, output);
		}
		previous = buttons;
	}

	// Lets go of every key and mouse button PC control is holding (when it is switched off).
	public void Release(IPcOutput output)
	{
		if (switcherOpen)
		{
			output.Key(PcKey.Alt, false);
			switcherOpen = false;
		}
		foreach ((_, PcAction action) in held)
		{
			Send(action, false, output);
		}
		held.Clear();
	}

	// Returns true while L1 is held as the switcher layer.
	private bool ProcessWindowSwitcher(in InputSnapshot input, TimeSpan now, IPcOutput output)
	{
		Buttons buttons = input.Buttons;
		if (!WindowSwitcherEnabled || (buttons & Buttons.Ps) != 0)
		{
			return false;
		}
		if ((buttons & Buttons.L1) == 0)
		{
			if (switcherOpen)
			{
				output.Key(PcKey.Alt, false);
			}
			switcherOpen = false;
			switcherClosed = false;
			return false;
		}
		if (Pressed(buttons, Buttons.L1))
		{
			output.Key(PcKey.Alt, true);
			Tap(PcKey.Tab, output);
			switcherOpen = true;
			switcherClosed = false;
			repeaters.Clear();
			return true;
		}
		if (!switcherOpen || switcherClosed)
		{
			return true;
		}
		if (Pressed(buttons, Buttons.Cross))
		{
			output.Key(PcKey.Alt, false);
			(switcherOpen, switcherClosed) = (false, true);
			return true;
		}
		if (Pressed(buttons, Buttons.Circle))
		{
			Tap(PcKey.Escape, output);
			output.Key(PcKey.Alt, false);
			(switcherOpen, switcherClosed) = (false, true);
			return true;
		}
		if (Pressed(buttons, Buttons.R1))
		{
			Tap(PcKey.Tab, output);
		}
		if (Pressed(buttons, Buttons.L2))
		{
			output.Key(PcKey.Shift, true);
			Tap(PcKey.Tab, output);
			output.Key(PcKey.Shift, false);
		}
		(double x, double y) = Stick(input);
		Step(PcKey.Left, (buttons & Buttons.DpadLeft) != 0 || x < -StickStep, now, output);
		Step(PcKey.Right, (buttons & Buttons.DpadRight) != 0 || x > StickStep, now, output);
		Step(PcKey.Up, (buttons & Buttons.DpadUp) != 0 || y < -StickStep, now, output);
		Step(PcKey.Down, (buttons & Buttons.DpadDown) != 0 || y > StickStep, now, output);
		return true;
	}

	// Returns true while R1 is held as the media layer.
	private bool ProcessMediaLayer(in InputSnapshot input, TimeSpan now, IPcOutput output)
	{
		Buttons buttons = input.Buttons;
		if (!MediaLayerEnabled || (buttons & Buttons.R1) == 0 || (buttons & Buttons.Ps) != 0)
		{
			return false;
		}
		if (Pressed(buttons, Buttons.R1))
		{
			repeaters.Clear();
		}
		(_, double y) = Stick(input);
		Step(PcKey.VolumeUp, (buttons & Buttons.DpadUp) != 0 || y < -StickStep, now, output);
		Step(PcKey.VolumeDown, (buttons & Buttons.DpadDown) != 0 || y > StickStep, now, output);
		if (Pressed(buttons, Buttons.DpadRight))
		{
			Tap(PcKey.NextTrack, output);
		}
		if (Pressed(buttons, Buttons.DpadLeft))
		{
			Tap(PcKey.PreviousTrack, output);
		}
		if (Pressed(buttons, Buttons.Cross))
		{
			Tap(PcKey.PlayPause, output);
		}
		if (Pressed(buttons, Buttons.Square))
		{
			Tap(PcKey.VolumeMute, output);
		}
		return true;
	}

	private bool Pressed(Buttons current, Buttons button) => (current & button) != 0 && (previous & button) == 0;

	// Sends the key once when the direction starts, then repeats while it is held (like a held arrow key).
	private void Step(PcKey key, bool active, TimeSpan now, IPcOutput output)
	{
		if (!repeaters.TryGetValue(key, out Repeater? repeater))
		{
			repeater = new Repeater();
			repeaters[key] = repeater;
		}
		if (repeater.Update(active, now))
		{
			Tap(key, output);
		}
	}

	private static void Tap(PcKey key, IPcOutput output)
	{
		output.Key(key, true);
		output.Key(key, false);
	}

	private static (double X, double Y) Stick(in InputSnapshot input)
	{
		return ((input.RightX - 128) / 127.5, (input.RightY - 128) / 127.5);
	}

	private void ProcessButtons(Buttons current, IPcOutput output)
	{
		bool psHeld = (current & Buttons.Ps) != 0;
		for (int i = held.Count - 1; i >= 0; i--)
		{
			if ((current & held[i].Button) == 0)
			{
				Send(held[i].Action, false, output);
				held.RemoveAt(i);
			}
		}
		if (psHeld)
		{
			return;
		}
		foreach (Buttons button in ButtonOrder)
		{
			if ((current & button) != 0 && ButtonActions.TryGetValue(button, out PcAction action) && action != PcAction.None
				&& !held.Exists(h => h.Button == button))
			{
				held.Add((button, action));
				Send(action, true, output);
			}
		}
	}

	private static void Send(PcAction action, bool down, IPcOutput output)
	{
		switch (action)
		{
		case PcAction.LeftClick:
			output.MouseButton(MouseButton.Left, down);
			break;
		case PcAction.RightClick:
			output.MouseButton(MouseButton.Right, down);
			break;
		case PcAction.MiddleClick:
			output.MouseButton(MouseButton.Middle, down);
			break;
		default:
			if (Enum.TryParse(action.ToString(), out PcKey key))
			{
				output.Key(key, down);
			}
			break;
		}
	}
}

// Fires when something starts being held, then repeatedly after a short delay while it stays held.
internal sealed class Repeater
{
	private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(350);

	private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(120);

	private TimeSpan? start;

	private TimeSpan last;

	public bool Update(bool held, TimeSpan now)
	{
		if (!held)
		{
			start = null;
			return false;
		}
		if (start is not { } since)
		{
			start = now;
			last = now;
			return true;
		}
		if (now - since >= InitialDelay && now - last >= Interval)
		{
			last = now;
			return true;
		}
		return false;
	}
}

// Recognises PS+Options being pressed (once per press) to switch PC control on and off.
public sealed class PcControlSwitch
{
	private const Buttons Chord = Buttons.Ps | Buttons.Options;

	private bool wasHeld;

	public bool Pressed(Buttons buttons)
	{
		bool held = (buttons & Chord) == Chord;
		bool pressed = held && !wasHeld;
		wasHeld = held;
		return pressed;
	}
}
