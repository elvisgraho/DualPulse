using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class FirstStartViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakePadBackend backend = new();

	private readonly FakeHidHideService hidHide = new() { Status = HidHideStatus.Off };

	private readonly List<string> opened = [];

	private int finished;

	private FirstStartViewModel CreateViewModel()
	{
		FirstStartViewModel guide = new(settings, backend, hidHide, opened.Add);
		guide.Finished += () => finished++;
		return guide;
	}

	[Fact]
	public void Opens_SayingWhatEachChoiceNeeds()
	{
		backend.IsAvailable = false;

		FirstStartViewModel guide = CreateViewModel();

		Assert.True(guide.IsVisible);
		Assert.Equal(["Xbox controller", "DualSense", "No virtual controller"], guide.Choices.Select(c => c.Label));
		Assert.Equal(["Needs the free ViGEmBus driver", "Ready", "Ready"], guide.Choices.Select(c => c.Status));
		Assert.Equal([false, true, true], guide.Choices.Select(c => c.Ready));
		guide.GetDriverCommand.Execute(guide.Choices[0]);
		Assert.Equal(["https://github.com/nefarius/ViGEmBus/releases"], opened);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public void Picking_AReadyChoice_FinishesIt(int choice)
	{
		FirstStartViewModel guide = CreateViewModel();

		guide.SelectedChoice = guide.Choices[choice];

		Assert.Equal(guide.Choices[choice].Kind, settings.Current.Emulation);
		Assert.True(settings.Current.SetupDone);
		Assert.False(guide.IsVisible);
		Assert.Equal(1, finished);
	}

	[Fact]
	public void Picking_AChoiceThatNeedsADriver_WaitsForIt()
	{
		backend.DualSenseAvailable = false;
		FirstStartViewModel guide = CreateViewModel();

		guide.SelectedChoice = guide.Choices[1];
		guide.Recheck();
		Assert.Equal(PadKind.DualSense, settings.Current.Emulation);
		Assert.Equal("Needs the free usbip-win2 driver", guide.Choices[1].Status);
		Assert.Equal(0, finished);

		backend.DualSenseAvailable = true;
		guide.Recheck();

		Assert.Equal("Ready", guide.Choices[1].Status);
		Assert.Equal(1, finished);
		Assert.True(settings.Current.SetupDone);
	}

	[Theory]
	[InlineData(HidHideStatus.NotInstalled, false)]
	[InlineData(HidHideStatus.NotAttached, false)]
	[InlineData(HidHideStatus.Off, true)]
	[InlineData(HidHideStatus.Hidden, true)]
	public void HidHide_IsOfferedWhenMissing(HidHideStatus status, bool ready)
	{
		hidHide.Status = status;

		FirstStartViewModel guide = CreateViewModel();
		guide.GetHidHideCommand.Execute(null);

		Assert.Equal(ready, guide.HidHideReady);
		Assert.Equal(ready ? "HidHide is installed." : "Needs the free HidHide driver.", guide.HidHideStatus);
		Assert.Equal(["https://github.com/nefarius/HidHide/releases"], opened);
	}

	[Fact]
	public void ClosingTheWindow_CountsAsDone()
	{
		FirstStartViewModel guide = CreateViewModel();

		guide.Dismiss();

		Assert.True(settings.Current.SetupDone);
		Assert.Equal(0, finished);
	}
}
