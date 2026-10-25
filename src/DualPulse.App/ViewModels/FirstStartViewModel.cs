using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Emulation;

namespace DualPulse.App.ViewModels;

// The official release pages of the drivers DualPulse can use; nothing is downloaded or installed by DualPulse.
public static class DriverPages
{
	public const string ViGEmBus = "https://github.com/nefarius/ViGEmBus/releases";

	public const string HidHide = "https://github.com/nefarius/HidHide/releases";

	public const string UsbIpWin2 = "https://github.com/vadimgrn/usbip-win2/releases";
}

// One answer to "how should games see your controller?", with whether its driver is installed.
public sealed partial class SetupChoice(string label, string detail, PadKind? kind, string? driver, string? driverPage) : ObservableObject
{
	public string Label { get; } = label;

	public string Detail { get; } = detail;

	public PadKind? Kind { get; } = kind;

	public string? DriverPage { get; } = driverPage;

	[ObservableProperty]
	public partial bool Ready { get; private set; } = true;

	[ObservableProperty]
	public partial string Status { get; private set; } = "Ready";

	internal void Update(bool installed)
	{
		Ready = driver == null || installed;
		Status = Ready ? "Ready" : $"Needs the free {driver} driver";
	}
}

// The first-start window: how games should see the controller, with what each choice needs installed, and HidHide for
// hiding the real controller. Picking a ready choice finishes it; a choice that needs a driver waits until it is
// installed (Recheck, every few seconds). Closing the window also finishes it (SetupDone).
public sealed partial class FirstStartViewModel : ObservableObject
{
	private readonly ISettingsService settings;

	private readonly IVirtualPadBackend backend;

	private readonly IHidHideService hidHide;

	private readonly Action<string> open;

	public FirstStartViewModel(ISettingsService settings, IVirtualPadBackend backend, IHidHideService hidHide, Action<string>? open = null)
	{
		this.settings = settings;
		this.backend = backend;
		this.hidHide = hidHide;
		this.open = open ?? (target => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose());
		Choices =
		[
			new("Xbox controller", "Works in every game.", PadKind.Xbox360, "ViGEmBus", DriverPages.ViGEmBus),
			new("DualSense", "Adaptive triggers and haptics in games that support them, also over Bluetooth.", PadKind.DualSense,
				"usbip-win2", DriverPages.UsbIpWin2),
			new("No virtual controller", "Games use your DualSense directly.", null, null, null)
		];
		UpdateDrivers();
	}

	public IReadOnlyList<SetupChoice> Choices { get; }

	public bool IsVisible => !settings.Current.SetupDone;

	// A ready choice was picked: the window closes.
	public event Action? Finished;

	public SetupChoice? SelectedChoice
	{
		get;
		set
		{
			if (value != null && SetProperty(ref field, value))
			{
				settings.Update(s => s with { Emulation = value.Kind });
				FinishIfReady();
			}
		}
	}

	[ObservableProperty]
	public partial bool HidHideReady { get; private set; }

	[ObservableProperty]
	public partial string HidHideStatus { get; private set; } = "";

	// Called every few seconds while the window is open: notices drivers installed meanwhile.
	public void Recheck()
	{
		UpdateDrivers();
		FinishIfReady();
	}

	// Closed either way: it doesn't open again.
	public void Dismiss()
	{
		if (!settings.Current.SetupDone)
		{
			settings.Update(s => s with { SetupDone = true });
			OnPropertyChanged(nameof(IsVisible));
		}
	}

	[RelayCommand]
	private void GetDriver(SetupChoice choice)
	{
		if (choice.DriverPage is { } page)
		{
			Open(page);
		}
	}

	[RelayCommand]
	private void GetHidHide() => Open(DriverPages.HidHide);

	private void UpdateDrivers()
	{
		foreach (SetupChoice choice in Choices)
		{
			choice.Update(choice.Kind is { } kind && backend.IsAvailableFor(kind));
		}
		HidHideReady = hidHide.Status is not (Services.HidHideStatus.NotInstalled or Services.HidHideStatus.NotAttached);
		HidHideStatus = HidHideReady ? "HidHide is installed." : "Needs the free HidHide driver.";
	}

	private void FinishIfReady()
	{
		if (SelectedChoice is { Ready: true } && !settings.Current.SetupDone)
		{
			Dismiss();
			Finished?.Invoke();
		}
	}

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
