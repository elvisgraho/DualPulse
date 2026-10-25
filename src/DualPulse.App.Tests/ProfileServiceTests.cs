using DualPulse.App.Services;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ProfileServiceTests : IDisposable
{
	private static readonly TriggerSettings Racing = new(Linked: true, Left: new TriggerEffectSettings(TriggerKind.Resistance, Deadzone: 0.1));

	private static readonly GameProfile Forza = new("Forza Horizon 5", "ForzaHorizon5.exe", Triggers: Racing);

	private readonly FakeSettingsService settings = new(new AppSettings(Profiles: [Forza]));

	private readonly FakeForegroundWatcher watcher = new();

	private readonly ProfileService profiles;

	public ProfileServiceTests()
	{
		profiles = new ProfileService(settings, watcher, "DualPulse.exe");
	}

	public void Dispose() => profiles.Dispose();

	[Fact]
	public void Merge_NullParts_FollowSaved()
	{
		AppSettings saved = new(Gyro: new GyroSettings(Enabled: true));

		AppSettings merged = ProfileService.Merge(saved, new GameProfile("Game", "game.exe"));

		Assert.Equal(saved.EffectiveTriggers, merged.EffectiveTriggers);
		Assert.Equal(saved.EffectiveGyro, merged.EffectiveGyro);
	}

	[Fact]
	public void Merge_SetParts_Win()
	{
		GyroSettings gyro = new(Enabled: true, Sensitivity: 3);

		AppSettings merged = ProfileService.Merge(new AppSettings(), new GameProfile("Game", "game.exe", Racing, gyro));

		Assert.Equal(Racing, merged.EffectiveTriggers);
		Assert.Equal(gyro, merged.EffectiveGyro);
	}

	[Fact]
	public void Merge_ProfileSticks_Win()
	{
		StickSettings sticks = new(Left: new StickRange(Deadzone: 0.2));

		AppSettings merged = ProfileService.Merge(new AppSettings(), new GameProfile("Game", "game.exe", Sticks: sticks));

		Assert.Equal(0.2, merged.EffectiveSticks.Left.Deadzone);
	}

	[Fact]
	public void Merge_KeepsSavedGyroOffsets()
	{
		AppSettings saved = new(Gyro: new GyroSettings(OffsetX: 5, OffsetY: -7, OffsetZ: 2));

		GyroSettings merged = ProfileService.Merge(saved, new GameProfile("Game", "game.exe", Gyro: new GyroSettings(Enabled: true, OffsetY: 99))).EffectiveGyro;

		Assert.True(merged.Enabled);
		Assert.Equal(((short)5, (short)-7, (short)2), (merged.OffsetX, merged.OffsetY, merged.OffsetZ));
	}

	[Fact]
	public void NamedProfile_HasNoGame_AndAUniqueKey()
	{
		GameProfile shooter = GameProfile.Named("Shooter", []);

		Assert.True(shooter.IsNamed);
		Assert.False(Forza.IsNamed);
		Assert.Equal("Shooter", shooter.Name);
		Assert.Equal("Shooter 2", GameProfile.Named("Shooter", [shooter]).Name);
	}

	[Fact]
	public void ChosenNamedProfile_AppliesWhileNoGameProfileIsInFront()
	{
		TriggerSettings heavy = new(Left: new TriggerEffectSettings(TriggerKind.Resistance, Strength: 8));
		GameProfile shooter = GameProfile.Named("Shooter", []) with { Triggers = heavy };
		settings.Update(s => s with { Profiles = [Forza, shooter], ChosenProfile = shooter.Exe });

		watcher.Focus("notepad.exe");
		Assert.Equal(shooter, profiles.Active);
		Assert.Equal(heavy, profiles.Effective.EffectiveTriggers);

		// A game with its own profile still wins while it's in front.
		watcher.Focus("ForzaHorizon5.exe");
		Assert.Equal(Forza, profiles.Active);
	}

	[Fact]
	public void ChosenProfile_ThatNoLongerExists_FallsBackToDefault()
	{
		settings.Update(s => s with { ChosenProfile = GameProfile.NamedPrefix + "Gone" });

		watcher.Focus("notepad.exe");

		Assert.Null(profiles.Active);
	}

	[Fact]
	public void ForegroundGame_ActivatesItsProfile()
	{
		watcher.Focus("forzahorizon5.EXE");

		Assert.Equal(Forza, profiles.Active);
		Assert.Equal(Racing, profiles.Effective.EffectiveTriggers);
	}

	[Fact]
	public void UnknownExe_ClearsTheProfile()
	{
		watcher.Focus("ForzaHorizon5.exe");
		watcher.Focus("explorer.exe");

		Assert.Null(profiles.Active);
		Assert.Equal(settings.Current.EffectiveTriggers, profiles.Effective.EffectiveTriggers);
	}

	[Fact]
	public void OwnWindow_KeepsTheProfile_UntilAnotherProgramHasFocus()
	{
		watcher.Focus("ForzaHorizon5.exe");
		watcher.Focus("dualpulse.exe");
		Assert.Equal(Forza, profiles.Active);

		watcher.Focus("explorer.exe");
		Assert.Null(profiles.Active);
	}

	[Fact]
	public void UnreadableExe_ClearsTheProfile()
	{
		watcher.Focus("ForzaHorizon5.exe");
		watcher.Focus(null);

		Assert.Null(profiles.Active);
	}

	[Fact]
	public void ActiveProfileEdited_EffectiveUpdatesAtOnce()
	{
		watcher.Focus("ForzaHorizon5.exe");
		GyroSettings gyro = new(Enabled: true);

		settings.Update(s => s with { Profiles = [Forza with { Gyro = gyro }] });

		Assert.Equal(gyro, profiles.Effective.EffectiveGyro);
	}

	[Fact]
	public void ActiveProfileRemoved_Clears()
	{
		watcher.Focus("ForzaHorizon5.exe");

		settings.Update(s => s with { Profiles = [] });

		Assert.Null(profiles.Active);
	}

	[Fact]
	public void Changed_RaisedOnSwitch_AndOnSettingsChange()
	{
		int raised = 0;
		profiles.Changed += () => raised++;

		watcher.Focus("ForzaHorizon5.exe");
		settings.Update(s => s with { StartMinimized = true });

		Assert.Equal(2, raised);
		Assert.True(profiles.Effective.StartMinimized);
	}

	[Fact]
	public void DuplicateExe_FirstWins()
	{
		settings.Update(s => s with { Profiles = [Forza, Forza with { Name = "Second" }] });

		watcher.Focus("ForzaHorizon5.exe");

		Assert.Equal("Forza Horizon 5", profiles.Active?.Name);
	}

	[Fact]
	public void GameAlreadyFocusedAtStart_IsActive()
	{
		FakeForegroundWatcher focused = new();
		focused.Focus("ForzaHorizon5.exe");

		using ProfileService service = new(settings, focused, "DualPulse.exe");

		Assert.Equal(Forza, service.Active);
	}

	[Fact]
	public void OldSettings_HaveNoProfiles()
	{
		Assert.Empty(new AppSettings().EffectiveProfiles);
	}
}
