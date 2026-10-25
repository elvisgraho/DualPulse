using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Core.Mapping;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class GyroViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakeControllerService controller = new();

	private readonly FakeGyroService gyro = new();

	private GyroViewModel CreateViewModel() => new(settings, controller, gyro);

	[Fact]
	public void Gyro_Choices()
	{
		GyroViewModel viewModel = CreateViewModel();

		Assert.Equal(["Right stick", "Mouse"], viewModel.OutputChoices.Select(c => c.Label));
		Assert.Equal(["While L2 is held", "While L1 is held", "While R1 is held", "Always"], viewModel.ActivationChoices.Select(c => c.Label));
		Assert.Equal(["Turn (yaw)", "Tilt sideways (roll)"], viewModel.AxisChoices.Select(c => c.Label));
	}

	[Fact]
	public void Gyro_Changes_Persist()
	{
		GyroViewModel viewModel = CreateViewModel();

		viewModel.Enabled = true;
		viewModel.SelectedOutput = viewModel.OutputChoices[1];
		viewModel.SelectedActivation = viewModel.ActivationChoices[3];
		viewModel.Sensitivity = 2.5;
		viewModel.InvertX = true;
		viewModel.InvertY = true;
		viewModel.SelectedAxis = viewModel.AxisChoices[1];

		Assert.Equal(new GyroSettings(true, GyroOutput.Mouse, GyroActivation.Always, 2.5, true, true, GyroAxis.Roll), settings.Current.EffectiveGyro);
	}

	[Fact]
	public void Gyro_DefaultSensitivity_Is2()
	{
		// 1x felt sluggish on small movements in hands-on testing.
		Assert.Equal(2, CreateViewModel().Sensitivity);
	}

	[Fact]
	public void Gyro_SensitivityClamped()
	{
		GyroViewModel viewModel = CreateViewModel();

		viewModel.Sensitivity = 50;

		Assert.Equal(5, settings.Current.EffectiveGyro.Sensitivity);
	}

	[Fact]
	public void Gyro_Calibrate_StartsCalibration_AndShowsProgress()
	{
		GyroViewModel viewModel = CreateViewModel();

		viewModel.CalibrateCommand.Execute(null);

		Assert.Equal(1, gyro.CalibrationsStarted);
		Assert.Equal("Hold still...", viewModel.CalibrationText);
		Assert.False(viewModel.CanCalibrate);

		gyro.Finish();

		Assert.Equal("Calibrated.", viewModel.CalibrationText);
	}

	[Theory]
	[InlineData(GyroOutput.Stick, null, true)]
	[InlineData(GyroOutput.Stick, DualPulse.Emulation.PadKind.Xbox360, false)]
	[InlineData(GyroOutput.Mouse, null, false)]
	public void RightStick_InNativeMode_SaysItNeedsAVirtualController(GyroOutput output, DualPulse.Emulation.PadKind? kind, bool note)
	{
		settings.Update(s => s with { Emulation = kind, Gyro = new GyroSettings(Output: output) });

		Assert.Equal(note, CreateViewModel().ShowStickNote);
	}
}
