using DualPulse.App.Services;
using DualPulse.Core.Protocol;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class SettingsServiceTests : IDisposable
{
	private readonly string folder = Path.Combine(Path.GetTempPath(), "dualpulse-tests-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(folder))
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	private string SettingsPath => Path.Combine(folder, "settings.json");

	[Fact]
	public void Settings_MissingFile_UsesDefaults()
	{
		SettingsService service = new(folder);

		Assert.Equal(1, service.Current.Version);
		Assert.Equal(PadKind.Xbox360, service.Current.Emulation);
		Assert.Null(service.Current.Lightbar);
		Assert.Equal(TriggerPreset.Off, service.Current.TriggerPreset);
		Assert.True(service.Current.EffectivePower.ShortcutEnabled);
		Assert.Equal(15, service.Current.EffectivePower.IdleTimeoutMinutes);
	}

	[Fact]
	public void Settings_FirstStartAfterTheRename_TakesTheOldSettings()
	{
		// The app was called Resonance and kept its settings in %LOCALAPPDATA%\Resonance.
		string old = folder + "-old";
		Directory.CreateDirectory(old);
		File.WriteAllText(Path.Combine(old, "settings.json"), """{ "Version": 1, "Emulation": "DualShock4" }""");
		try
		{
			SettingsService service = new(folder, old);

			Assert.Equal(PadKind.DualShock4, service.Current.Emulation);
			Assert.True(File.Exists(SettingsPath));
		}
		finally
		{
			Directory.Delete(old, recursive: true);
		}
	}

	[Fact]
	public void Settings_OldSettings_NeverReplaceCurrentOnes()
	{
		string old = folder + "-old";
		Directory.CreateDirectory(old);
		File.WriteAllText(Path.Combine(old, "settings.json"), """{ "Version": 1, "Emulation": "DualShock4" }""");
		Directory.CreateDirectory(folder);
		File.WriteAllText(SettingsPath, """{ "Version": 1, "Emulation": null }""");
		try
		{
			SettingsService service = new(folder, old);

			Assert.Null(service.Current.Emulation);
		}
		finally
		{
			Directory.Delete(old, recursive: true);
		}
	}

	[Theory]
	[InlineData("""{ "Version": 1 }""", true)]
	[InlineData("""{ "Version": 1, "SetupDone": false }""", false)]
	public void Settings_FromBeforeTheFirstStartGuide_CountAsSetUp(string json, bool done)
	{
		Directory.CreateDirectory(folder);
		File.WriteAllText(SettingsPath, json);

		Assert.Equal(done, new SettingsService(folder).Current.SetupDone);
	}

	[Fact]
	public void Settings_NewInstall_ShowsTheFirstStartGuide()
	{
		Assert.False(new SettingsService(folder).Current.SetupDone);
	}

	[Fact]
	public void Settings_DrawnTriggerZones_RoundTrip()
	{
		TriggerEffectSettings zones = new(TriggerKind.Zones, Zones: [8, 6, 4, 2, 0, 0, 2, 4, 6, 8]);
		new SettingsService(folder).Update(s => s with { Triggers = new TriggerSettings(Left: zones) });

		TriggerEffectSettings loaded = new SettingsService(folder).Current.EffectiveTriggers.Left;

		Assert.Equal(TriggerKind.Zones, loaded.Kind);
		Assert.Equal([8, 6, 4, 2, 0, 0, 2, 4, 6, 8], loaded.EffectiveZones);
	}

	[Fact]
	public void Settings_TriggersWithoutRange_LoadWithNoRange()
	{
		Directory.CreateDirectory(folder);
		File.WriteAllText(SettingsPath, """{ "Triggers": { "Linked": true, "Left": { "Kind": "Resistance" } } }""");

		TriggerEffectSettings left = new SettingsService(folder).Current.EffectiveTriggers.Left;

		Assert.Equal(TriggerKind.Resistance, left.Kind);
		Assert.Equal(0, left.Deadzone);
		Assert.Equal(0, left.StartAt);
	}

	[Fact]
	public void Settings_Profiles_RoundTrip()
	{
		GameProfile profile = new("Game", "game.exe", Gyro: new GyroSettings(Enabled: true, Sensitivity: 3));
		new SettingsService(folder).Update(s => s with { Profiles = [profile] });

		GameProfile loaded = Assert.Single(new SettingsService(folder).Current.EffectiveProfiles);

		Assert.Equal(profile, loaded);
	}

	[Fact]
	public void Settings_Update_PersistsAndRaisesChanged()
	{
		SettingsService service = new(folder);
		AppSettings? raised = null;
		service.Changed += s => raised = s;

		service.Update(s => s with { Lightbar = new Rgb(1, 2, 3), Emulation = null });

		Assert.Equal(new Rgb(1, 2, 3), raised!.Lightbar);
		SettingsService reloaded = new(folder);
		Assert.Equal(new Rgb(1, 2, 3), reloaded.Current.Lightbar);
		Assert.Null(reloaded.Current.Emulation);
	}

	[Fact]
	public void Settings_CorruptFile_FallsBackAndKeepsBackup()
	{
		Directory.CreateDirectory(folder);
		File.WriteAllText(SettingsPath, "{not json");

		SettingsService service = new(folder);

		Assert.Equal(PadKind.Xbox360, service.Current.Emulation);
		Assert.Equal("{not json", File.ReadAllText(SettingsPath + ".bak"));
	}

	[Fact]
	public void Settings_NewerUnknownFields_Ignored()
	{
		Directory.CreateDirectory(folder);
		File.WriteAllText(SettingsPath, """{ "Version": 1, "TriggerPreset": "Weapon", "SomethingFromTheFuture": 42 }""");

		SettingsService service = new(folder);

		Assert.Equal(TriggerPreset.Weapon, service.Current.TriggerPreset);
	}

	[Fact]
	public void Settings_FileLockedByAnotherProcess_UsesDefaults()
	{
		Directory.CreateDirectory(folder);
		File.WriteAllText(SettingsPath, "{}");
		using FileStream locked = new(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

		SettingsService service = new(folder);

		Assert.Equal(PadKind.Xbox360, service.Current.Emulation);
	}

	[Fact]
	public void Settings_WriteFails_KeepsChangeInMemoryWithoutThrowing()
	{
		SettingsService service = new(folder);
		service.Update(s => s with { StartMinimized = false });
		using FileStream locked = new(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

		service.Update(s => s with { StartMinimized = true });

		Assert.True(service.Current.StartMinimized);
	}
}
