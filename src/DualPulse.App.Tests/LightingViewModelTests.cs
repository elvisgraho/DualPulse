using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Core.Features;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class LightingViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakeControllerService controller = new();

	private LightingViewModel CreateViewModel() => new(settings, controller);

	[Fact]
	public void Lighting_Choices_ExactValues()
	{
		LightingViewModel viewModel = CreateViewModel();

		Assert.Equal(["Static", "Breathing", "Rainbow", "Battery"], viewModel.ModeChoices.Select(c => c.Label));
		Assert.Equal(["Off", "Player 1", "Player 2", "Player 3", "Player 4", "All", "Battery level"], viewModel.PlayerLedChoices.Select(c => c.Label));
		Assert.Equal(["High", "Medium", "Low"], viewModel.PlayerLedBrightnessChoices.Select(c => c.Label));
		Assert.Equal(["Off", "On", "Pulse"], viewModel.MicLedChoices.Select(c => c.Label));
		Assert.Equal(6, viewModel.Swatches.Count);
	}

	[Fact]
	public void Lighting_Defaults_ReflectSettings()
	{
		LightingViewModel viewModel = CreateViewModel();

		Assert.Equal(LightbarMode.Static, viewModel.SelectedMode.Mode);
		Assert.Equal(100, viewModel.Brightness);
		Assert.Equal("#C9A86A", viewModel.HexColour);
		Assert.Equal(PlayerLedPattern.Player1, viewModel.SelectedPlayerLeds.Pattern);
	}

	[Fact]
	public void Lighting_HueSaturation_SetsCustomColour()
	{
		LightingViewModel viewModel = CreateViewModel();

		viewModel.Saturation = 100;
		viewModel.Hue = 120;

		Assert.Equal(new Rgb(0, 255, 0), settings.Current.Lightbar);
		Assert.Equal("#00FF00", viewModel.HexColour);
	}

	[Fact]
	public void Lighting_Hex_AcceptsValidAndFlagsInvalid()
	{
		LightingViewModel viewModel = CreateViewModel();

		viewModel.HexColour = "#3D7BFF";
		Assert.Equal(new Rgb(0x3D, 0x7B, 0xFF), settings.Current.Lightbar);
		Assert.False(viewModel.HexInvalid);

		viewModel.HexColour = "3dff00";
		Assert.Equal(new Rgb(0x3D, 0xFF, 0x00), settings.Current.Lightbar);

		viewModel.HexColour = "zzz";
		Assert.True(viewModel.HexInvalid);
		Assert.Equal(new Rgb(0x3D, 0xFF, 0x00), settings.Current.Lightbar);
	}

	[Fact]
	public void Lighting_Swatch_SetsColourAndSyncsHex()
	{
		LightingViewModel viewModel = CreateViewModel();

		viewModel.SetColourCommand.Execute(new Rgb(0xFF, 0x3B, 0x3B));

		Assert.Equal(new Rgb(0xFF, 0x3B, 0x3B), settings.Current.Lightbar);
		Assert.Equal("#FF3B3B", viewModel.HexColour);
	}

	[Fact]
	public void Lighting_Changes_Persist()
	{
		LightingViewModel viewModel = CreateViewModel();

		viewModel.SelectedMode = viewModel.ModeChoices[1];
		viewModel.Brightness = 40;
		viewModel.SelectedPlayerLeds = viewModel.PlayerLedChoices[5];
		viewModel.SelectedPlayerLedBrightness = viewModel.PlayerLedBrightnessChoices[2];
		viewModel.SelectedMicLed = viewModel.MicLedChoices[2];

		LightingSettings lighting = settings.Current.EffectiveLighting;
		Assert.Equal(LightbarMode.Breathing, lighting.Mode);
		Assert.Equal(40, lighting.Brightness);
		Assert.Equal(PlayerLedPattern.All, lighting.PlayerLeds);
		Assert.Equal(LedBrightness.Low, lighting.PlayerLedBrightness);
		Assert.Equal(MicLed.Pulse, lighting.MicLed);
	}

	[Fact]
	public void Lighting_ExternalColourChange_UpdatesPage()
	{
		LightingViewModel viewModel = CreateViewModel();

		settings.Update(s => s with { Lightbar = new Rgb(0xFF, 0xFF, 0xFF) });

		Assert.Equal("#FFFFFF", viewModel.HexColour);
	}

	[Fact]
	public void Lighting_Preview_FollowsControllerOrSettings()
	{
		LightingViewModel viewModel = CreateViewModel();

		viewModel.RefreshPreview();

		Assert.Equal(AppSettings.AccentLightbar, viewModel.Preview);
	}

	[Fact]
	public void Lighting_BatteryPattern_IsAChoice()
	{
		LightingViewModel viewModel = CreateViewModel();

		viewModel.SelectedPlayerLeds = viewModel.PlayerLedChoices.Single(c => c.Label == "Battery level");

		Assert.Equal(PlayerLedPattern.Battery, settings.Current.EffectiveLighting.PlayerLeds);
	}

	[Fact]
	public void Lighting_FlashAndMicSwitches_Persist()
	{
		LightingViewModel viewModel = CreateViewModel();
		Assert.True(viewModel.LowBatteryFlash);
		Assert.True(viewModel.MuteButtonMutesMic);

		viewModel.LowBatteryFlash = false;
		viewModel.MuteButtonMutesMic = false;

		Assert.False(settings.Current.EffectiveLighting.LowBatteryFlash);
		Assert.False(settings.Current.EffectiveLighting.MuteButtonMutesMic);
	}

	[Fact]
	public void Lighting_MicHint_SaysWhetherThereIsAMicrophone()
	{
		FakeMicrophone mic = new();
		using MicMuteService micMute = new(controller, settings, mic);
		LightingViewModel viewModel = new(settings, controller, micMute);
		Assert.Equal("Shows whether your microphone is muted.", viewModel.MicHint);

		mic.SetFromWindows(null);

		Assert.Equal("No microphone found.", viewModel.MicHint);
	}
}
