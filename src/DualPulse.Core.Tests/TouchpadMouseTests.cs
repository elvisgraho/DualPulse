using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

// Records what the mapping asked the OS to do.
internal sealed class RecordingOutput : IPcOutput
{
	public List<string> Events { get; } = [];

	public (int X, int Y) Moved { get; private set; }

	public int WheelTicks { get; private set; }

	public void MoveMouse(int dx, int dy)
	{
		Moved = (Moved.X + dx, Moved.Y + dy);
	}

	public void MouseButton(MouseButton button, bool down)
	{
		Events.Add($"{button} {(down ? "down" : "up")}");
	}

	public void Wheel(int ticks)
	{
		WheelTicks += ticks;
	}

	public void Key(PcKey key, bool down)
	{
		Events.Add($"{key} {(down ? "down" : "up")}");
	}
}

public sealed class TouchpadMouseTests
{
	private readonly TouchpadMouse touchpad = new() { Speed = 0.6 };

	private readonly RecordingOutput output = new();

	private TimeSpan now;

	private void Report(TouchPoint first, TouchPoint second = default, Buttons buttons = Buttons.None, int afterMs = 8)
	{
		now += TimeSpan.FromMilliseconds(afterMs);
		touchpad.Process(new InputSnapshot { Touch0 = first, Touch1 = second, Buttons = buttons }, now, output);
	}

	private static TouchPoint Finger(byte id, int x, int y) => new(true, id, (ushort)x, (ushort)y);

	private static readonly TouchPoint None = default;

	[Fact]
	public void OneFinger_MovesThePointerBySpeed()
	{
		Report(Finger(1, 100, 100));
		Report(Finger(1, 200, 150));

		Assert.Equal((60, 30), output.Moved);
	}

	[Fact]
	public void SmallMoves_AccumulateInsteadOfBeingLost()
	{
		Report(Finger(1, 100, 100));
		for (int i = 1; i <= 5; i++)
		{
			Report(Finger(1, 100 + i, 100));
		}

		Assert.Equal((3, 0), output.Moved);
	}

	[Fact]
	public void Tap_Clicks()
	{
		Report(Finger(1, 500, 500));
		Report(Finger(1, 502, 501), afterMs: 60);
		Report(None, afterMs: 60);

		Assert.Equal(["Left down", "Left up"], output.Events);
	}

	[Fact]
	public void LongTouch_DoesNotClick()
	{
		Report(Finger(1, 500, 500));
		Report(Finger(1, 500, 500), afterMs: 400);
		Report(None);

		Assert.Empty(output.Events);
	}

	[Fact]
	public void Moving_ThenLifting_DoesNotClick()
	{
		Report(Finger(1, 100, 100));
		Report(Finger(1, 300, 100), afterMs: 40);
		Report(None, afterMs: 40);

		Assert.Empty(output.Events);
	}

	[Fact]
	public void TwoFingerTap_RightClicks()
	{
		Report(Finger(1, 500, 500));
		Report(Finger(1, 500, 500), Finger(2, 800, 500), afterMs: 30);
		Report(None, afterMs: 80);

		Assert.Equal(["Right down", "Right up"], output.Events);
	}

	[Fact]
	public void TwoFingerDrag_ScrollsWithTheFingers_WithoutMovingThePointer()
	{
		Report(Finger(1, 500, 300), Finger(2, 800, 300));
		Report(Finger(1, 500, 360), Finger(2, 800, 360));
		Report(Finger(1, 500, 420), Finger(2, 800, 420));

		Assert.Equal(2, output.WheelTicks);
		Assert.Equal((0, 0), output.Moved);
	}

	[Fact]
	public void Press_HoldsTheLeftButton_ForDragging()
	{
		Report(Finger(1, 500, 500));
		Report(Finger(1, 500, 500), buttons: Buttons.Touchpad);
		Report(Finger(1, 600, 500), buttons: Buttons.Touchpad);
		Report(Finger(1, 600, 500));
		Report(None);

		Assert.Equal(["Left down", "Left up"], output.Events);
		Assert.Equal((60, 0), output.Moved);
	}

	[Fact]
	public void NewFinger_DoesNotJumpThePointer()
	{
		Report(Finger(1, 100, 100));
		Report(Finger(2, 1800, 900));
		Report(Finger(2, 1810, 900));

		Assert.Equal((6, 0), output.Moved);
	}

	[Fact]
	public void Press_OnTheRightHalf_RightClicks_LikeALaptopTouchpad()
	{
		Report(Finger(1, 1500, 500));
		Report(Finger(1, 1500, 500), buttons: Buttons.Touchpad);
		// The finger drifting across the middle while pressed doesn't change the button being held.
		Report(Finger(1, 900, 500), buttons: Buttons.Touchpad);
		Report(Finger(1, 900, 500));

		Assert.Equal(["Right down", "Right up"], output.Events);
	}

	[Fact]
	public void Press_WithoutAFinger_IsALeftClick()
	{
		Report(None, buttons: Buttons.Touchpad);
		Report(None);

		Assert.Equal(["Left down", "Left up"], output.Events);
	}
}
