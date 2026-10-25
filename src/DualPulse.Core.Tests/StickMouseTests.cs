using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class StickMouseTests
{
	private readonly StickMouse sticks = new() { Speed = 1, ScrollSpeed = 1 };

	private readonly RecordingOutput output = new();

	private TimeSpan now;

	// Holds the sticks for the given time in 8 ms reports (the first report only starts the clock).
	private void Hold(byte rightX, byte rightY, byte leftY = 128, int milliseconds = 1000)
	{
		InputSnapshot input = new() { LeftX = 128, LeftY = leftY, RightX = rightX, RightY = rightY };
		sticks.Process(input, now, output);
		for (int elapsed = 8; elapsed <= milliseconds; elapsed += 8)
		{
			now += TimeSpan.FromMilliseconds(8);
			sticks.Process(input, now, output);
		}
	}

	[Fact]
	public void Centred_AndInsideTheDeadzone_DoNotMove()
	{
		Hold(128, 128);
		Hold(138, 120);

		Assert.Equal((0, 0), output.Moved);
		Assert.Equal(0, output.WheelTicks);
	}

	[Fact]
	public void FullRight_ForOneSecond_MovesAboutTheFullSpeed()
	{
		Hold(255, 128);

		Assert.InRange(output.Moved.X, StickMouse.PixelsPerSecond - 20, StickMouse.PixelsPerSecond + 20);
		Assert.Equal(0, output.Moved.Y);
	}

	[Fact]
	public void HalfDeflection_IsMuchSlower_ForFineControl()
	{
		Hold(192, 128);

		Assert.InRange(output.Moved.X, 1, StickMouse.PixelsPerSecond / 4);
	}

	[Fact]
	public void Up_MovesUp()
	{
		Hold(128, 0, milliseconds: 200);

		Assert.True(output.Moved.Y < 0);
	}

	[Fact]
	public void LeftStickUp_ScrollsUp()
	{
		Hold(128, 128, leftY: 0);

		Assert.InRange(output.WheelTicks, StickMouse.TicksPerSecond - 1, StickMouse.TicksPerSecond + 1);
	}

	[Fact]
	public void LongGapBetweenReports_DoesNotJump()
	{
		InputSnapshot input = new() { LeftX = 128, LeftY = 128, RightX = 255, RightY = 128 };
		sticks.Process(input, now, output);
		now += TimeSpan.FromSeconds(1);
		sticks.Process(input, now, output);

		Assert.InRange(output.Moved.X, 1, StickMouse.PixelsPerSecond / 10);
	}
}
