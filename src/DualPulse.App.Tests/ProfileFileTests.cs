using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ProfileFileTests : IDisposable
{
	private static readonly GameProfile Forza = new("Forza Horizon 5", "ForzaHorizon5.exe", Triggers: TriggerSettings.FromPreset(TriggerPreset.Weapon),
		Gyro: new GyroSettings(Sensitivity: 3));

	private readonly string folder = Path.Combine(Path.GetTempPath(), "dualpulse-profile-tests-" + Guid.NewGuid().ToString("N"));

	private readonly FakeSettingsService settings = new(new AppSettings(Profiles: [Forza]));

	private readonly FakeForegroundWatcher watcher = new();

	private readonly FakeFileDialogs dialogs = new();

	private readonly ProfileService profiles;

	private readonly ProfileSelection selection;

	private readonly ControllerPageViewModel page;

	public ProfileFileTests()
	{
		Directory.CreateDirectory(folder);
		profiles = new ProfileService(settings, watcher, "DualPulse.exe");
		selection = new ProfileSelection(settings, profiles);
		page = new ControllerPageViewModel(selection: selection, settings: settings, files: dialogs);
	}

	public void Dispose()
	{
		profiles.Dispose();
		Directory.Delete(folder, recursive: true);
	}

	private string FilePath(string name) => Path.Combine(folder, name);

	[Fact]
	public async Task Export_SavesTheSelectedProfile_UnderItsName()
	{
		selection.Select(Forza);
		dialogs.SavePath = FilePath("out.dualpulse.json");

		await page.ExportProfileCommand.ExecuteAsync(null);

		Assert.Equal("Forza Horizon 5.dualpulse.json", dialogs.SuggestedName);
		Assert.True(File.Exists(dialogs.SavePath));
		Assert.Equal("Saved.", page.ProfileFileStatus);
	}

	[Fact]
	public void Export_OnlyWithAProfileSelected()
	{
		Assert.False(page.ExportProfileCommand.CanExecute(null));

		selection.Select(Forza);

		Assert.True(page.ExportProfileCommand.CanExecute(null));
	}

	[Fact]
	public async Task Import_OnAnotherInstall_AddsItAndSelectsIt()
	{
		selection.Select(Forza);
		dialogs.SavePath = FilePath("forza.dualpulse.json");
		await page.ExportProfileCommand.ExecuteAsync(null);
		settings.Update(s => s with { Profiles = [] });

		dialogs.OpenPath = dialogs.SavePath;
		await page.ImportProfileCommand.ExecuteAsync(null);

		GameProfile imported = Assert.Single(settings.Current.EffectiveProfiles);
		Assert.Equal(Forza, imported);
		Assert.Equal(Forza.Exe, selection.Selected?.Exe);
	}

	[Fact]
	public async Task Import_OfOneYouHave_KeepsBoth()
	{
		selection.Select(Forza);
		dialogs.SavePath = FilePath("forza.dualpulse.json");
		await page.ExportProfileCommand.ExecuteAsync(null);

		dialogs.OpenPath = dialogs.SavePath;
		await page.ImportProfileCommand.ExecuteAsync(null);

		Assert.Equal(2, settings.Current.EffectiveProfiles.Count);
		GameProfile copy = settings.Current.EffectiveProfiles[1];
		Assert.Equal("Forza Horizon 5 2", copy.Name);
		Assert.True(copy.IsNamed);
		Assert.Equal(Forza.Triggers, copy.Triggers);
		Assert.Equal(Forza.Gyro, copy.Gyro);
		Assert.Equal(copy.Exe, selection.Selected?.Exe);
	}

	[Fact]
	public async Task Import_OfAnotherFile_SaysSo_AndAddsNothing()
	{
		File.WriteAllText(FilePath("notes.json"), "{ \"hello\": 1 }");
		dialogs.OpenPath = FilePath("notes.json");

		await page.ImportProfileCommand.ExecuteAsync(null);

		Assert.Single(settings.Current.EffectiveProfiles);
		Assert.Equal("That file isn't a DualPulse profile.", page.ProfileFileStatus);
	}

	[Fact]
	public async Task CancelledDialogs_DoNothing()
	{
		selection.Select(Forza);

		await page.ExportProfileCommand.ExecuteAsync(null);
		await page.ImportProfileCommand.ExecuteAsync(null);

		Assert.Single(settings.Current.EffectiveProfiles);
		Assert.Equal("", page.ProfileFileStatus);
	}

	private sealed class FakeFileDialogs : IFileDialogs
	{
		public string? SavePath { get; set; }

		public string? OpenPath { get; set; }

		public string? SuggestedName { get; private set; }

		public Task<string?> PickSaveAsync(string suggestedName)
		{
			SuggestedName = suggestedName;
			return Task.FromResult(SavePath);
		}

		public Task<string?> PickOpenAsync() => Task.FromResult(OpenPath);
	}
}
