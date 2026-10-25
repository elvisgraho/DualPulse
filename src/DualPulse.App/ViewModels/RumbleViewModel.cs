using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;

namespace DualPulse.App.ViewModels;

public sealed record RumbleModeChoice(string Label, RumbleMode Mode);

// The Rumble page: game rumble strength and mute, rumble mode, motor tests and live motor meters.
public sealed partial class RumbleViewModel : ObservableObject
{
	private readonly ISettingsService settings;

	private readonly IControllerService controller;

	private readonly IRumbleService rumble;

	private readonly UiContext ui = new();

	public RumbleViewModel(ISettingsService settings, IControllerService controller, IRumbleService rumble)
	{
		this.settings = settings;
		this.controller = controller;
		this.rumble = rumble;
		settings.Changed += _ => ui.Run(() =>
		{
			OnPropertyChanged(nameof(Strength));
			OnPropertyChanged(nameof(Muted));
			OnPropertyChanged(nameof(SelectedMode));
			OnPropertyChanged(nameof(BluetoothHaptics));
		});
	}

	public IReadOnlyList<RumbleModeChoice> ModeChoices { get; } =
	[
		new("Automatic", RumbleMode.Automatic),
		new("Classic", RumbleMode.Classic),
		new("Improved", RumbleMode.Improved)
	];

	// How hard each motor runs right now, 0-1.
	[ObservableProperty]
	public partial double LeftLevel { get; private set; }

	[ObservableProperty]
	public partial double RightLevel { get; private set; }

	[ObservableProperty]
	public partial bool CanTest { get; private set; }

	// What Automatic means for the connected controller.
	[ObservableProperty]
	public partial string ModeHint { get; private set; } = "";

	private RumbleSettings Rumble => settings.Current.EffectiveRumble;

	public int Strength
	{
		get => Rumble.Strength;
		set => Update(Rumble with { Strength = Math.Clamp(value, 0, 200) });
	}

	public bool Muted
	{
		get => Rumble.Muted;
		set => Update(Rumble with { Muted = value });
	}

	public bool BluetoothHaptics
	{
		get => Rumble.BluetoothHaptics;
		set => Update(Rumble with { BluetoothHaptics = value });
	}

	public RumbleModeChoice SelectedMode
	{
		get => ModeChoices.First(c => c.Mode == Rumble.Mode);
		set => Update(Rumble with { Mode = value.Mode });
	}

	public void RefreshMeters()
	{
		Core.Devices.ControllerSession? session = controller.Session;
		// What is playing (motors, or haptics over Bluetooth), not the raw motor bytes, which stay 0 in haptics mode.
		(byte playingLeft, byte playingRight) = session == null ? ((byte)0, (byte)0) : rumble.Levels;
		double left = playingLeft / 255.0;
		double right = playingRight / 255.0;
		if (left != LeftLevel)
		{
			LeftLevel = left;
		}
		if (right != RightLevel)
		{
			RightLevel = right;
		}
		CanTest = session != null;
		ModeHint = session?.Firmware is { } firmware
			? (firmware.SupportsImprovedRumble ? "Your controller supports improved rumble, so Automatic uses it." : "Your controller's firmware is older, so Automatic uses classic rumble.")
			: "";
	}

	[RelayCommand]
	private void TestLeft() => rumble.TestMotors(left: true, right: false);

	[RelayCommand]
	private void TestRight() => rumble.TestMotors(left: false, right: true);

	[RelayCommand]
	private void TestBoth() => rumble.TestMotors(left: true, right: true);

	private void Update(RumbleSettings updated)
	{
		if (updated != Rumble)
		{
			settings.Update(s => s with { Rumble = updated });
		}
	}
}
