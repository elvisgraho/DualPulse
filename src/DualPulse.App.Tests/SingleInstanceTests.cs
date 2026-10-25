using Avalonia.Controls;
using DualPulse.App;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class SingleInstanceTests
{
	[Fact]
	public void SingleInstance_SecondLaunch_IsNotFirst_AndAsksFirstToShow()
	{
		string name = "dualpulse-test-" + Guid.NewGuid().ToString("N");
		using SingleInstance first = new(name);
		using ManualResetEventSlim shown = new();
		first.ShowRequested += shown.Set;

		using SingleInstance second = new(name);
		second.SignalFirst();

		Assert.True(first.IsFirst);
		Assert.False(second.IsFirst);
		Assert.True(shown.Wait(2000, TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData(WindowCloseReason.WindowClosing, false, false, true)]
	[InlineData(WindowCloseReason.WindowClosing, false, true, false)]
	[InlineData(WindowCloseReason.WindowClosing, true, false, false)]
	[InlineData(WindowCloseReason.OSShutdown, false, false, false)]
	[InlineData(WindowCloseReason.ApplicationShutdown, false, false, false)]
	public void Close_HidesToTrayOnlyWhenTheUserClosesTheWindow(WindowCloseReason reason, bool programmatic, bool quitting, bool hide)
	{
		Assert.Equal(hide, CloseBehavior.HideToTray(reason, programmatic, quitting));
	}
}
