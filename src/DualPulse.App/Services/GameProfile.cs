using System.Text.Json.Serialization;

namespace DualPulse.App.Services;

// Per-game settings, applied while the game's window has focus. Exe is the file name (e.g. "game.exe"), matched
// case-insensitively. A part left null follows the saved settings.
// A named profile ("Racing") has no game: its Exe is NamedPrefix + name, which no program can be called (':' isn't
// allowed in file names), and it applies when picked by hand (AppSettings.ChosenProfile).
public sealed record GameProfile(string Name, string Exe, TriggerSettings? Triggers = null, GyroSettings? Gyro = null, StickSettings? Sticks = null)
{
	public const string NamedPrefix = "profile:";

	[JsonIgnore]
	public bool IsNamed => Exe.StartsWith(NamedPrefix, StringComparison.Ordinal);

	// A new named profile; a name already taken gets a number ("Racing 2").
	public static GameProfile Named(string name, IReadOnlyList<GameProfile> existing)
	{
		string unique = name.Trim();
		for (int number = 2; existing.Any(p => string.Equals(p.Name, unique, StringComparison.OrdinalIgnoreCase)); number++)
		{
			unique = $"{name.Trim()} {number}";
		}
		return new GameProfile(unique, NamedPrefix + unique);
	}
}
