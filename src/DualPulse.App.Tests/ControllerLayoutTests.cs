using Avalonia;
using DualPulse.App.Controls;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ControllerLayoutTests
{
	[Fact]
	public void Layout_StickDot_CentreAndExtremes()
	{
		Point centre = new(100, 50);

		Point middle = ControllerLayout.StickDot(128, 128, centre, 20);
		Point upLeft = ControllerLayout.StickDot(0, 0, centre, 20);
		Point downRight = ControllerLayout.StickDot(255, 255, centre, 20);

		Assert.InRange(middle.X, 99, 101);
		Assert.InRange(middle.Y, 49, 51);
		Assert.InRange(upLeft.X, 79, 81);
		Assert.InRange(upLeft.Y, 29, 31);
		Assert.InRange(downRight.X, 119, 121);
		Assert.InRange(downRight.Y, 69, 71);
	}

	[Fact]
	public void Layout_TriggerFill_0To1()
	{
		Assert.Equal(0, ControllerLayout.TriggerFill(0));
		Assert.Equal(1, ControllerLayout.TriggerFill(255));
		Assert.InRange(ControllerLayout.TriggerFill(128), 0.49, 0.51);
	}

	[Fact]
	public void Layout_TouchPoint_MapsSurfaceToPad()
	{
		Rect pad = new(10, 20, 192, 108);

		Point topLeft = ControllerLayout.TouchPoint(new DualPulse.Core.Protocol.TouchPoint(true, 0, 0, 0), pad);
		Point bottomRight = ControllerLayout.TouchPoint(new DualPulse.Core.Protocol.TouchPoint(true, 0, 1919, 1079), pad);

		Assert.Equal(new Point(10, 20), topLeft);
		Assert.Equal(new Point(202, 128), bottomRight);
	}

	[Fact]
	public void Layout_PressedTouchpadArea_IsTheHalfUnderTheFinger()
	{
		Rect pad = new(0, 0, 100, 50);

		Assert.Equal(new Rect(50, 0, 50, 50), ControllerLayout.PressedTouchpadArea(new DualPulse.Core.Protocol.TouchPoint(true, 0, 1500, 300), default, pad));
		Assert.Equal(new Rect(0, 0, 50, 50), ControllerLayout.PressedTouchpadArea(new DualPulse.Core.Protocol.TouchPoint(true, 0, 200, 300), default, pad));
		Assert.Equal(pad, ControllerLayout.PressedTouchpadArea(default, default, pad));
	}
}
