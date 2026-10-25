using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

namespace DualPulse.App.Services;

// Programs the OS starts at sign-in, by entry name. On Windows the current user's Run key; platforms without an
// implementation keep no entries.
public interface IRunRegistry
{
	string? Get(string name);

	void Set(string name, string value);

	void Delete(string name);
}

// HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
[SupportedOSPlatform("windows")]
public sealed class RunRegistry : IRunRegistry
{
	private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

	public string? Get(string name)
	{
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
		return key?.GetValue(name) as string;
	}

	public void Set(string name, string value)
	{
		using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey);
		key.SetValue(name, value, RegistryValueKind.String);
	}

	public void Delete(string name)
	{
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
		key?.DeleteValue(name, throwOnMissingValue: false);
	}
}

// "Start with Windows": keeps DualPulse's own Run entry in line with the setting. Started that way, DualPulse opens
// minimized to the tray. Each start re-checks the entry, so moving the exe or a leftover entry is corrected.
public sealed class StartupService : IHostedService, IDisposable
{
	public const string MinimizedArgument = "--minimized";

	private const string EntryName = "DualPulse";

	// The entry from before the rename; replaced by this one.
	private const string PreviousEntryName = "Resonance";

	private readonly IRunRegistry registry;

	private readonly ISettingsService settings;

	private readonly string command;

	public StartupService(IRunRegistry registry, ISettingsService settings, string exePath)
	{
		this.registry = registry;
		this.settings = settings;
		command = $"\"{exePath}\" {MinimizedArgument}";
		settings.Changed += OnSettingsChanged;
	}

	public static bool StartedMinimized(IEnumerable<string> args)
	{
		return args.Contains(MinimizedArgument, StringComparer.OrdinalIgnoreCase);
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		Apply();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		settings.Changed -= OnSettingsChanged;
	}

	public void Apply()
	{
		try
		{
			if (registry.Get(PreviousEntryName) != null)
			{
				registry.Delete(PreviousEntryName);
			}
			if (settings.Current.StartWithWindows)
			{
				if (registry.Get(EntryName) != command)
				{
					registry.Set(EntryName, command);
				}
			}
			else if (registry.Get(EntryName) != null)
			{
				registry.Delete(EntryName);
			}
		}
		catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
		{
			// Leaves the setting as it is; it is retried on the next start.
		}
	}

	private void OnSettingsChanged(AppSettings current)
	{
		Apply();
	}
}
