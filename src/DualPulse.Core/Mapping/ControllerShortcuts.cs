using DualPulse.Core.Protocol;

namespace DualPulse.Core.Mapping;

public enum Shortcut
{
	NextProfile,
	PreviousProfile,
	NextTriggers,
	PreviousTriggers
}

// Hold PS and press the D-pad: Right/Left switch profile, Up/Down the adaptive trigger preset. Once per press; the
// D-pad press has to come while PS is held.
public sealed class ShortcutRecognizer
{
	private Buttons previous;

	public Shortcut? Next(Buttons pressed)
	{
		Buttons edges = pressed & ~previous;
		previous = pressed;
		if ((pressed & Buttons.Ps) == 0)
		{
			return null;
		}
		return (edges & Buttons.DpadRight) != 0 ? Shortcut.NextProfile
			: (edges & Buttons.DpadLeft) != 0 ? Shortcut.PreviousProfile
			: (edges & Buttons.DpadUp) != 0 ? Shortcut.NextTriggers
			: (edges & Buttons.DpadDown) != 0 ? Shortcut.PreviousTriggers
			: null;
	}
}

// What a game gets of the PS button while it is DualPulse's modifier (virtual controllers only):
// - PS is held back while pressed. Let go on its own, the game gets a 100 ms PS tap then.
// - With one of DualPulse's combos (D-pad, Triangle, Options), neither PS nor that button reaches the game; the
//   button stays hidden until it is let go.
// - Pressed with any other button, or held on its own for half a second, PS passes through from then on, so the
//   game's and Steam's own PS chords and long presses still work.
public sealed class PsGate
{
	public const Buttons Combos = Buttons.DpadUp | Buttons.DpadDown | Buttons.DpadLeft | Buttons.DpadRight | Buttons.Triangle | Buttons.Options;

	private static readonly TimeSpan PassAfter = TimeSpan.FromMilliseconds(500);

	private static readonly TimeSpan TapLength = TimeSpan.FromMilliseconds(100);

	private Buttons previous;

	private Buttons hidden;

	private bool withholding;

	private bool passing;

	private bool comboUsed;

	private TimeSpan psDownAt;

	private TimeSpan? tapUntil;

	public Buttons Filter(Buttons pressed, TimeSpan now)
	{
		bool ps = (pressed & Buttons.Ps) != 0;
		bool psWasDown = (previous & Buttons.Ps) != 0;
		Buttons edges = pressed & ~previous;
		previous = pressed;
		if (ps && !psWasDown)
		{
			psDownAt = now;
			withholding = true;
			passing = false;
			comboUsed = false;
		}
		if (ps && withholding)
		{
			Buttons combo = edges & Combos;
			if (combo != 0)
			{
				comboUsed = true;
				hidden |= combo;
			}
			else if ((edges & ~(Buttons.Ps | Combos)) != 0 || (!comboUsed && now - psDownAt >= PassAfter))
			{
				withholding = false;
				passing = true;
			}
		}
		if (!ps && psWasDown)
		{
			if (withholding && !comboUsed)
			{
				tapUntil = now + TapLength;
			}
			withholding = false;
			passing = false;
		}
		Buttons output = pressed & ~hidden;
		hidden &= pressed;
		if (ps)
		{
			if (!passing)
			{
				output &= ~Buttons.Ps;
			}
		}
		else if (tapUntil is { } until && now <= until)
		{
			output |= Buttons.Ps;
		}
		else
		{
			tapUntil = null;
		}
		return output;
	}
}
