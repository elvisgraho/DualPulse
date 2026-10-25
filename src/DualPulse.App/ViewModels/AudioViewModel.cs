using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Core.Audio;

namespace DualPulse.App.ViewModels;

public sealed record AudioSourceChoice(string Label, string? DeviceId) : LabelledChoice(Label);

public sealed record AudioOutputChoice(string Label, ControllerAudioOutput Output) : LabelledChoice(Label);

public sealed record AudioStyleChoice(string Label, DualPulse.Core.Protocol.HapticsAudioStyle Style);

// The Audio page: PC sound on the controller over Bluetooth, played on its speaker or felt as haptics. The source is a Windows output device whose sound is
// forwarded (Default output mirrors everything); a saved device that is not present shows as unavailable instead of
// silently switching.
public sealed partial class AudioViewModel : ObservableObject
{
	private readonly ISettingsService settings;

	private readonly ISystemAudioSourceFactory sources;

	private readonly UiContext ui = new();

	public AudioViewModel(ISettingsService settings, ISystemAudioSourceFactory sources)
	{
		this.settings = settings;
		this.sources = sources;
		settings.Changed += _ => ui.Run(Refresh);
		RefreshSources();
	}

	// Updated in place: replacing the list makes the selector re-select its old item afterwards.
	public ObservableCollection<AudioSourceChoice> Sources { get; } = [];

	public IReadOnlyList<AudioOutputChoice> OutputChoices { get; } =
	[
		new("Auto", ControllerAudioOutput.Auto),
		new("Speaker", ControllerAudioOutput.Speaker),
		new("Earbuds", ControllerAudioOutput.Earbuds)
	];

	private ControllerAudioSettings Audio => settings.Current.EffectiveAudio;

	private RumbleSettings Rumble => settings.Current.EffectiveRumble;

	public bool Enabled
	{
		get => Audio.Enabled;
		set => Update(Audio with { Enabled = value });
	}

	public AudioSourceChoice SelectedSource
	{
		get => Sources.FirstOrDefault(s => s.DeviceId == Audio.DeviceId) ?? Sources[0];
		set
		{
			// The list control clears its selection while items change; that isn't the user choosing.
			if (value != null)
			{
				Update(Audio with { DeviceId = value.DeviceId });
			}
		}
	}

	public AudioOutputChoice SelectedOutput
	{
		get => OutputChoices.First(c => c.Output == Audio.Output);
		set => Update(Audio with { Output = value.Output });
	}

	public int Volume
	{
		get => Audio.Volume;
		set => Update(Audio with { Volume = Math.Clamp(value, 0, 100) });
	}

	public IReadOnlyList<AudioStyleChoice> AudioStyleChoices { get; } =
	[
		new("Punchy (music)", DualPulse.Core.Protocol.HapticsAudioStyle.Punchy),
		new("Natural", DualPulse.Core.Protocol.HapticsAudioStyle.Natural)
	];

	public AudioStyleChoice SelectedAudioStyle
	{
		get => AudioStyleChoices.First(c => c.Style == Rumble.AudioHapticsStyle);
		set => UpdateRumble(Rumble with { AudioHapticsStyle = value.Style });
	}

	public bool AudioHaptics
	{
		get => Rumble.AudioHaptics;
		set => UpdateRumble(Rumble with { AudioHaptics = value });
	}

	// Percent, 0-200.
	public int AudioHapticsIntensity
	{
		get => (int)Math.Round(Rumble.AudioHapticsIntensity * 100);
		set => UpdateRumble(Rumble with { AudioHapticsIntensity = Math.Clamp(value, 0, 200) / 100.0 });
	}

	[RelayCommand]
	private void RefreshSources()
	{
		List<AudioSourceChoice> choices = [new("Default output", null), .. sources.OutputDevices().Select(d => new AudioSourceChoice(d.Name, d.Id))];
		if (Audio.DeviceId is { } saved && choices.All(c => c.DeviceId != saved))
		{
			choices.Add(new AudioSourceChoice("Unavailable device", saved));
		}
		for (int i = 0; i < choices.Count; i++)
		{
			if (i == Sources.Count)
			{
				Sources.Add(choices[i]);
			}
			else if (Sources[i] != choices[i])
			{
				Sources[i] = choices[i];
			}
		}
		while (Sources.Count > choices.Count)
		{
			Sources.RemoveAt(Sources.Count - 1);
		}
		OnPropertyChanged(nameof(SelectedSource));
	}

	private void Refresh()
	{
		OnPropertyChanged(nameof(Enabled));
		OnPropertyChanged(nameof(SelectedSource));
		OnPropertyChanged(nameof(SelectedOutput));
		OnPropertyChanged(nameof(Volume));
		OnPropertyChanged(nameof(AudioHaptics));
		OnPropertyChanged(nameof(AudioHapticsIntensity));
		OnPropertyChanged(nameof(SelectedAudioStyle));
	}

	private void Update(ControllerAudioSettings updated)
	{
		if (updated != Audio)
		{
			settings.Update(s => s with { Audio = updated });
		}
	}

	private void UpdateRumble(RumbleSettings updated)
	{
		if (updated != Rumble)
		{
			settings.Update(s => s with { Rumble = updated });
		}
	}
}
