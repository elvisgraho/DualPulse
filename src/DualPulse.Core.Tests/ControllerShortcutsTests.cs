using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class ControllerShortcutsTests
{
	private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

	[Fact]
	public void PsWithTheDpad_GivesTheShortcuts_OncePerPress()
	{
		ShortcutRecognizer shortcuts = new();

		Assert.Null(shortcuts.Next(Buttons.Ps));
		Assert.Equal(Shortcut.NextProfile, shortcuts.Next(Buttons.Ps | Buttons.DpadRight));
		Assert.Null(shortcuts.Next(Buttons.Ps | Buttons.DpadRight));
		Assert.Null(shortcuts.Next(Buttons.Ps));
		Assert.Equal(Shortcut.PreviousProfile, shortcuts.Next(Buttons.Ps | Buttons.DpadLeft));
		Assert.Null(shortcuts.Next(Buttons.Ps));
		Assert.Equal(Shortcut.NextTriggers, shortcuts.Next(Buttons.Ps | Buttons.DpadUp));
		Assert.Null(shortcuts.Next(Buttons.Ps));
		Assert.Equal(Shortcut.PreviousTriggers, shortcuts.Next(Buttons.Ps | Buttons.DpadDown));
	}

	[Fact]
	public void TheDpadWithoutPs_IsNoShortcut()
	{
		ShortcutRecognizer shortcuts = new();

		Assert.Null(shortcuts.Next(Buttons.DpadRight));
		// PS pressed while the D-pad is already held isn't a shortcut either.
		Assert.Null(shortcuts.Next(Buttons.Ps | Buttons.DpadRight));
	}

	[Fact]
	public void PsTap_ReachesTheGame_WhenLetGo()
	{
		PsGate gate = new();

		Assert.Equal(Buttons.None, gate.Filter(Buttons.Ps, Ms(0)));
		Assert.Equal(Buttons.Ps, gate.Filter(Buttons.None, Ms(80)));
		Assert.Equal(Buttons.Ps, gate.Filter(Buttons.None, Ms(150)));
		Assert.Equal(Buttons.None, gate.Filter(Buttons.None, Ms(300)));
	}

	[Fact]
	public void PsWithAShortcut_NeverReachesTheGame_NorDoesTheDpad()
	{
		PsGate gate = new();

		gate.Filter(Buttons.Ps, Ms(0));
		Assert.Equal(Buttons.None, gate.Filter(Buttons.Ps | Buttons.DpadRight, Ms(40)));
		// Letting go of PS first: the D-pad stays hidden until it's let go too.
		Assert.Equal(Buttons.None, gate.Filter(Buttons.DpadRight, Ms(80)));
		Assert.Equal(Buttons.None, gate.Filter(Buttons.None, Ms(120)));
		Assert.Equal(Buttons.None, gate.Filter(Buttons.None, Ms(400)));
		Assert.Equal(Buttons.DpadRight, gate.Filter(Buttons.DpadRight, Ms(500)));
	}

	[Fact]
	public void PsWithTriangleOrOptions_IsHiddenToo()
	{
		// PS+Triangle turns the controller off, PS+Options switches PC control.
		PsGate gate = new();

		gate.Filter(Buttons.Ps, Ms(0));
		Assert.Equal(Buttons.None, gate.Filter(Buttons.Ps | Buttons.Triangle, Ms(30)));
		Assert.Equal(Buttons.None, gate.Filter(Buttons.None, Ms(60)));
	}

	[Fact]
	public void PsHeldOnItsOwn_PassesAfterHalfASecond()
	{
		PsGate gate = new();

		Assert.Equal(Buttons.None, gate.Filter(Buttons.Ps, Ms(0)));
		Assert.Equal(Buttons.None, gate.Filter(Buttons.Ps, Ms(400)));
		Assert.Equal(Buttons.Ps, gate.Filter(Buttons.Ps, Ms(520)));
		Assert.Equal(Buttons.None, gate.Filter(Buttons.None, Ms(700)));
	}

	[Fact]
	public void PsWithAnotherButton_PassesAtOnce()
	{
		// Steam and games have PS chords of their own.
		PsGate gate = new();

		gate.Filter(Buttons.Ps, Ms(0));

		Assert.Equal(Buttons.Ps | Buttons.Cross, gate.Filter(Buttons.Ps | Buttons.Cross, Ms(40)));
		Assert.Equal(Buttons.Ps, gate.Filter(Buttons.Ps, Ms(80)));
		Assert.Equal(Buttons.None, gate.Filter(Buttons.None, Ms(120)));
	}

	[Fact]
	public void OtherButtons_PassUntouched()
	{
		PsGate gate = new();

		Assert.Equal(Buttons.Cross | Buttons.DpadUp, gate.Filter(Buttons.Cross | Buttons.DpadUp, Ms(0)));
	}
}
