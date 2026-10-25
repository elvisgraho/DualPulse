using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class AudioViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakeAudioSourceFactory sources = new();

	private AudioViewModel CreateViewModel() => new(settings, sources);

	[Fact]
	public void Audio_Sources_DefaultOutputFirst_ThenDevices()
	{
		AudioViewModel viewModel = CreateViewModel();

		Assert.Equal(["Default output", "Speakers", "Playback 7/8"], viewModel.Sources.Select(s => s.Label));
		Assert.Null(viewModel.SelectedSource.DeviceId);
	}

	[Fact]
	public void Audio_Changes_Persist_AndVolumeClamps()
	{
		AudioViewModel viewModel = CreateViewModel();

		viewModel.Enabled = true;
		viewModel.SelectedSource = viewModel.Sources[2];
		viewModel.SelectedOutput = viewModel.OutputChoices[2];
		viewModel.Volume = 150;

		Assert.Equal(new ControllerAudioSettings(true, "dev-b", ControllerAudioOutput.Earbuds, 100), settings.Current.EffectiveAudio);
		Assert.Equal(["Auto", "Speaker", "Earbuds"], viewModel.OutputChoices.Select(c => c.Label));
	}

	[Fact]
	public void Audio_SavedDeviceGone_ShowsItAsMissing_AndRefreshFindsNewOnes()
	{
		settings.Update(s => s with { Audio = new ControllerAudioSettings(DeviceId: "gone") });
		AudioViewModel viewModel = CreateViewModel();
		Assert.Equal("Unavailable device", viewModel.SelectedSource.Label);

		sources.Devices = [.. sources.Devices, new("dev-c", "Headphones")];
		viewModel.RefreshSourcesCommand.Execute(null);

		Assert.Contains(viewModel.Sources, s => s.Label == "Headphones");
	}

	[Fact]
	public void Audio_FeelSound_PersistsAndClampsIntensity()
	{
		AudioViewModel viewModel = CreateViewModel();
		Assert.False(viewModel.AudioHaptics);
		Assert.Equal(100, viewModel.AudioHapticsIntensity);

		viewModel.AudioHaptics = true;
		viewModel.AudioHapticsIntensity = 150;
		Assert.True(settings.Current.EffectiveRumble.AudioHaptics);
		Assert.Equal(1.5, settings.Current.EffectiveRumble.AudioHapticsIntensity, 3);

		viewModel.AudioHapticsIntensity = 500;
		Assert.Equal(2.0, settings.Current.EffectiveRumble.AudioHapticsIntensity, 3);
	}

	[Fact]
	public void Audio_FeelStyle_DefaultsToPunchy_AndPersists()
	{
		AudioViewModel viewModel = CreateViewModel();
		Assert.Equal(["Punchy (music)", "Natural"], viewModel.AudioStyleChoices.Select(c => c.Label));
		Assert.Equal(DualPulse.Core.Protocol.HapticsAudioStyle.Punchy, viewModel.SelectedAudioStyle.Style);

		viewModel.SelectedAudioStyle = viewModel.AudioStyleChoices[1];

		Assert.Equal(DualPulse.Core.Protocol.HapticsAudioStyle.Natural, settings.Current.EffectiveRumble.AudioHapticsStyle);
	}
}
