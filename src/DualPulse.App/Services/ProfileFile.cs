using System.Text.Json;

namespace DualPulse.App.Services;

// Picks a file to save to or open (Avalonia's dialogs in the app); null when cancelled.
public interface IFileDialogs
{
	Task<string?> PickSaveAsync(string suggestedName);

	Task<string?> PickOpenAsync();
}

// A profile in a file of its own ("Racing.dualpulse.json"), to share or keep: a small versioned JSON wrapper around
// the profile, written like settings.json.
public static class ProfileFile
{
	public const string Extension = ".dualpulse.json";

	private const string Format = "dualpulse-profile";

	public static string FileName(GameProfile profile)
	{
		string name = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
		return (name.Length == 0 ? "Profile" : name) + Extension;
	}

	public static string Write(GameProfile profile) =>
		JsonSerializer.Serialize(new ProfileFileData(Format, 1, profile), SettingsJsonContext.Default.ProfileFileData);

	// The profile to add, or null when the text isn't a profile file. One that is already there (same game or name)
	// comes in as a named copy, so nothing is overwritten.
	public static GameProfile? Read(string json, IReadOnlyList<GameProfile> existing)
	{
		ProfileFileData? data;
		try
		{
			data = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.ProfileFileData);
		}
		catch (JsonException)
		{
			return null;
		}
		if (data?.Format != Format || data.Profile is not { } profile || string.IsNullOrWhiteSpace(profile.Name) || string.IsNullOrWhiteSpace(profile.Exe))
		{
			return null;
		}
		bool clash = existing.Any(p => string.Equals(p.Exe, profile.Exe, StringComparison.OrdinalIgnoreCase)
			|| string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
		return clash ? GameProfile.Named(profile.Name, existing) with { Triggers = profile.Triggers, Gyro = profile.Gyro, Sticks = profile.Sticks } : profile;
	}
}

public sealed record ProfileFileData(string Format, int Version, GameProfile? Profile);
