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
	private readonly ISettingsService settings;

	private readonly string logsFolder;

	private readonly Action<string> open;

	public SettingsViewModel(ISettingsService settings, string logsFolder, Action<string>? open = null)
	{
		this.settings = settings;
		this.logsFolder = logsFolder;
		this.open = open ?? OpenWithShell;
	}

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

	private static void OpenWithShell(string path)
	{
		Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
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
