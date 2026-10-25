using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;

namespace DualPulse.App.ViewModels;

public enum ProfilePart
{
	Triggers,
	Gyro,
	Sticks
}

// Which profile the Controller page is editing (null = Default). It jumps to a game's profile when that game becomes
// active, so alt-tabbing to DualPulse mid-game lands on the right profile. Changed can be raised off the UI thread.
public sealed class ProfileSelection
{
	private readonly ISettingsService settings;

	private readonly IProfileService? profiles;

	private readonly object gate = new();

	private string? selectedExe;

	private string? lastActiveExe;

	public ProfileSelection(ISettingsService settings, IProfileService? profiles = null)
	{
		this.settings = settings;
		this.profiles = profiles;
		settings.Changed += _ => Changed?.Invoke();
		if (profiles != null)
		{
			profiles.Changed += OnProfilesChanged;
			OnProfilesChanged();
		}
	}

	public event Action? Changed;

	public GameProfile? Selected
	{
		get
		{
			string? exe;
			lock (gate)
			{
				exe = selectedExe;
			}
			return exe == null ? null : settings.Current.EffectiveProfiles.FirstOrDefault(p => Same(p.Exe, exe));
		}
	}

	public bool IsActive(GameProfile? profile) => profile != null && profiles?.Active is { } active && Same(active.Exe, profile.Exe);

	public void Select(GameProfile? profile)
	{
		lock (gate)
		{
			selectedExe = profile?.Exe;
		}
		Changed?.Invoke();
	}

	public bool UsesOwn(ProfilePart part) => Selected is { } profile && part switch
	{
		ProfilePart.Triggers => profile.Triggers != null,
		ProfilePart.Gyro => profile.Gyro != null,
		_ => profile.Sticks != null
	};

	// Starts the selected profile's part from the current default.
	public void Customize(ProfilePart part)
	{
		AppSettings saved = settings.Current;
		Update(p => part switch
		{
			ProfilePart.Triggers => p with { Triggers = saved.EffectiveTriggers },
			ProfilePart.Gyro => p with { Gyro = saved.EffectiveGyro },
			_ => p with { Sticks = saved.EffectiveSticks }
		});
	}

	public void UseDefault(ProfilePart part)
	{
		Update(p => part switch
		{
			ProfilePart.Triggers => p with { Triggers = null },
			ProfilePart.Gyro => p with { Gyro = null },
			_ => p with { Sticks = null }
		});
	}

	// Replaces the selected profile in the saved list; does nothing on Default.
	public void Update(Func<GameProfile, GameProfile> change)
	{
		if (Selected is not { } selected)
		{
			return;
		}
		settings.Update(s => s with { Profiles = [.. s.EffectiveProfiles.Select(p => Same(p.Exe, selected.Exe) ? change(p) : p)] });
	}

	private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	private void OnProfilesChanged()
	{
		string? active = profiles!.Active?.Exe;
		bool follow;
		lock (gate)
		{
			follow = active != null && (lastActiveExe == null || !Same(active, lastActiveExe));
			lastActiveExe = active;
			if (follow)
			{
				selectedExe = active;
			}
		}
		Changed?.Invoke();
	}
}

// The strip above Triggers or Gyro while a game profile is selected: whether the profile has its own settings for
// this part, with the button that switches. Controls below are only editable on Default or with the profile's own.
public sealed partial class ProfileStripViewModel(ProfileSelection? selection, ProfilePart part, string noun) : ObservableObject
{
	public bool IsVisible => selection?.Selected != null;

	public bool IsCustom => selection?.UsesOwn(part) == true;

	public bool CanEdit => !IsVisible || IsCustom;

	public string Text => selection?.Selected is { } profile
		? IsCustom ? $"{profile.Name} uses its own {noun}." : $"Follows your default {noun}."
		: "";

	public void Refresh() => OnPropertyChanged(string.Empty);

	[RelayCommand]
	private void Customize() => selection?.Customize(part);

	[RelayCommand]
	private void UseDefault() => selection?.UseDefault(part);
}
