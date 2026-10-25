using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Core.Mapping;

namespace DualPulse.App.ViewModels;

// A labelled dropdown entry; the base type lets item templates bind Label without generics.
public abstract record LabelledChoice(string Label);

public sealed record GyroChoice<T>(string Label, T Value) : LabelledChoice(Label);

// The Gyro page: motion aiming on/off, where it goes (right stick or mouse), when it is active, sensitivity,
// inversion, axis and calibration. With a game profile selected, edits go to that profile (once it has its own gyro);
// calibration is always the controller's.
public sealed partial class GyroViewModel : ObservableObject
{
	private readonly ISettingsService settings;

	private readonly IControllerService controller;

	private readonly IGyroService gyro;

	private readonly ProfileSelection? selection;

	private readonly UiContext ui = new();

	public GyroViewModel(ISettingsService settings, IControllerService controller, IGyroService gyro, ProfileSelection? selection = null)
	{
		this.settings = settings;
		this.controller = controller;
		this.gyro = gyro;
		this.selection = selection;
		Strip = new ProfileStripViewModel(selection, ProfilePart.Gyro, "gyro");
		settings.Changed += _ => ui.Run(RefreshSettings);
		if (selection != null)
		{
			selection.Changed += () => ui.Run(RefreshSettings);
		}
		gyro.CalibrationChanged += () => ui.Run(RefreshCalibration);
		controller.SessionChanged += () => ui.Run(RefreshCalibration);
		RefreshCalibration();
	}

	public ProfileStripViewModel Strip { get; }

	public IReadOnlyList<GyroChoice<GyroOutput>> OutputChoices { get; } =
	[
		new("Right stick", GyroOutput.Stick),
		new("Mouse", GyroOutput.Mouse)
	];

	public IReadOnlyList<GyroChoice<GyroActivation>> ActivationChoices { get; } =
	[
		new("While L2 is held", GyroActivation.WhileL2),
		new("While L1 is held", GyroActivation.WhileL1),
		new("While R1 is held", GyroActivation.WhileR1),
		new("Always", GyroActivation.Always)
	];

	public IReadOnlyList<GyroChoice<GyroAxis>> AxisChoices { get; } =
	[
		new("Turn (yaw)", GyroAxis.Yaw),
		new("Tilt sideways (roll)", GyroAxis.Roll)
	];

	[ObservableProperty]
	public partial string CalibrationText { get; private set; } = "";

	[ObservableProperty]
	public partial bool CanCalibrate { get; private set; }

	private GyroSettings Gyro => selection?.Selected?.Gyro ?? settings.Current.EffectiveGyro;

	public bool Enabled
	{
		get => Gyro.Enabled;
		set => Update(Gyro with { Enabled = value });
	}

	public GyroChoice<GyroOutput> SelectedOutput
	{
		get => OutputChoices.First(c => c.Value == Gyro.Output);
		set => Update(Gyro with { Output = value.Value });
	}

	public GyroChoice<GyroActivation> SelectedActivation
	{
		get => ActivationChoices.First(c => c.Value == Gyro.Activation);
		set => Update(Gyro with { Activation = value.Value });
	}

	public double Sensitivity
	{
		get => Gyro.Sensitivity;
		set => Update(Gyro with { Sensitivity = Math.Round(Math.Clamp(value, 0.1, 5), 1) });
	}

	public bool InvertX
	{
		get => Gyro.InvertX;
		set => Update(Gyro with { InvertX = value });
	}

	public bool InvertY
	{
		get => Gyro.InvertY;
		set => Update(Gyro with { InvertY = value });
	}

	public GyroChoice<GyroAxis> SelectedAxis
	{
		get => AxisChoices.First(c => c.Value == Gyro.Axis);
		set => Update(Gyro with { Axis = value.Value });
	}

	[RelayCommand]
	private void Calibrate() => gyro.StartCalibration();

	private void RefreshCalibration()
	{
		CalibrationText = gyro.CalibrationState switch
		{
			GyroCalibrationState.Calibrating => "Hold still...",
			GyroCalibrationState.Done => "Calibrated.",
			_ => "Lay the controller flat and still, then calibrate if the aim drifts."
		};
		CanCalibrate = gyro.CalibrationState != GyroCalibrationState.Calibrating && controller.Session != null;
	}

	// Right stick output goes through the virtual controller, so in Native mode it does nothing; Mouse works anyway.
	public bool ShowStickNote => Gyro.Output == GyroOutput.Stick && settings.Current.Emulation == null;

	private void RefreshSettings()
	{
		Strip.Refresh();
		OnPropertyChanged(nameof(ShowStickNote));
		OnPropertyChanged(nameof(Enabled));
		OnPropertyChanged(nameof(SelectedOutput));
		OnPropertyChanged(nameof(SelectedActivation));
		OnPropertyChanged(nameof(Sensitivity));
		OnPropertyChanged(nameof(InvertX));
		OnPropertyChanged(nameof(InvertY));
		OnPropertyChanged(nameof(SelectedAxis));
	}

	private void Update(GyroSettings updated)
	{
		if (selection?.Selected != null)
		{
			if (Strip.CanEdit && updated != Gyro)
			{
				selection.Update(p => p with { Gyro = updated });
			}
			return;
		}
		if (updated != Gyro)
		{
			settings.Update(s => s with { Gyro = updated });
		}
	}
}
