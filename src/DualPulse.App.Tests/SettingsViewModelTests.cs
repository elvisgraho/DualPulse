using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class SettingsViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly List<string> opened = [];

	private SettingsViewModel CreateViewModel() => new(settings, @"C:\data\logs", opened.Add);

	[Fact]
	public void Settings_PowerOptions_Persist()
	{
		SettingsViewModel viewModel = CreateViewModel();

		viewModel.ShortcutEnabled = false;
		viewModel.PowerOffOnExit = true;
		viewModel.SelectedIdleTimeout = viewModel.IdleTimeoutChoices.Single(c => c.Minutes == 30);
		viewModel.StartMinimized = true;

		Assert.False(settings.Current.EffectivePower.ShortcutEnabled);
		Assert.True(settings.Current.EffectivePower.PowerOffOnExit);
		Assert.Equal(30, settings.Current.EffectivePower.IdleTimeoutMinutes);
		Assert.Equal(30, viewModel.IdleTimeoutMinutes);
		Assert.True(settings.Current.StartMinimized);
	}

	[Fact]
	public void Settings_IdleChoices_ExactValues()
	{
		SettingsViewModel viewModel = CreateViewModel();

		Assert.Equal([0, 5, 10, 15, 30, 60], viewModel.IdleTimeoutChoices.Select(c => c.Minutes));
		Assert.Equal("Never", viewModel.IdleTimeoutChoices[0].Label);
		Assert.Equal(15, viewModel.SelectedIdleTimeout.Minutes);
	}

	[Fact]
	public void Settings_AboutSection_VersionAndLinks()
	{
		SettingsViewModel viewModel = CreateViewModel();

		Assert.Equal("0.1.0", viewModel.Version);
		viewModel.OpenLogsFolderCommand.Execute(null);
		viewModel.OpenThirdPartyNoticesCommand.Execute(null);

		Assert.Equal(@"C:\data\logs", opened[0]);
		Assert.EndsWith("THIRD-PARTY-NOTICES.md", opened[1]);
	}

	[Fact]
	public void Settings_UpdateCheck_IsOptIn_AndANewVersionLinksToItsPage()
	{
		FakeUpdateService updates = new();
		SettingsViewModel viewModel = new(settings, @"C:\data\logs", opened.Add, updates);
		Assert.False(viewModel.CheckForUpdates);
		Assert.False(viewModel.UpdateAvailable);

		viewModel.CheckForUpdates = true;
		updates.Announce(new ReleaseInfo(new Version(0, 2, 0), "https://github.com/elvisgraho/DualPulse/releases/tag/v0.2.0"));
		viewModel.DownloadUpdateCommand.Execute(null);

		Assert.True(settings.Current.CheckForUpdates);
		Assert.True(viewModel.UpdateAvailable);
		Assert.Equal("Version 0.2.0 is out", viewModel.UpdateText);
		Assert.Equal("https://github.com/elvisgraho/DualPulse/releases/tag/v0.2.0", opened[^1]);
	}

	[Fact]
	public async Task Settings_CopyDiagnostics_PutsTheReportOnTheClipboard()
	{
		List<string> copied = [];
		SettingsViewModel viewModel = new(settings, @"C:\data\logs", opened.Add, diagnostics: () => "report", copy: text =>
		{
			copied.Add(text);
			return Task.CompletedTask;
		});

		await viewModel.CopyDiagnosticsCommand.ExecuteAsync(null);

		Assert.Equal(["report"], copied);
		Assert.Equal("Copied. Paste it into your bug report.", viewModel.DiagnosticsStatus);
	}

	[Fact]
	public void Settings_OpenFails_DoesNotThrow()
	{
		SettingsViewModel viewModel = new(settings, @"C:\data\logs", _ => throw new System.ComponentModel.Win32Exception("No application is associated"));

		viewModel.OpenThirdPartyNoticesCommand.Execute(null);
		viewModel.OpenLogsFolderCommand.Execute(null);
	}

	[Fact]
	public void Settings_StartWithWindows_Persists()
	{
		SettingsViewModel viewModel = CreateViewModel();

		viewModel.StartWithWindows = true;

		Assert.True(settings.Current.StartWithWindows);
		Assert.True(viewModel.StartWithWindows);
	}
}

internal sealed class FakeUpdateService : IUpdateService
{
	public ReleaseInfo? Available { get; private set; }

	public event Action? AvailableChanged;

	public void Announce(ReleaseInfo release)
	{
		Available = release;
		AvailableChanged?.Invoke();
	}
}
