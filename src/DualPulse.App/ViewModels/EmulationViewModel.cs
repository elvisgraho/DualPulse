using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Core.Protocol;
using DualPulse.Emulation;

namespace DualPulse.App.ViewModels;

public sealed record EmulationChoice(string Label, PadKind? Kind);

// The Emulation page: which virtual controller games see, the driver it needs (ViGEmBus or usbip-win2), and hiding the
// real DualSense through HidHide. Missing drivers link to their official release pages; nothing is downloaded or
// installed here.
public sealed partial class EmulationViewModel : ObservableObject
{
	private readonly ISettingsService settings;

	private readonly IEmulationService emulation;

	private readonly IVirtualPadBackend backend;

	private readonly IHidHideService hidHide;

	private readonly Func<string?> driverVersion;

	private readonly Action<string> open;

	private readonly Func<bool> steamRunning;

	private readonly UiContext ui = new();

	public EmulationViewModel(ISettingsService settings, IEmulationService emulation, IVirtualPadBackend backend, IHidHideService hidHide,
		Func<string?> driverVersion, Action<string>? open = null, Func<bool>? steamRunning = null)
	{
		this.steamRunning = steamRunning ?? (() => false);
		this.settings = settings;
		this.emulation = emulation;
		this.backend = backend;
		this.hidHide = hidHide;
		this.driverVersion = driverVersion;
		this.open = open ?? Platform.Shell.Open;
		settings.Changed += _ => ui.Run(() =>
		{
			OnPropertyChanged(nameof(SelectedEmulation));
			OnPropertyChanged(nameof(ShowDualSenseHint));
			OnPropertyChanged(nameof(ShowViGEmBus));
			OnPropertyChanged(nameof(ShowUsbIp));
			OnPropertyChanged(nameof(ShowRemaps));
			OnPropertyChanged(nameof(HideController));
			OnPropertyChanged(nameof(GameSoundOnController));
			OnPropertyChanged(nameof(GameLights));
			OnPropertyChanged(nameof(GameTriggers));
		});
		foreach (ButtonRemap remap in settings.Current.ButtonRemaps ?? [])
		{
			Remaps.Add(new RemapRow(remap, SaveRemaps, RemoveRemap));
		}
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

	// Steam is running: its Steam Input can take the controller from a game, so the page says how to turn it off.
	[ObservableProperty]
	public partial bool ShowSteamHint { get; private set; }

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

	// Buttons sent to games as other buttons; only a virtual controller can change them.
	public ObservableCollection<RemapRow> Remaps { get; } = [];

	public bool ShowRemaps => settings.Current.Emulation != null;

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

	// In DualSense mode: whether a game sets the lights / adaptive triggers (off keeps the user's own).
	public bool GameLights
	{
		get => settings.Current.GameLights;
		set
		{
			if (value != settings.Current.GameLights)
			{
				settings.Update(s => s with { GameLights = value });
			}
		}
	}

	public bool GameTriggers
	{
		get => settings.Current.GameTriggers;
		set
		{
			if (value != settings.Current.GameTriggers)
			{
				settings.Update(s => s with { GameTriggers = value });
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

	// Finding the Steam process takes a moment, so it runs off the UI thread.
	public async Task RefreshSteamAsync()
	{
		ShowSteamHint = await Task.Run(steamRunning);
	}

	// A new row for the first button not changed yet, sending itself (no change) until a target is picked.
	[RelayCommand]
	private void AddRemap()
	{
		Core.Protocol.Buttons unused = RemapRow.Buttons.Select(c => c.Button).FirstOrDefault(b => Remaps.All(r => r.From.Button != b), RemapRow.Buttons[0].Button);
		Remaps.Add(new RemapRow(new ButtonRemap(unused, unused), SaveRemaps, RemoveRemap));
		SaveRemaps();
	}

	private void RemoveRemap(RemapRow row)
	{
		Remaps.Remove(row);
		SaveRemaps();
	}

	private void SaveRemaps()
	{
		settings.Update(s => s with { ButtonRemaps = [.. Remaps.Select(r => r.ToRemap())] });
	}

	[RelayCommand]
	private void GetViGEmBus() => Open(DriverPages.ViGEmBus);

	[RelayCommand]
	private void GetHidHide() => Open(DriverPages.HidHide);

	[RelayCommand]
	private void GetUsbIp() => Open(DriverPages.UsbIpWin2);

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

}
