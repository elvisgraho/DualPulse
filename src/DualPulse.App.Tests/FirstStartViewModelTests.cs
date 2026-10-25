using DualPulse.App.ViewModels;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class FirstStartViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakePadBackend backend = new();

	private readonly List<string> opened = [];

	private int finished;

	private FirstStartViewModel CreateViewModel()
	{
		FirstStartViewModel guide = new(settings, backend, opened.Add);
		guide.Finished += () => finished++;
		return guide;
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public void Picking_WithTheDriverThere_FinishesIt(int choice)
	{
		FirstStartViewModel guide = CreateViewModel();
		Assert.True(guide.IsVisible);
		Assert.Equal(["Xbox controller", "DualSense", "No virtual controller"], guide.Choices.Select(c => c.Label));

		guide.SelectedChoice = guide.Choices[choice];

		Assert.Equal(guide.Choices[choice].Kind, settings.Current.Emulation);
		Assert.False(guide.DriverMissing);
		Assert.True(settings.Current.SetupDone);
		Assert.False(guide.IsVisible);
		Assert.Equal(1, finished);
	}

	[Fact]
	public void PickingDualSense_WithoutUsbIpWin2_StaysOpenAndOffersIt()
	{
		backend.DualSenseAvailable = false;
		FirstStartViewModel guide = CreateViewModel();

		guide.SelectedChoice = guide.Choices[1];
		guide.GetDriverCommand.Execute(null);

		Assert.Equal(PadKind.DualSense, settings.Current.Emulation);
		Assert.True(guide.DriverMissing);
		Assert.Equal("Needs the free usbip-win2 driver.", guide.DriverText);
		Assert.Equal(["https://github.com/vadimgrn/usbip-win2/releases"], opened);
		Assert.Equal(0, finished);
	}

	[Fact]
	public void PickingXbox_WithViGEmBusMissing_StaysOpenAndOffersIt()
	{
		backend.IsAvailable = false;
		FirstStartViewModel guide = CreateViewModel();

		guide.SelectedChoice = guide.Choices[0];
		guide.GetDriverCommand.Execute(null);

		Assert.Equal(PadKind.Xbox360, settings.Current.Emulation);
		Assert.Equal("Needs the free ViGEmBus driver.", guide.DriverText);
		Assert.Equal(["https://github.com/nefarius/ViGEmBus/releases"], opened);
		Assert.Equal(0, finished);
	}

	[Fact]
	public void DriverInstalledWhileOpen_FinishesIt()
	{
		backend.DualSenseAvailable = false;
		FirstStartViewModel guide = CreateViewModel();
		guide.SelectedChoice = guide.Choices[1];

		guide.Recheck();
		Assert.Equal(0, finished);
		backend.DualSenseAvailable = true;
		guide.Recheck();

		Assert.False(guide.DriverMissing);
		Assert.Equal(1, finished);
		Assert.True(settings.Current.SetupDone);
	}

	[Fact]
	public void ClosingTheWindow_CountsAsDone()
	{
		FirstStartViewModel guide = CreateViewModel();

		guide.Dismiss();

		Assert.True(settings.Current.SetupDone);
	}
}
