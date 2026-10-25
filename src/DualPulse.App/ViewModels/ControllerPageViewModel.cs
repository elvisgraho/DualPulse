using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Emulation;

namespace DualPulse.App.ViewModels;

public enum ControllerSection
{
	Lighting,
	Triggers,
	Sticks,
	Rumble,
	Gyro,
	Audio
}

// One selector entry; Exe is null for Default. Only name and exe, so editing a profile doesn't replace its entry.
public sealed record ProfileChoice(string Label, string? Exe);

// The Controller page: Lighting, Triggers, Sticks, Rumble, Gyro and Audio, chosen with chips at the top. They only change what the
// controller itself does (lights, trigger feel, motors, motion aiming), so they share one rail entry. The profile
// selector picks what Triggers, Sticks and Gyro edit: Default, or a game's profile (added by picking a running program).
public sealed partial class ControllerPageViewModel : ObservableObject
{
	private readonly ProfileSelection? selection;

	private readonly ISettingsService? settings;

	private readonly IRunningWindows? windows;

	private readonly UiContext ui = new();

	public ControllerPageViewModel(LightingViewModel? lighting = null, TriggersViewModel? triggers = null, RumbleViewModel? rumble = null,
		GyroViewModel? gyro = null, ProfileSelection? selection = null, ISettingsService? settings = null, IRunningWindows? windows = null,
		AudioViewModel? audio = null, SticksViewModel? sticks = null)
	{
		Sticks = sticks;
		Audio = audio;
		Lighting = lighting;
		Triggers = triggers;
		Rumble = rumble;
		Gyro = gyro;
		this.selection = selection;
		this.settings = settings;
		this.windows = windows;
		if (selection != null)
		{
			selection.Changed += () => ui.Run(RefreshProfiles);
		}
		RefreshProfiles();
	}

	public LightingViewModel? Lighting { get; }

	public TriggersViewModel? Triggers { get; }

	public RumbleViewModel? Rumble { get; }

	public SticksViewModel? Sticks { get; }

	public GyroViewModel? Gyro { get; }

	public AudioViewModel? Audio { get; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsLighting), nameof(IsTriggers), nameof(IsSticks), nameof(IsRumble), nameof(IsGyro), nameof(IsAudio), nameof(ShowSameForAllGames))]
	public partial ControllerSection CurrentSection { get; set; } = ControllerSection.Lighting;

	public bool IsLighting => CurrentSection == ControllerSection.Lighting;

	public bool IsTriggers => CurrentSection == ControllerSection.Triggers;

	public bool IsSticks => CurrentSection == ControllerSection.Sticks;

	public bool IsRumble => CurrentSection == ControllerSection.Rumble;

	public bool IsGyro => CurrentSection == ControllerSection.Gyro;

	public bool IsAudio => CurrentSection == ControllerSection.Audio;

	public bool HasProfiles => selection != null;

	// Updated in place: replacing the list makes the selector re-select its old item afterwards, undoing a selection.
	public ObservableCollection<ProfileChoice> Profiles { get; } = [];

	[ObservableProperty]
	public partial IReadOnlyList<RunningWindow> RunningGames { get; private set; } = [];

	[ObservableProperty]
	public partial bool IsAddingGame { get; private set; }

	public ProfileChoice? SelectedProfile
	{
		get => Profiles.FirstOrDefault(c => c.Exe == selection?.Selected?.Exe);
		set
		{
			// The list control clears its selection while the list is replaced; that isn't the user choosing.
			if (value != null && selection != null && value.Exe != selection.Selected?.Exe)
			{
				selection.Select(settings?.Current.EffectiveProfiles.FirstOrDefault(p => p.Exe == value.Exe));
			}
		}
	}

	public bool IsActiveNow => selection?.IsActive(selection.Selected) == true;

	// Lighting and Rumble aren't per game; they say so while a game profile is selected.
	public bool ShowSameForAllGames => selection?.Selected != null && (IsLighting || IsRumble || IsAudio);

	public bool CanRemove => selection?.Selected != null;

	[RelayCommand]
	private void Select(ControllerSection section)
	{
		CurrentSection = section;
	}

	[RelayCommand]
	private void StartAddGame()
	{
		RunningGames = windows?.List(Environment.ProcessId) ?? [];
		IsAddingGame = true;
	}

	[RelayCommand]
	private void CancelAddGame()
	{
		IsAddingGame = false;
	}

	[RelayCommand]
	private void PickGame(RunningWindow window)
	{
		IsAddingGame = false;
		if (settings == null || selection == null)
		{
			return;
		}
		GameProfile? existing = settings.Current.EffectiveProfiles.FirstOrDefault(p => string.Equals(p.Exe, window.Exe, StringComparison.OrdinalIgnoreCase));
		if (existing == null)
		{
			existing = new GameProfile(window.Title, window.Exe);
			settings.Update(s => s with { Profiles = [.. s.EffectiveProfiles, existing] });
		}
		selection.Select(existing);
	}

	[RelayCommand]
	private void RemoveProfile()
	{
		if (settings == null || selection?.Selected is not { } selected)
		{
			return;
		}
		selection.Select(null);
		settings.Update(s => s with { Profiles = [.. s.EffectiveProfiles.Where(p => !string.Equals(p.Exe, selected.Exe, StringComparison.OrdinalIgnoreCase))] });
	}

	private void RefreshProfiles()
	{
		IReadOnlyList<GameProfile> games = settings?.Current.EffectiveProfiles ?? [];
		IReadOnlyList<ProfileChoice> choices = [new("Default", null), .. games.Select(p => new ProfileChoice(p.Name, p.Exe))];
		for (int i = 0; i < choices.Count; i++)
		{
			if (i == Profiles.Count)
			{
				Profiles.Add(choices[i]);
			}
			else if (Profiles[i] != choices[i])
			{
				Profiles[i] = choices[i];
			}
		}
		while (Profiles.Count > choices.Count)
		{
			Profiles.RemoveAt(Profiles.Count - 1);
		}
		OnPropertyChanged(nameof(SelectedProfile));
		OnPropertyChanged(nameof(IsActiveNow));
		OnPropertyChanged(nameof(ShowSameForAllGames));
		OnPropertyChanged(nameof(CanRemove));
	}
}
