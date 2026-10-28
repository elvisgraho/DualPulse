using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ProfileRenameTests : IDisposable
{
	private static readonly GameProfile Forza = new("Forza Horizon 5", "ForzaHorizon5.exe");

	private static readonly GameProfile Racing = new("Racing", GameProfile.NamedPrefix + "Racing", Triggers: TriggerSettings.FromPreset(TriggerPreset.Weapon));

	private static readonly GameProfile Shooter = new("Shooter", GameProfile.NamedPrefix + "Shooter");

	private readonly FakeSettingsService settings = new(new AppSettings(Profiles: [Forza, Racing, Shooter], ChosenProfile: Racing.Exe));

	private readonly FakeForegroundWatcher watcher = new();

	private readonly ProfileService profiles;

	private readonly ProfileSelection selection;

	private readonly ControllerPageViewModel page;

	public ProfileRenameTests()
	{
		profiles = new ProfileService(settings, watcher, "DualPulse.exe");
		selection = new ProfileSelection(settings, profiles);
		page = new ControllerPageViewModel(selection: selection, settings: settings);
	}

	public void Dispose() => profiles.Dispose();

	private void Rename(string name)
	{
		page.StartRenameProfileCommand.Execute(null);
		page.RenameProfileName = name;
		page.SaveRenameCommand.Execute(null);
	}

	[Fact]
	public void Rename_StartsWithTheCurrentName_OnlyWithAProfileSelected()
	{
		selection.Select(null);
		Assert.False(page.StartRenameProfileCommand.CanExecute(null));

		selection.Select(Forza);
		page.StartRenameProfileCommand.Execute(null);

		Assert.True(page.IsRenamingProfile);
		Assert.Equal("Forza Horizon 5", page.RenameProfileName);
	}

	[Fact]
	public void RenamingANamedProfile_KeepsItsSettings_AndKeepsItChosen()
	{
		selection.Select(Racing);

		Rename("Drift");

		GameProfile renamed = settings.Current.EffectiveProfiles[1];
		Assert.Equal(new GameProfile("Drift", GameProfile.NamedPrefix + "Drift", Triggers: Racing.Triggers), renamed);
		Assert.Equal(renamed.Exe, settings.Current.ChosenProfile);
		Assert.Equal(renamed.Exe, selection.Selected?.Exe);
		Assert.False(page.IsRenamingProfile);
	}

	[Fact]
	public void RenamingAGameProfile_ChangesOnlyItsName()
	{
		selection.Select(Forza);

		Rename("FH5");

		Assert.Equal(Forza with { Name = "FH5" }, settings.Current.EffectiveProfiles[0]);
	}

	[Fact]
	public void ANameAlreadyTaken_GetsANumber()
	{
		selection.Select(Racing);

		Rename("Shooter");

		Assert.Equal("Shooter 2", settings.Current.EffectiveProfiles[1].Name);
	}

	[Fact]
	public void AnEmptyName_CantBeSaved()
	{
		selection.Select(Racing);
		page.StartRenameProfileCommand.Execute(null);

		page.RenameProfileName = "  ";

		Assert.False(page.SaveRenameCommand.CanExecute(null));
	}
}
