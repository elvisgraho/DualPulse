using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class EmulationViewModelTests
{
	// Starts in Xbox 360 mode, which these tests use (new installs start in Native).
	private readonly FakeSettingsService settings = new(new AppSettings(Emulation: PadKind.Xbox360));

	private readonly FakeEmulationService emulation = new();

	private readonly FakePadBackend backend = new();

	private readonly FakeHidHideService hidHide = new();

	private readonly List<string> opened = [];

	private EmulationViewModel CreateViewModel(IEmulationService? service = null) =>
		new(settings, service ?? emulation, backend, hidHide, () => "1.22.0.0", opened.Add);

	[Fact]
	public async Task Emulation_ChangingChoice_PersistsAndRestartsPad()
	{
		FakeHidTransport transport = new();
		using ControllerService controller = new(transport, new FakeTimeProvider(), settings, NullLogger<ControllerManager>.Instance);
		using RumbleService rumble = new(controller, settings, new FakeTimeProvider());
		using EmulationService real = new(controller, backend, settings, rumble, NullLogger<EmulationService>.Instance, padWorker: work => work());
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		EmulationViewModel viewModel = CreateViewModel(real);
		Assert.Equal(["Xbox 360", "DualShock 4", "DualSense", "Native"], viewModel.EmulationChoices.Select(c => c.Label));

		viewModel.SelectedEmulation = viewModel.EmulationChoices[1];

		Assert.Equal(PadKind.DualShock4, settings.Current.Emulation);
		Assert.True(backend.Created[0].Disposed);
		Assert.Equal(PadKind.DualShock4, backend.Created[^1].Kind);

		viewModel.SelectedEmulation = viewModel.EmulationChoices[3];

		Assert.Null(settings.Current.Emulation);
		Assert.Equal(EmulationStatus.Off, real.Status);
	}

	[Theory]
	[InlineData(EmulationStatus.Active, "Active")]
	[InlineData(EmulationStatus.Off, "Off")]
	[InlineData(EmulationStatus.DriverMissing, "Driver missing")]
	public void Emulation_StatusText(EmulationStatus status, string text)
	{
		emulation.Status = status;
		EmulationViewModel viewModel = CreateViewModel();

		viewModel.Refresh();

		Assert.Equal(text, viewModel.StatusText);
	}

	[Fact]
	public void EachMode_SaysInShortWhatGamesGet()
	{
		EmulationViewModel viewModel = CreateViewModel();

		settings.Update(s => s with { Emulation = null });
		Assert.Equal(["Games use your DualSense as it is.", "DualPulse's stick, button and gyro settings don't reach games."], viewModel.ModePoints);

		settings.Update(s => s with { Emulation = PadKind.DualSense });
		Assert.Equal(["Adaptive triggers, haptics and the speaker, also over Bluetooth.", "Your stick, button and gyro settings work in games."], viewModel.ModePoints);

		settings.Update(s => s with { Emulation = PadKind.Xbox360 });
		Assert.Equal(["For games without DualSense support.", "Your stick, button and gyro settings work in games."], viewModel.ModePoints);
	}

	[Fact]
	public void Driver_Installed_ShowsVersion()
	{
		EmulationViewModel viewModel = CreateViewModel();

		viewModel.Refresh();

		Assert.Equal("ViGEmBus 1.22.0.0 installed", viewModel.DriverText);
		Assert.False(viewModel.DriverMissing);
	}

	[Fact]
	public void Driver_Missing_OffersTheOfficialReleasePage()
	{
		backend.IsAvailable = false;
		EmulationViewModel viewModel = CreateViewModel();

		viewModel.Refresh();
		viewModel.GetViGEmBusCommand.Execute(null);

		Assert.True(viewModel.DriverMissing);
		Assert.Equal("ViGEmBus isn't installed", viewModel.DriverText);
		Assert.Equal(["https://github.com/nefarius/ViGEmBus/releases"], opened);
	}

	[Fact]
	public void DualSense_WithoutUsbIpWin2_SaysItIsMissing_AndShowsTheHint()
	{
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		emulation.Status = EmulationStatus.DriverMissing;
		backend.DualSenseAvailable = false;
		EmulationViewModel viewModel = CreateViewModel();

		viewModel.Refresh();

		Assert.Equal("usbip-win2 isn't installed", viewModel.StatusText);
		Assert.True(viewModel.ShowDualSenseHint);
	}

	[Fact]
	public void DualSense_WithoutUsbIpWin2_OffersItsReleasePage()
	{
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		backend.DualSenseAvailable = false;
		EmulationViewModel viewModel = CreateViewModel();

		viewModel.Refresh();
		viewModel.GetUsbIpCommand.Execute(null);

		Assert.True(viewModel.ShowUsbIp);
		Assert.True(viewModel.UsbIpMissing);
		Assert.Equal("usbip-win2 isn't installed", viewModel.UsbIpText);
		Assert.Equal(["https://github.com/vadimgrn/usbip-win2/releases"], opened);
	}

	[Theory]
	[InlineData(PadKind.Xbox360, false)]
	[InlineData(PadKind.DualSense, true)]
	[InlineData(null, false)]
	public void UsbIpLine_ShowsOnlyForTheDualSense(PadKind? kind, bool shown)
	{
		settings.Update(s => s with { Emulation = kind });
		EmulationViewModel viewModel = CreateViewModel();

		Assert.Equal(shown, viewModel.ShowUsbIp);
		Assert.Equal("usbip-win2 installed", viewModel.UsbIpText);
		Assert.False(viewModel.UsbIpMissing);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task SteamRunning_ShowsTheSteamInputHint(bool running)
	{
		EmulationViewModel viewModel = new(settings, emulation, backend, hidHide, () => "1.22.0.0", opened.Add, () => running);

		// Looked up off the UI thread: finding a process takes a moment.
		await viewModel.RefreshSteamAsync();

		Assert.Equal(running, viewModel.ShowSteamHint);
	}

	[Theory]
	[InlineData(PadKind.Xbox360, true)]
	[InlineData(PadKind.DualSense, true)]
	[InlineData(null, false)]
	public void Remaps_ShowOnlyWithAVirtualController(PadKind? kind, bool shown)
	{
		settings.Update(s => s with { Emulation = kind });

		Assert.Equal(shown, CreateViewModel().ShowRemaps);
	}

	[Fact]
	public void GameControlSwitches_ShowWithTheDualSense_AndAreSaved()
	{
		settings.Update(s => s with { Emulation = PadKind.Xbox360 });
		EmulationViewModel viewModel = CreateViewModel();
		Assert.False(viewModel.ShowDualSenseHint);
		Assert.True(viewModel.GameLights);
		Assert.True(viewModel.GameTriggers);

		Assert.True(viewModel.VirtualSoundDevice);

		viewModel.GameLights = false;
		viewModel.GameTriggers = false;
		viewModel.VirtualSoundDevice = false;

		Assert.False(settings.Current.GameLights);
		Assert.False(settings.Current.GameTriggers);
		Assert.False(settings.Current.VirtualSoundDevice);
	}

	[Fact]
	public void Remaps_AddChangeAndRemove_AreSaved()
	{
		EmulationViewModel viewModel = CreateViewModel();
		Assert.Empty(viewModel.Remaps);

		viewModel.AddRemapCommand.Execute(null);
		RemapRow row = Assert.Single(viewModel.Remaps);
		row.From = row.FromChoices.Single(c => c.Button == Buttons.Create);
		row.To = row.ToChoices.Single(c => c.Label == "Nothing");
		Assert.Equal([new ButtonRemap(Buttons.Create, Buttons.None)], settings.Current.ButtonRemaps);

		row.RemoveCommand.Execute(null);
		Assert.Empty(viewModel.Remaps);
		Assert.Empty(settings.Current.ButtonRemaps ?? []);
	}

	[Fact]
	public void Remaps_OfferTheDigitalButtons_AndNothingAsATarget()
	{
		EmulationViewModel viewModel = CreateViewModel();
		viewModel.AddRemapCommand.Execute(null);
		RemapRow row = viewModel.Remaps[0];

		Assert.Contains(row.FromChoices, c => c.Label == "Cross");
		Assert.DoesNotContain(row.FromChoices, c => c.Button is Buttons.L2 or Buttons.R2);
		Assert.Equal("Nothing", row.ToChoices[^1].Label);
	}

	[Fact]
	public void DualSense_StartFailure_WithUsbIpWin2_SaysItCouldNotPlugIn()
	{
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		emulation.Status = EmulationStatus.DriverMissing;
		EmulationViewModel viewModel = CreateViewModel();

		viewModel.Refresh();

		Assert.Equal("Couldn't start the virtual controller", viewModel.StatusText);
	}

	[Theory]
	[InlineData(PadKind.Xbox360, true)]
	[InlineData(PadKind.DualShock4, true)]
	[InlineData(PadKind.DualSense, false)]
	[InlineData(null, false)]
	public void ViGEmBusLine_ShowsOnlyForTheChoicesThatUseIt(PadKind? kind, bool shown)
	{
		settings.Update(s => s with { Emulation = kind });

		Assert.Equal(shown, CreateViewModel().ShowViGEmBus);
	}

	[Fact]
	public void GameSound_Toggle_WritesTheSetting_AndShowsWithTheDualSense()
	{
		EmulationViewModel viewModel = CreateViewModel();
		Assert.False(viewModel.GameSoundOnController);

		viewModel.GameSoundOnController = true;

		Assert.True(settings.Current.GameSoundOnController);
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		Assert.True(viewModel.ShowDualSenseHint);
	}

	[Fact]
	public void Hide_Toggle_WritesTheSetting()
	{
		EmulationViewModel viewModel = CreateViewModel();

		viewModel.HideController = true;

		Assert.True(settings.Current.HideController);
	}

	[Theory]
	[InlineData(HidHideStatus.Off, "Other apps and games can see the DualSense.")]
	[InlineData(HidHideStatus.Hidden, "Hidden from other apps and games. If a game still sees it, reconnect the controller.")]
	[InlineData(HidHideStatus.Error, "Couldn't hide the controller. See the log.")]
	[InlineData(HidHideStatus.NotInstalled, "HidHide isn't installed.")]
	[InlineData(HidHideStatus.NotAttached, "HidHide isn't set up for controllers. Reinstall HidHide.")]
	public void Hide_StatusText(HidHideStatus status, string text)
	{
		hidHide.Status = status;
		EmulationViewModel viewModel = CreateViewModel();

		viewModel.Refresh();

		Assert.Equal(text, viewModel.HideStatusText);
		Assert.Equal(status is HidHideStatus.NotInstalled or HidHideStatus.NotAttached, viewModel.HidHideMissing);
	}

	[Fact]
	public void Hide_NotInstalled_OffersTheOfficialReleasePage()
	{
		hidHide.Status = HidHideStatus.NotInstalled;
		EmulationViewModel viewModel = CreateViewModel();

		viewModel.GetHidHideCommand.Execute(null);

		Assert.Equal(["https://github.com/nefarius/HidHide/releases"], opened);
	}
}
