using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class StickShapeTests
{
	[Fact]
	public void InsideTheDeadzone_IsCentred()
	{
		Assert.Equal(((byte)128, (byte)128), StickShape.Apply(140, 120, deadzone: 0.15, fullTiltAt: 1));
	}

	[Fact]
	public void PastTheDeadzone_StartsFromZero_AndKeepsTheDirection()
	{
		// Just past a 20% deadzone pointing right: barely off centre, not a jump to 20%.
		(byte x, byte y) = StickShape.Apply(158, 128, deadzone: 0.2, fullTiltAt: 1);

		Assert.InRange(x, 128, 135);
		Assert.Equal(128, y);
	}

	[Fact]
	public void FullTiltAt_ReachesTheEdgeSooner()
	{
		// 90% of the way right with full tilt at 90%: the game gets the edge.
		(byte x, byte y) = StickShape.Apply(243, 128, deadzone: 0, fullTiltAt: 0.9);

		Assert.Equal(255, x);
		Assert.Equal(128, y);
	}

	[Fact]
	public void Diagonals_StayDiagonal()
	{
		(byte x, byte y) = StickShape.Apply(200, 56, deadzone: 0.1, fullTiltAt: 0.95);

		Assert.InRange(Math.Abs((x - 128) - (128 - y)), 0, 1);
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(128, 128)]
	[InlineData(255, 0)]
	[InlineData(17, 211)]
	public void NoShaping_LeavesTheStickAsItIs(byte x, byte y)
	{
		Assert.Equal((x, y), StickShape.Apply(x, y, deadzone: 0, fullTiltAt: 1));
	}

	[Fact]
	public void SuggestedDeadzone_CoversTheFurthestRestingDrift_PlusAMargin()
	{
		// Resting 2 steps off centre (about 1.6%), wobbling to 3: the deadzone must cover 3 steps (2.4%) plus the margin.
		(byte, byte)[] samples = [(128, 130), (128, 131), (129, 130), (128, 130)];

		int percent = StickShape.SuggestDeadzone(samples);

		Assert.Equal(5, percent);
	}

	[Fact]
	public void SuggestedDeadzone_ForAPerfectStick_IsJustTheMargin()
	{
		Assert.Equal(2, StickShape.SuggestDeadzone([(128, 128), (128, 128)]));
	}

	[Fact]
	public void SuggestedDeadzone_NeverExceedsTheSliderMaximum()
	{
		Assert.Equal(30, StickShape.SuggestDeadzone([(255, 128)]));
	}
}
