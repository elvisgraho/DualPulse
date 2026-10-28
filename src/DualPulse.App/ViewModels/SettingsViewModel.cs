using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Core.Power;

namespace DualPulse.App.ViewModels;

public sealed record IdleTimeoutChoice(string Label, int Minutes);

// The Settings page. Every change is written straight to the settings; services react to ISettingsService.Changed.
public sealed partial class SettingsViewModel : ObservableObject
{
	private const string BugReportForm = "https://github.com/elvisgraho/DualPulse/issues/new?template=bug_report.yml";

	private readonly ISettingsService settings;

	private readonly string logsFolder;

	private readonly Action<string> open;

	private readonly IUpdateService? updates;

	private readonly Func<string>? diagnostics;

	private readonly Func<string, Task>? copy;

	private readonly UiContext ui = new();

	public SettingsViewModel(ISettingsService settings, string logsFolder, Action<string>? open = null, IUpdateService? updates = null,
		Func<string>? diagnostics = null, Func<string, Task>? copy = null)
	{
		this.diagnostics = diagnostics;
		this.copy = copy;
		this.settings = settings;
		this.logsFolder = logsFolder;
		this.open = open ?? Platform.Shell.Open;
		this.updates = updates;
		if (updates != null)
		{
			updates.AvailableChanged += () => ui.Run(() =>
			{
				OnPropertyChanged(nameof(UpdateAvailable));
				OnPropertyChanged(nameof(UpdateText));
			});
		}
	}

	public bool CheckForUpdates
	{
		get => settings.Current.CheckForUpdates;
		set
		{
			if (value != settings.Current.CheckForUpdates)
			{
				settings.Update(s => s with { CheckForUpdates = value });
				OnPropertyChanged();
			}
		}
	}

	// Hold PS and press the D-pad to switch profile or trigger preset (ShortcutService).
	public bool ControllerShortcuts
	{
		get => settings.Current.ControllerShortcuts;
		set
		{
			if (value != settings.Current.ControllerShortcuts)
			{
				settings.Update(s => s with { ControllerShortcuts = value });
				OnPropertyChanged();
			}
		}
	}

	// Shown after Copy diagnostics.
	[ObservableProperty]
	public partial string DiagnosticsStatus { get; private set; } = "";

	public bool UpdateAvailable => updates?.Available != null;

	public string UpdateText => updates?.Available is { } release ? $"Version {release.Version} is out" : "";

	public IReadOnlyList<IdleTimeoutChoice> IdleTimeoutChoices { get; } =
	[
		new("Never", 0),
		new("5 minutes", 5),
		new("10 minutes", 10),
		new("15 minutes", 15),
		new("30 minutes", 30),
		new("60 minutes", 60)
	];

	public string Version { get; } = (typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
		.Split('+')[0];

	public bool ShortcutEnabled
	{
		get => settings.Current.EffectivePower.ShortcutEnabled;
		set => UpdatePower(p => p with { ShortcutEnabled = value });
	}

	public bool PowerOffOnExit
	{
		get => settings.Current.EffectivePower.PowerOffOnExit;
		set => UpdatePower(p => p with { PowerOffOnExit = value });
	}

	public int IdleTimeoutMinutes => settings.Current.EffectivePower.IdleTimeoutMinutes;

	public IdleTimeoutChoice SelectedIdleTimeout
	{
		get => IdleTimeoutChoices.FirstOrDefault(c => c.Minutes == IdleTimeoutMinutes) ?? IdleTimeoutChoices[0];
		set
		{
			UpdatePower(p => p with { IdleTimeoutMinutes = value.Minutes });
			OnPropertyChanged(nameof(IdleTimeoutMinutes));
		}
	}

	public bool StartMinimized
	{
		get => settings.Current.StartMinimized;
		set
		{
			if (value != settings.Current.StartMinimized)
			{
				settings.Update(s => s with { StartMinimized = value });
				OnPropertyChanged();
			}
		}
	}

	// Starts minimized to the tray when Windows starts (StartupService keeps the Run entry).
	public bool StartWithWindows
	{
		get => settings.Current.StartWithWindows;
		set
		{
			if (value != settings.Current.StartWithWindows)
			{
				settings.Update(s => s with { StartWithWindows = value });
				OnPropertyChanged();
			}
		}
	}

	[RelayCommand]
	private void OpenLogsFolder()
	{
		// FileLoggerProvider creates the folder at startup.
		Open(logsFolder);
	}

	[RelayCommand]
	private async Task CopyDiagnostics()
	{
		if (await TryCopyDiagnostics())
		{
			DiagnosticsStatus = "Copied. Paste it into your bug report.";
		}
	}

	// Copies the diagnostics, then opens the bug report form on GitHub.
	[RelayCommand]
	private async Task ReportProblem()
	{
		bool copied = await TryCopyDiagnostics();
		Open(BugReportForm);
		if (copied)
		{
			DiagnosticsStatus = "Diagnostics copied. Paste them into the form.";
		}
	}

	private async Task<bool> TryCopyDiagnostics()
	{
		if (diagnostics == null || copy == null)
		{
			return false;
		}
		try
		{
			// Reading the log takes a moment, so the report is built off the UI thread.
			await copy(await Task.Run(diagnostics));
			return true;
		}
		catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.COMException)
		{
			DiagnosticsStatus = "Couldn't copy. Try again.";
			return false;
		}
	}

	[RelayCommand]
	private void DownloadUpdate()
	{
		if (updates?.Available is { } release)
		{
			Open(release.Url);
		}
	}

	[RelayCommand]
	private void OpenThirdPartyNotices()
	{
		Open(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"));
	}

	// Never throws: a missing file association (e.g. nothing opens .md) must not take the app down.
	private void Open(string path)
	{
		try
		{
			open(path);
		}
		catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
		{
		}
	}


	private void UpdatePower(Func<PowerSettings, PowerSettings> change, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
	{
		PowerSettings current = settings.Current.EffectivePower;
		PowerSettings updated = change(current);
		if (updated != current)
		{
			settings.Update(s => s with { Power = updated });
			OnPropertyChanged(property);
		}
	}
}
