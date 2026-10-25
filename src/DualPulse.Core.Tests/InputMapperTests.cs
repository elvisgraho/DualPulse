using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public class InputMapperTests
{
	private static InputSnapshot Snapshot(byte lx = 128, byte ly = 128, byte rx = 128, byte ry = 128, byte l2 = 0, byte r2 = 0, Buttons buttons = Buttons.None)
	{
		return new InputSnapshot(Transport.Usb, lx, ly, rx, ry, l2, r2, buttons, 0, 0, 0, 0, 0, 0, 0, default, default, 0, null);
	}

	[Fact]
	public void Xbox_StickExtremes()
	{
		Assert.Equal(-32768, InputMapper.ToXbox(Snapshot(lx: 0)).LeftX);
		Assert.Equal(32767, InputMapper.ToXbox(Snapshot(lx: 255)).LeftX);
		Assert.Equal(32767, InputMapper.ToXbox(Snapshot(ly: 0)).LeftY);
		Assert.Equal(-32768, InputMapper.ToXbox(Snapshot(ly: 255)).LeftY);
		Assert.Equal(32767, InputMapper.ToXbox(Snapshot(rx: 255)).RightX);
		Assert.Equal(-32768, InputMapper.ToXbox(Snapshot(ry: 255)).RightY);
	}

	[Theory]
	[InlineData(Buttons.Cross, XboxButtons.A)]
	[InlineData(Buttons.Circle, XboxButtons.B)]
	[InlineData(Buttons.Square, XboxButtons.X)]
	[InlineData(Buttons.Triangle, XboxButtons.Y)]
	[InlineData(Buttons.L1, XboxButtons.LeftShoulder)]
	[InlineData(Buttons.R1, XboxButtons.RightShoulder)]
	[InlineData(Buttons.Create, XboxButtons.Back)]
	[InlineData(Buttons.Options, XboxButtons.Start)]
	[InlineData(Buttons.Ps, XboxButtons.Guide)]
	[InlineData(Buttons.L3, XboxButtons.LeftThumb)]
	[InlineData(Buttons.R3, XboxButtons.RightThumb)]
	[InlineData(Buttons.DpadUp | Buttons.DpadRight, XboxButtons.Up | XboxButtons.Right)]
	[InlineData(Buttons.DpadDown | Buttons.DpadLeft, XboxButtons.Down | XboxButtons.Left)]
	public void Xbox_ButtonsMap(Buttons input, XboxButtons expected)
	{
		Assert.Equal(expected, InputMapper.ToXbox(Snapshot(buttons: input)).Buttons);
	}

	[Fact]
	public void Xbox_Triggers_PassThrough()
	{
		XboxPadState pad = InputMapper.ToXbox(Snapshot(l2: 17, r2: 250));

		Assert.Equal(17, pad.LeftTrigger);
		Assert.Equal(250, pad.RightTrigger);
	}

	[Fact]
	public void Ds4_CopiesAxesAndButtons()
	{
		Ds4PadState pad = InputMapper.ToDs4(Snapshot(lx: 1, ly: 2, rx: 3, ry: 4, l2: 5, r2: 6, buttons: Buttons.Cross | Buttons.Create | Buttons.Touchpad));

		Assert.Equal(new Ds4PadState(1, 2, 3, 4, 5, 6, Buttons.Cross | Buttons.Create | Buttons.Touchpad), pad);
	}
}
