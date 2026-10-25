using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class ButtonRemapTests
{
	[Fact]
	public void Remapped_ButtonSendsItsTarget_Only()
	{
		ButtonRemap[] map = [new(Buttons.Cross, Buttons.Circle)];

		Assert.Equal(Buttons.Circle, ButtonRemap.Apply(Buttons.Cross, map));
		// The target's own button still works.
		Assert.Equal(Buttons.Circle, ButtonRemap.Apply(Buttons.Circle, map));
		Assert.Equal(Buttons.Circle | Buttons.L1, ButtonRemap.Apply(Buttons.Cross | Buttons.L1, map));
	}

	[Fact]
	public void TwoButtons_CanSwap()
	{
		ButtonRemap[] map = [new(Buttons.Cross, Buttons.Circle), new(Buttons.Circle, Buttons.Cross)];

		Assert.Equal(Buttons.Circle, ButtonRemap.Apply(Buttons.Cross, map));
		Assert.Equal(Buttons.Cross, ButtonRemap.Apply(Buttons.Circle, map));
	}

	[Fact]
	public void Nothing_SwitchesAButtonOff()
	{
		ButtonRemap[] map = [new(Buttons.Create, Buttons.None)];

		Assert.Equal(Buttons.Options, ButtonRemap.Apply(Buttons.Create | Buttons.Options, map));
	}

	[Fact]
	public void NoRemaps_LeaveTheButtonsAlone()
	{
		Assert.Equal(Buttons.Cross | Buttons.DpadUp, ButtonRemap.Apply(Buttons.Cross | Buttons.DpadUp, []));
	}
}
