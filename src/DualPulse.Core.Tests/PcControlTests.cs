using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class PcControlTests
{
	private readonly PcControl control = new();

	private readonly RecordingOutput output = new();

	private TimeSpan now;

	private void Press(Buttons buttons, TouchPoint touch = default)
	{
		now += TimeSpan.FromMilliseconds(8);
		control.Process(new InputSnapshot { LeftX = 128, LeftY = 128, RightX = 128, RightY = 128, Buttons = buttons, Touch0 = touch }, now, output);
	}

	[Fact]
	public void Defaults_CoverTheEverydayKeys()
	{
		IReadOnlyDictionary<Buttons, PcAction> defaults = PcControl.DefaultButtons;

		Assert.Equal(PcAction.Enter, defaults[Buttons.Cross]);
		Assert.Equal(PcAction.Escape, defaults[Buttons.Circle]);
		Assert.Equal(PcAction.Up, defaults[Buttons.DpadUp]);
		Assert.Equal(PcAction.Windows, defaults[Buttons.Options]);
		// L1 and R1 belong to the window switcher and media hold layers.
		Assert.False(defaults.ContainsKey(Buttons.L1));
		Assert.False(defaults.ContainsKey(Buttons.R1));
		Assert.Equal(PcAction.LeftClick, defaults[Buttons.R2]);
		Assert.False(defaults.ContainsKey(Buttons.Ps));
	}

	[Fact]
	public void Button_SendsItsKeyDownAndUp()
	{
		Press(Buttons.Cross);
		Press(Buttons.None);

		Assert.Equal(["Enter down", "Enter up"], output.Events);
	}

	[Fact]
	public void MouseAction_PressesTheMouseButton()
	{
		Press(Buttons.R2);
		Press(Buttons.None);

		Assert.Equal(["Left down", "Left up"], output.Events);
	}

	[Fact]
	public void TwoButtons_GiveAModifierCombination()
	{
		control.WindowSwitcherEnabled = false;
		control.MediaLayerEnabled = false;
		control.ButtonActions = new Dictionary<Buttons, PcAction> { [Buttons.L1] = PcAction.Alt, [Buttons.R1] = PcAction.Tab };
		Press(Buttons.L1);
		Press(Buttons.L1 | Buttons.R1);
		Press(Buttons.L1);
		Press(Buttons.None);

		Assert.Equal(["Alt down", "Tab down", "Tab up", "Alt up"], output.Events);
	}

	[Fact]
	public void WhilePsIsHeld_ButtonsSendNothing()
	{
		Press(Buttons.Ps);
		Press(Buttons.Ps | Buttons.Options);
		Press(Buttons.None);

		Assert.Empty(output.Events);
	}

	[Fact]
	public void Release_LetsGoOfEverythingHeld()
	{
		Press(Buttons.Cross | Buttons.R2);

		control.Release(output);

		Assert.Equal(["Enter down", "Left down", "Enter up", "Left up"], output.Events);
	}

	[Fact]
	public void CustomMapping_ReplacesTheDefaults()
	{
		control.ButtonActions = new Dictionary<Buttons, PcAction> { [Buttons.Cross] = PcAction.PlayPause };

		Press(Buttons.Cross);
		Press(Buttons.Circle);

		// Circle is no longer mapped, so it sends nothing; Cross being let go releases its key.
		Assert.Equal(["PlayPause down", "PlayPause up"], output.Events);
	}

	[Fact]
	public void TouchpadDisabled_IgnoresTouches()
	{
		control.TouchpadEnabled = false;

		Press(Buttons.None, new TouchPoint(true, 1, 100, 100));
		Press(Buttons.None, new TouchPoint(true, 1, 400, 100));

		Assert.Equal((0, 0), output.Moved);
	}

	[Fact]
	public void Switch_PsPlusOptions_FiresOncePerPress()
	{
		PcControlSwitch toggle = new();

		Assert.False(toggle.Pressed(Buttons.Ps));
		Assert.True(toggle.Pressed(Buttons.Ps | Buttons.Options));
		Assert.False(toggle.Pressed(Buttons.Ps | Buttons.Options));
		Assert.False(toggle.Pressed(Buttons.None));
		Assert.True(toggle.Pressed(Buttons.Ps | Buttons.Options));
	}
}
