using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Emulation;

namespace DualPulse.App.ViewModels;

public sealed record EmulationChoice(string Label, PadKind? Kind);

// The Emulation page: which virtual controller games see, the driver it needs (ViGEmBus or usbip-win2), and hiding the
// real DualSense through HidHide. Missing drivers link to their official release pages; nothing is downloaded or
// installed here.
public sealed partial class EmulationViewModel : ObservableObject
{
	private const string ViGEmBusReleases = "https://github.com/nefarius/ViGEmBus/releases";

	private const string HidHideReleases = "https://github.com/nefarius/HidHide/releases";

	private const string UsbIpReleases = "https://github.com/vadimgrn/usbip-win2/releases";

	private readonly ISettingsService settings;

	private readonly IEmulationService emulation;

	private readonly IVirtualPadBackend backend;

	private readonly IHidHideService hidHide;

	private readonly Func<string?> driverVersion;

	private readonly Action<string> open;

	private readonly UiContext ui = new();

	public EmulationViewModel(ISettingsService settings, IEmulationService emulation, IVirtualPadBackend backend, IHidHideService hidHide,
		Func<string?> driverVersion, Action<string>? open = null)
	{
		this.settings = settings;
		this.emulation = emulation;
		this.backend = backend;
		this.hidHide = hidHide;
		this.driverVersion = driverVersion;
		this.open = open ?? OpenWithShell;
		settings.Changed += _ => ui.Run(() =>
		{
			OnPropertyChanged(nameof(SelectedEmulation));
			OnPropertyChanged(nameof(ShowDualSenseHint));
			OnPropertyChanged(nameof(ShowViGEmBus));
			OnPropertyChanged(nameof(ShowUsbIp));
			OnPropertyChanged(nameof(HideController));
			OnPropertyChanged(nameof(GameSoundOnController));
		});
		emulation.StatusChanged += () => ui.Run(Refresh);
		hidHide.StatusChanged += () => ui.Run(Refresh);
		Refresh();
	}

	public IReadOnlyList<EmulationChoice> EmulationChoices { get; } =
	[
		new("Xbox 360", PadKind.Xbox360),
		new("DualShock 4", PadKind.DualShock4),
		new("DualSense", PadKind.DualSense),
		new("Native", null)
	];

	[ObservableProperty]
	public partial string StatusText { get; private set; } = "";

	[ObservableProperty]
	public partial string DriverText { get; private set; } = "";

	[ObservableProperty]
	public partial bool DriverMissing { get; private set; }

	[ObservableProperty]
	public partial string UsbIpText { get; private set; } = "";

	[ObservableProperty]
	public partial bool UsbIpMissing { get; private set; }

	[ObservableProperty]
	public partial string HideStatusText { get; private set; } = "";

	// HidHide needs installing (or reinstalling): shows the button to its release page.
	[ObservableProperty]
	public partial bool HidHideMissing { get; private set; }

	// The switch stays usable unless HidHide is absent, so hiding can always be turned off.
	[ObservableProperty]
	public partial bool CanToggleHide { get; private set; }

	// The virtual DualSense needs usbip-win2; the page says so while it is chosen.
	public bool ShowDualSenseHint => settings.Current.Emulation == PadKind.DualSense;

	// ViGEmBus only matters for the Xbox 360 and DualShock 4 choices.
	public bool ShowViGEmBus => settings.Current.Emulation is PadKind.Xbox360 or PadKind.DualShock4;

	// usbip-win2 only matters for the virtual DualSense.
	public bool ShowUsbIp => settings.Current.Emulation == PadKind.DualSense;

	public EmulationChoice SelectedEmulation
	{
		get => EmulationChoices.First(c => c.Kind == settings.Current.Emulation);
		set
		{
			if (value.Kind != settings.Current.Emulation)
			{
				settings.Update(s => s with { Emulation = value.Kind });
			}
		}
	}

	// The game's controller-speaker sound on the real controller (DualSense mode).
	public bool GameSoundOnController
	{
		get => settings.Current.GameSoundOnController;
		set
		{
			if (value != settings.Current.GameSoundOnController)
			{
				settings.Update(s => s with { GameSoundOnController = value });
			}
		}
	}

	public bool HideController
	{
		get => settings.Current.HideController;
		set
		{
			if (value != settings.Current.HideController)
			{
				settings.Update(s => s with { HideController = value });
			}
		}
	}

	public void Refresh()
	{
		StatusText = emulation.Status switch
		{
			EmulationStatus.Active => "Active",
			EmulationStatus.DriverMissing when settings.Current.Emulation == PadKind.DualSense =>
				backend.IsAvailableFor(PadKind.DualSense) ? "Couldn't start the virtual controller" : "usbip-win2 isn't installed",
			EmulationStatus.DriverMissing => "Driver missing",
			_ => "Off"
		};
		DriverMissing = !backend.IsAvailable;
		string? version = driverVersion();
		DriverText = DriverMissing ? "ViGEmBus isn't installed" : version == null ? "ViGEmBus installed" : $"ViGEmBus {version} installed";
		UsbIpMissing = !backend.IsAvailableFor(PadKind.DualSense);
		UsbIpText = UsbIpMissing ? "usbip-win2 isn't installed" : "usbip-win2 installed";
		HidHideMissing = hidHide.Status is HidHideStatus.NotInstalled or HidHideStatus.NotAttached;
		CanToggleHide = hidHide.Status != HidHideStatus.NotInstalled;
		HideStatusText = hidHide.Status switch
		{
			HidHideStatus.Hidden => "Hidden from other apps and games. If a game still sees it, reconnect the controller.",
			HidHideStatus.Error => "Couldn't hide the controller. See the log.",
			HidHideStatus.NotInstalled => "HidHide isn't installed.",
			HidHideStatus.NotAttached => "HidHide isn't set up for controllers. Reinstall HidHide.",
			_ => "Other apps and games can see the DualSense."
		};
	}

	[RelayCommand]
	private void GetViGEmBus() => Open(ViGEmBusReleases);

	[RelayCommand]
	private void GetHidHide() => Open(HidHideReleases);

	[RelayCommand]
	private void GetUsbIp() => Open(UsbIpReleases);

	private void Open(string target)
	{
		try
		{
			open(target);
		}
		catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
		{
		}
	}

	private static void OpenWithShell(string target)
	{
		Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
	}
}
