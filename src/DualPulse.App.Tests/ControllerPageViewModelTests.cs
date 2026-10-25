using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ControllerPageViewModelTests : IDisposable
{
	private static readonly GameProfile Forza = new("Forza Horizon 5", "ForzaHorizon5.exe");

	private readonly FakeSettingsService settings = new(new AppSettings(Profiles: [Forza]));

	private readonly FakeForegroundWatcher watcher = new();

	private readonly FakeRunningWindows windows = new();

	private readonly ProfileService profiles;

	private readonly ProfileSelection selection;

	private readonly ControllerPageViewModel page;

	public ControllerPageViewModelTests()
	{
		profiles = new ProfileService(settings, watcher, "DualPulse.exe");
		selection = new ProfileSelection(settings, profiles);
		page = new ControllerPageViewModel(selection: selection, settings: settings, windows: windows);
	}

	public void Dispose() => profiles.Dispose();

	[Fact]
	public void Profiles_ListDefaultThenGames()
	{
		Assert.Equal(["Default", "Forza Horizon 5"], page.Profiles.Select(p => p.Label));
		Assert.Null(page.SelectedProfile?.Exe);
	}

	[Fact]
	public void Profiles_UpdatedInPlace_SoTheSelectorKeepsItsSelection()
	{
		// Replacing the list makes the selector re-select its old item afterwards, undoing a new selection.
		IReadOnlyList<ProfileChoice> list = page.Profiles;
		windows.Windows = [new RunningWindow("Cyberpunk 2077", "Cyberpunk2077.exe")];

		page.StartAddGameCommand.Execute(null);
		page.PickGameCommand.Execute(page.RunningGames[0]);

		Assert.Same(list, page.Profiles);
		Assert.Equal(3, list.Count);
	}

	[Fact]
	public void SelectingAProfile_SelectsItForEditing()
	{
		page.SelectedProfile = page.Profiles[1];

		Assert.Equal(Forza, selection.Selected);
		Assert.True(page.ShowSameForAllGames);
		Assert.True(page.CanRemove);
	}

	[Fact]
	public void IsActiveNow_WhileTheSelectedGameHasFocus()
	{
		watcher.Focus("ForzaHorizon5.exe");
		Assert.True(page.IsActiveNow);

		watcher.Focus("explorer.exe");
		Assert.False(page.IsActiveNow);
	}

	[Fact]
	public void AddGame_ListsRunningPrograms_AndPickingOneCreatesAndSelectsItsProfile()
	{
		windows.Windows = [new RunningWindow("Cyberpunk 2077", "Cyberpunk2077.exe")];

		page.StartAddGameCommand.Execute(null);
		Assert.True(page.IsAddingGame);
		page.PickGameCommand.Execute(page.RunningGames[0]);

		Assert.False(page.IsAddingGame);
		Assert.Contains(settings.Current.EffectiveProfiles, p => p == new GameProfile("Cyberpunk 2077", "Cyberpunk2077.exe"));
		Assert.Equal("Cyberpunk2077.exe", selection.Selected?.Exe);
		Assert.Equal("Cyberpunk 2077", page.SelectedProfile?.Label);
	}

	[Fact]
	public void AddGame_ExistingExe_SelectsTheExistingProfile()
	{
		windows.Windows = [new RunningWindow("Forza Horizon 5 - window title", "forzahorizon5.exe")];

		page.StartAddGameCommand.Execute(null);
		page.PickGameCommand.Execute(page.RunningGames[0]);

		Assert.Single(settings.Current.EffectiveProfiles);
		Assert.Equal(Forza, selection.Selected);
	}

	[Fact]
	public void RemoveProfile_DeletesIt_AndGoesBackToDefault()
	{
		page.SelectedProfile = page.Profiles[1];

		page.RemoveProfileCommand.Execute(null);

		Assert.Empty(settings.Current.EffectiveProfiles);
		Assert.Null(selection.Selected);
		Assert.Equal(["Default"], page.Profiles.Select(p => p.Label));
	}
}
