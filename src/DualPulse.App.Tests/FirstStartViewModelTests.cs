using DualPulse.App.ViewModels;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class FirstStartViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakePadBackend backend = new();

	private readonly List<string> opened = [];

	private FirstStartViewModel CreateViewModel() => new(settings, backend, opened.Add);

	[Fact]
	public void Shows_UntilDone()
	{
		FirstStartViewModel guide = CreateViewModel();
		Assert.True(guide.IsVisible);
		Assert.Equal(["Xbox controller", "DualSense", "No virtual controller"], guide.Choices.Select(c => c.Label));

		int finished = 0;
		guide.Finished += () => finished++;

		guide.DoneCommand.Execute(null);

		Assert.False(guide.IsVisible);
		Assert.True(settings.Current.SetupDone);
		Assert.Equal(1, finished);
	}

	[Fact]
	public void ClosingTheWindow_CountsAsDone()
	{
		FirstStartViewModel guide = CreateViewModel();

		guide.Dismiss();

		Assert.True(settings.Current.SetupDone);
	}

	[Fact]
	public void ChoosingDualSense_WithoutUsbIpWin2_OffersIt()
	{
		backend.DualSenseAvailable = false;
		FirstStartViewModel guide = CreateViewModel();

		guide.ChooseCommand.Execute(guide.Choices[1]);
		guide.GetDriverCommand.Execute(null);

		Assert.Equal(PadKind.DualSense, settings.Current.Emulation);
		Assert.True(guide.DriverMissing);
		Assert.Equal("Needs the free usbip-win2 driver.", guide.DriverText);
		Assert.Equal(["https://github.com/vadimgrn/usbip-win2/releases"], opened);
	}

	[Fact]
	public void ChoosingXbox_WithViGEmBusMissing_OffersIt()
	{
		backend.IsAvailable = false;
		FirstStartViewModel guide = CreateViewModel();

		guide.ChooseCommand.Execute(guide.Choices[0]);
		guide.GetDriverCommand.Execute(null);

		Assert.Equal(PadKind.Xbox360, settings.Current.Emulation);
		Assert.Equal("Needs the free ViGEmBus driver.", guide.DriverText);
		Assert.Equal(["https://github.com/nefarius/ViGEmBus/releases"], opened);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(2)]
	public void Choosing_WithTheDriverThere_IsReady(int choice)
	{
		FirstStartViewModel guide = CreateViewModel();

		guide.ChooseCommand.Execute(guide.Choices[choice]);

		Assert.False(guide.DriverMissing);
		Assert.Equal("Ready.", guide.DriverText);
		Assert.Equal(guide.Choices[choice].Kind, settings.Current.Emulation);
	}
}
