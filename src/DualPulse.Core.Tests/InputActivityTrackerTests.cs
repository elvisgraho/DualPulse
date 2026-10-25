using System;
using DualPulse.Core.Features;
using Xunit;

namespace DualPulse.Core.Tests;

public class InputActivityTrackerTests
{
	private static readonly DateTime Start = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

	private static void Rest(InputActivityTracker tracker, DateTime at, byte lx = 128, byte buttons1 = 0)
	{
		tracker.Observe(lx, 128, 128, 128, 0, 0, 0x08, buttons1, 0, at);
	}

	[Fact]
	public void BecomesIdleAfterTimeoutWithoutInput()
	{
		InputActivityTracker tracker = new InputActivityTracker(Start);
		Rest(tracker, Start);

		Assert.False(tracker.IsIdle(TimeSpan.FromMinutes(15), Start.AddMinutes(14)));
		Assert.True(tracker.IsIdle(TimeSpan.FromMinutes(15), Start.AddMinutes(15)));
	}

	[Fact]
	public void SmallStickDriftDoesNotCountAsActivity()
	{
		InputActivityTracker tracker = new InputActivityTracker(Start);
		Rest(tracker, Start);

		Rest(tracker, Start.AddMinutes(10), lx: 140);

		Assert.Equal(Start, tracker.LastActivityUtc);
	}

	[Fact]
	public void StickMovementAndButtonPressesCountAsActivity()
	{
		InputActivityTracker tracker = new InputActivityTracker(Start);
		Rest(tracker, Start);

		Rest(tracker, Start.AddMinutes(5), lx: 200);
		Assert.Equal(Start.AddMinutes(5), tracker.LastActivityUtc);

		Rest(tracker, Start.AddMinutes(9), lx: 200, buttons1: 0x01);
		Assert.Equal(Start.AddMinutes(9), tracker.LastActivityUtc);
	}

	[Fact]
	public void ZeroTimeoutNeverIdles()
	{
		InputActivityTracker tracker = new InputActivityTracker(Start);

		Assert.False(tracker.IsIdle(TimeSpan.Zero, Start.AddDays(1)));
	}
}
