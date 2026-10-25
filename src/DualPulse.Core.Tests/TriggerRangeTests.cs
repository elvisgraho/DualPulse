using DualPulse.Core.Mapping;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class TriggerRangeTests
{
	[Fact]
	public void Identity_WhenBothZero()
	{
		Assert.Equal(0, TriggerRange.Apply(0, 0, 0));
		Assert.Equal(128, TriggerRange.Apply(128, 0, 0));
		Assert.Equal(255, TriggerRange.Apply(255, 0, 0));
	}

	[Fact]
	public void Deadzone_SwallowsTheStart()
	{
		Assert.Equal(0, TriggerRange.Apply(51, 0.2, 0));
		Assert.Equal(255, TriggerRange.Apply(255, 0.2, 0));
		Assert.InRange(TriggerRange.Apply(153, 0.2, 0), 126, 130);
	}

	[Fact]
	public void StartAt_LiftsTheFirstTouch()
	{
		Assert.Equal(0, TriggerRange.Apply(0, 0, 0.2));
		Assert.True(TriggerRange.Apply(1, 0, 0.2) >= 51);
		Assert.Equal(255, TriggerRange.Apply(255, 0, 0.2));
	}

	[Fact]
	public void Both()
	{
		Assert.Equal(0, TriggerRange.Apply(25, 0.1, 0.2));
		Assert.True(TriggerRange.Apply(26, 0.1, 0.2) >= 51);
		Assert.Equal(255, TriggerRange.Apply(255, 0.1, 0.2));
	}

	[Fact]
	public void OutOfRange_IsClamped()
	{
		Assert.Equal(TriggerRange.Apply(100, 0.3, 0.3), TriggerRange.Apply(100, 2, 2));
		Assert.Equal(TriggerRange.Apply(100, 0, 0), TriggerRange.Apply(100, -1, -1));
	}
}
