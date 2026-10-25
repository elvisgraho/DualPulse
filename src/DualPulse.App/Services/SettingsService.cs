using System.Text.Json;

namespace DualPulse.App.Services;

public interface ISettingsService
{
	AppSettings Current { get; }

	void Update(Func<AppSettings, AppSettings> change);

	event Action<AppSettings>? Changed;
}

// Loads and saves settings.json. A file that can't be parsed is kept as settings.json.bak and defaults are used,
// so a corrupt file never stops the app or silently loses the user's copy. File access errors (another program
// holding the file, permissions) never throw: reads fall back to defaults, a failed write keeps the change in memory.
public sealed class SettingsService : ISettingsService
{
	private readonly object gate = new();

	private readonly string path;

	// previousFolder: where an older version kept its settings (before the rename to DualPulse); they are copied over
	// once, while this folder has none.
	public SettingsService(string folder, string? previousFolder = null)
	{
		path = Path.Combine(folder, "settings.json");
		if (previousFolder != null && !File.Exists(path) && File.Exists(Path.Combine(previousFolder, "settings.json")))
		{
			try
			{
				Directory.CreateDirectory(folder);
				File.Copy(Path.Combine(previousFolder, "settings.json"), path);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// Starts with defaults instead.
			}
		}
		Current = Load();
	}

	public AppSettings Current { get; private set; }

	public event Action<AppSettings>? Changed;

	public void Update(Func<AppSettings, AppSettings> change)
	{
		AppSettings updated;
		lock (gate)
		{
			updated = change(Current);
			Current = updated;
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				File.WriteAllText(path, JsonSerializer.Serialize(updated, SettingsJsonContext.Default.AppSettings));
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				// Kept in memory; the next successful update writes it.
			}
		}
		Changed?.Invoke(updated);
	}

	private AppSettings Load()
	{
		if (!File.Exists(path))
		{
			return new AppSettings();
		}
		string json;
		try
		{
			json = File.ReadAllText(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return new AppSettings();
		}
		try
		{
			AppSettings loaded = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
			// Settings from before the first-start guide existed: that user has set DualPulse up already.
			using JsonDocument document = JsonDocument.Parse(json);
			return document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty(nameof(AppSettings.SetupDone), out _)
				? loaded with { SetupDone = true }
				: loaded;
		}
		catch (Exception exception) when (exception is JsonException or NotSupportedException)
		{
			try
			{
				File.Copy(path, path + ".bak", overwrite: true);
			}
			catch (Exception copyException) when (copyException is IOException or UnauthorizedAccessException)
			{
				// Defaults still load; the unreadable file stays where it is.
			}
			return new AppSettings();
		}
	}
}
