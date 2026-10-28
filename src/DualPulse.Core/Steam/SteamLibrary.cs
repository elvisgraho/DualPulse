using System.Text.RegularExpressions;

namespace DualPulse.Core.Steam;

// An installed Steam game: its app id, name and folder under steamapps\common.
public sealed record SteamApp(int AppId, string Name, string InstallDir);

// Reads Steam's own library files (steamapps\libraryfolders.vdf, steamapps\appmanifest_*.acf), which are Valve's
// key-value text: "key" "value" pairs and nested { } blocks, with backslashes escaped.
public static partial class SteamLibrary
{
	// Every library folder Steam knows (each holds a steamapps folder).
	public static IReadOnlyList<string> LibraryPaths(string libraryFolders) =>
		[.. Pairs(libraryFolders).Where(p => p.Key.Equals("path", StringComparison.OrdinalIgnoreCase)).Select(p => p.Value)];

	public static SteamApp? ParseManifest(string manifest)
	{
		Dictionary<string, string> values = [];
		foreach ((string key, string value) in Pairs(manifest))
		{
			values.TryAdd(key.ToLowerInvariant(), value);
		}
		return values.TryGetValue("appid", out string? id) && int.TryParse(id, out int appId)
			&& values.TryGetValue("name", out string? name) && values.TryGetValue("installdir", out string? folder)
			? new SteamApp(appId, name, folder)
			: null;
	}

	// The game whose folder (library\steamapps\common\installdir) holds the exe; null for anything else.
	public static SteamApp? GameFor(string exePath, IEnumerable<(string Library, SteamApp App)> installed)
	{
		string full = Path.GetFullPath(exePath);
		foreach ((string library, SteamApp app) in installed)
		{
			string folder = Path.GetFullPath(Path.Combine(library, "steamapps", "common", app.InstallDir)) + Path.DirectorySeparatorChar;
			if (full.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
			{
				return app;
			}
		}
		return null;
	}

	private static IEnumerable<(string Key, string Value)> Pairs(string text) =>
		Pair().Matches(text).Select(m => (Unescape(m.Groups[1].Value), Unescape(m.Groups[2].Value)));

	private static string Unescape(string value) => value.Replace(@"\\", @"\").Replace("\\\"", "\"");

	// "key" then "value" on one line; quotes inside are escaped.
	[GeneratedRegex(@"""((?:[^""\\]|\\.)*)""[ \t]+""((?:[^""\\]|\\.)*)""")]
	private static partial Regex Pair();
}
