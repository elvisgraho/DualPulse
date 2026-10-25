using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Features;
using DualPulse.Core.Protocol;
using DualPulse.Emulation;

namespace DualPulse.App.ViewModels;

// The Home page: live controller, connection status and quick actions. Status values refresh from Refresh() (a
// 500 ms timer and session changes). Snapshot follows the input: the read thread posts SampleInput() only when the
// input changed visibly, at most one post pending, so a resting controller costs the UI thread nothing.
public sealed partial class HomeViewModel : ObservableObject
{
	private readonly IControllerService controller;

	private readonly IEmulationService emulation;

	private readonly ISettingsService settings;

	private readonly IPowerService power;

	private readonly IPcControlService? pcControl;

	private readonly UiContext ui = new();

	// The controller type the tray's emulation switch goes back to after turning emulation off.
	private PadKind lastEmulation = PadKind.Xbox360;

	private InputSnapshot? lastPosted;

	private int samplePending;

	private readonly object sampleGate = new();

	public HomeViewModel(IControllerService controller, IEmulationService emulation, ISettingsService settings, IPowerService power,
		IPcControlService? pcControl = null)
	{
		this.pcControl = pcControl;
		if (pcControl != null)
		{
			pcControl.ActiveChanged += () => ui.Run(() => OnPropertyChanged(nameof(PcControlOn)));
		}
		this.controller = controller;
		this.emulation = emulation;
		this.settings = settings;
		this.power = power;
		settings.Changed += _ => ui.Run(() =>
		{
			OnPropertyChanged(nameof(SelectedTriggerPreset));
			OnPropertyChanged(nameof(EmulationOn));
			OnPropertyChanged(nameof(LightbarSwatches));
			RefreshNamedProfiles();
		});
		controller.SessionChanged += OnBackgroundChange;
		controller.SessionStarted += session => session.InputReceived += OnInput;
		if (controller.Session is { } current)
		{
			current.InputReceived += OnInput;
		}
		emulation.StatusChanged += OnBackgroundChange;
		RefreshNamedProfiles();
		Refresh();
	}

	public IReadOnlyList<SwatchChoice> LightbarSwatches => SwatchChoice.For(settings.Current.EffectiveLightbar);

	// Default and the named profiles, for switching by hand; only shown once a named profile exists. Updated in place
	// so the dropdown keeps its selection.
	public System.Collections.ObjectModel.ObservableCollection<ProfileChoice> NamedProfiles { get; } = [];

	public bool ShowProfilePicker => NamedProfiles.Count > 1;

	public ProfileChoice? ChosenProfile
	{
		get => NamedProfiles.FirstOrDefault(c => c.Exe == settings.Current.ChosenProfile) ?? NamedProfiles.FirstOrDefault();
		set
		{
			// The list control clears its selection while items change; that isn't the user choosing.
			if (value != null && value.Exe != settings.Current.ChosenProfile)
			{
				settings.Update(s => s with { ChosenProfile = value.Exe });
			}
		}
	}

	private void RefreshNamedProfiles()
	{
		IReadOnlyList<ProfileChoice> choices = [new("Default", null), .. settings.Current.EffectiveProfiles.Where(p => p.IsNamed).Select(p => new ProfileChoice(p.Name, p.Exe))];
		for (int i = 0; i < choices.Count; i++)
		{
			if (i == NamedProfiles.Count)
			{
				NamedProfiles.Add(choices[i]);
			}
			else if (NamedProfiles[i] != choices[i])
			{
				NamedProfiles[i] = choices[i];
			}
		}
		while (NamedProfiles.Count > choices.Count)
		{
			NamedProfiles.RemoveAt(NamedProfiles.Count - 1);
		}
		OnPropertyChanged(nameof(ShowProfilePicker));
		OnPropertyChanged(nameof(ChosenProfile));
	}

	public IReadOnlyList<TriggerPreset> TriggerPresets { get; } = [TriggerPreset.Off, TriggerPreset.Feedback, TriggerPreset.Weapon, TriggerPreset.Vibration];

	[ObservableProperty]
	public partial bool IsConnected { get; private set; }

	// Connected by USB: the controller picture shows the cable.
	[ObservableProperty]
	public partial bool IsCabled { get; private set; }

	[ObservableProperty]
	public partial string ConnectionText { get; private set; } = "Not connected";

	[ObservableProperty]
	public partial int BatteryPercent { get; private set; }

	[ObservableProperty]
	public partial bool IsCharging { get; private set; }

	[ObservableProperty]
	public partial string FirmwareText { get; private set; } = "";

	[ObservableProperty]
	public partial string EmulationText { get; private set; } = "";

	[ObservableProperty]
	public partial string TrayToolTip { get; private set; } = "DualPulse - not connected";

	// The tray menu's status line: connection and battery.
	[ObservableProperty]
	public partial string TrayStatus { get; private set; } = "Not connected";

	public bool EmulationOn => settings.Current.Emulation != null;

	// PC control: the controller drives the mouse and keyboard (also switched with PS+Options).
	public bool PcControlOn => pcControl?.IsActive == true;

	[ObservableProperty]
	public partial InputSnapshot? Snapshot { get; private set; }

	// Null when the triggers were customised on the Triggers page (the box then shows "Custom").
	public TriggerPreset? SelectedTriggerPreset
	{
		get => settings.Current.TriggerPreset == TriggerPreset.Custom ? null : settings.Current.TriggerPreset;
		set
		{
			if (value is { } preset && preset != settings.Current.TriggerPreset)
			{
				settings.Update(s => s with { TriggerPreset = preset, Triggers = TriggerSettings.FromPreset(preset) });
			}
		}
	}

	public void Refresh()
	{
		ControllerSession? session = controller.Session;
		IsConnected = session != null;
		IsCabled = session?.Transport == Transport.Usb;
		EmulationText = emulation.Status == EmulationStatus.DriverMissing ? "Driver missing" : settings.Current.Emulation switch
		{
			PadKind.Xbox360 => "Xbox 360",
			PadKind.DualShock4 => "DualShock 4",
			PadKind.DualSense => "DualSense",
			_ => "Native"
		};
		if (session == null)
		{
			ConnectionText = controller.OpenError != null ? "Controller in use by another app" : "Not connected";
			BatteryPercent = 0;
			IsCharging = false;
			FirmwareText = "";
			TrayToolTip = "DualPulse - not connected";
			TrayStatus = ConnectionText;
			Snapshot = null;
		}
		else
		{
			string transport = session.Transport == Transport.Bluetooth ? "Bluetooth" : "USB";
			ConnectionText = session.LinkQuality is { } link ? $"{transport} · {link.Summary}" : transport;
			DualSenseBattery battery = session.Battery;
			BatteryPercent = battery.Level;
			IsCharging = battery.IsCharging;
			FirmwareText = session.Firmware is { } firmware ? "Firmware " + firmware.UpdateVersionText : "";
			TrayToolTip = BatteryNotifier.TooltipText(battery.Level, battery.State);
			TrayStatus = $"{transport} · {battery.Level}%{(battery.IsCharging ? " (charging)" : "")}";
		}
		PowerOffCommand.NotifyCanExecuteChanged();
	}

	// Pushes the newest input report into Snapshot only when something the drawing shows changed visibly; motion
	// sensors, the timestamp and stick noise change in every report and would otherwise redraw at the display rate.
	public void SampleInput()
	{
		// The app only calls this on the UI thread; the lock keeps callers without a UI context (tests) consistent.
		lock (sampleGate)
		{
			InputSnapshot? latest = controller.Session?.Latest;
			if (VisiblyDifferent(latest, Snapshot))
			{
				Snapshot = latest;
			}
		}
	}

	// Emulation off means native mode; on returns to the controller type used before.
	[RelayCommand]
	private void ToggleEmulation()
	{
		if (settings.Current.Emulation is { } kind)
		{
			lastEmulation = kind;
			settings.Update(s => s with { Emulation = null });
		}
		else
		{
			settings.Update(s => s with { Emulation = lastEmulation });
		}
		OnPropertyChanged(nameof(EmulationOn));
	}

	[RelayCommand]
	private void TogglePcControl()
	{
		if (pcControl != null)
		{
			pcControl.IsActive = !pcControl.IsActive;
		}
		OnPropertyChanged(nameof(PcControlOn));
	}

	[RelayCommand]
	private void SetLightbar(Rgb colour)
	{
		settings.Update(s => s with { Lightbar = colour });
	}

	private bool CanPowerOff()
	{
		return controller.Session?.Transport == Transport.Bluetooth;
	}

	[RelayCommand(CanExecute = nameof(CanPowerOff))]
	private Task PowerOff()
	{
		return Task.Run(() => power.PowerOffNow("power off button"));
	}

	// Read thread.
	private void OnInput(InputSnapshot snapshot)
	{
		if (!VisiblyDifferent(snapshot, lastPosted) || Interlocked.Exchange(ref samplePending, 1) == 1)
		{
			return;
		}
		lastPosted = snapshot;
		ui.Run(() =>
		{
			Volatile.Write(ref samplePending, 0);
			SampleInput();
		});
	}

	private void OnBackgroundChange()
	{
		ui.Run(Refresh);
	}

	// Compared with the snapshot last drawn, so a value resting near a threshold can't flicker: analog axes must move
	// by AnalogStep and touch points by TouchStep before the controller is redrawn.
	private static bool VisiblyDifferent(InputSnapshot? next, InputSnapshot? drawn)
	{
		const int AnalogStep = 3;
		if (next is not { } a || drawn is not { } b)
		{
			return next.HasValue != drawn.HasValue;
		}
		return a.Buttons != b.Buttons
			|| Math.Abs(a.LeftX - b.LeftX) >= AnalogStep || Math.Abs(a.LeftY - b.LeftY) >= AnalogStep
			|| Math.Abs(a.RightX - b.RightX) >= AnalogStep || Math.Abs(a.RightY - b.RightY) >= AnalogStep
			|| Math.Abs(a.L2 - b.L2) >= AnalogStep || Math.Abs(a.R2 - b.R2) >= AnalogStep
			|| TouchMoved(a.Touch0, b.Touch0) || TouchMoved(a.Touch1, b.Touch1);
	}

	private static bool TouchMoved(TouchPoint next, TouchPoint drawn)
	{
		const int TouchStep = 16;
		return next.Active != drawn.Active
			|| (next.Active && (Math.Abs(next.X - drawn.X) >= TouchStep || Math.Abs(next.Y - drawn.Y) >= TouchStep));
	}
}
