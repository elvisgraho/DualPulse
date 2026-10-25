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

public sealed record SetupChoice(string Label, string Detail, PadKind? Kind);

// The first-start window: how games should see the controller. Picking a mode whose driver is there finishes it; with
// the driver missing it stays open with a link to it, and closing the window finishes it (SetupDone).
public sealed partial class FirstStartViewModel : ObservableObject
{
	private readonly ISettingsService settings;

	private readonly IVirtualPadBackend backend;

	private readonly Action<string> open;

	public FirstStartViewModel(ISettingsService settings, IVirtualPadBackend backend, Action<string>? open = null)
	{
		this.settings = settings;
		this.backend = backend;
		this.open = open ?? (target => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose());
	}

	public IReadOnlyList<SetupChoice> Choices { get; } =
	[
		new("Xbox controller", "Works in every game.", PadKind.Xbox360),
		new("DualSense", "Adaptive triggers and haptics in games that support them, also over Bluetooth.", PadKind.DualSense),
		new("No virtual controller", "Games use your DualSense directly.", null)
	];

	public bool IsVisible => !settings.Current.SetupDone;

	// A ready mode was picked: the window closes.
	public event Action? Finished;

	public SetupChoice? SelectedChoice
	{
		get;
		set
		{
			if (value != null && SetProperty(ref field, value))
			{
				Choose(value);
			}
		}
	}

	[ObservableProperty]
	public partial bool HasChosen { get; private set; }

	[ObservableProperty]
	public partial string DriverText { get; private set; } = "";

	[ObservableProperty]
	public partial bool DriverMissing { get; private set; }

	private string? driverPage;

	private void Choose(SetupChoice choice)
	{
		settings.Update(s => s with { Emulation = choice.Kind });
		(string? name, string? page) = choice.Kind switch
		{
			PadKind.Xbox360 or PadKind.DualShock4 when !backend.IsAvailable => ("ViGEmBus", DriverPages.ViGEmBus),
			PadKind.DualSense when !backend.IsAvailableFor(PadKind.DualSense) => ("usbip-win2", DriverPages.UsbIpWin2),
			_ => ((string?)null, (string?)null)
		};
		driverPage = page;
		DriverMissing = page != null;
		DriverText = name == null ? "" : $"Needs the free {name} driver.";
		HasChosen = true;
		if (page == null)
		{
			Dismiss();
			Finished?.Invoke();
		}
	}

	[RelayCommand]
	private void GetDriver()
	{
		if (driverPage == null)
		{
			return;
		}
		try
		{
			open(driverPage);
		}
		catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
		{
		}
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
}
