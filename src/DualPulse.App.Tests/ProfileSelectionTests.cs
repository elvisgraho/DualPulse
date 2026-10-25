using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ProfileSelectionTests : IDisposable
{
	private static readonly GameProfile Game = new("Game", "game.exe");

	private readonly FakeSettingsService settings = new(new AppSettings(
		Triggers: TriggerSettings.FromPreset(TriggerPreset.Weapon),
		Gyro: new GyroSettings(Sensitivity: 3),
		Profiles: [Game]));

	private readonly FakeControllerService controller = new();

	private readonly FakeForegroundWatcher watcher = new();

	private readonly ProfileService profiles;

	private readonly ProfileSelection selection;

	public ProfileSelectionTests()
	{
		profiles = new ProfileService(settings, watcher, "DualPulse.exe");
		selection = new ProfileSelection(settings, profiles);
	}

	public void Dispose() => profiles.Dispose();

	private GameProfile Saved => settings.Current.EffectiveProfiles.Single();

	[Fact]
	public void Selection_StartsOnDefault_AndFollowsTheActiveGame()
	{
		Assert.Null(selection.Selected);

		watcher.Focus("game.exe");

		Assert.Equal(Game, selection.Selected);
	}

	[Fact]
	public void Selection_StaysWhenTheGameLosesFocus()
	{
		watcher.Focus("game.exe");
		watcher.Focus("explorer.exe");

		Assert.Equal(Game, selection.Selected);
	}

	[Fact]
	public void Customize_CopiesTheDefault_UseDefault_ClearsIt()
	{
		selection.Select(Game);

		selection.Customize(ProfilePart.Triggers);
		Assert.Equal(settings.Current.EffectiveTriggers, Saved.Triggers);
		Assert.True(selection.UsesOwn(ProfilePart.Triggers));

		selection.UseDefault(ProfilePart.Triggers);
		Assert.Null(Saved.Triggers);
		Assert.False(selection.UsesOwn(ProfilePart.Triggers));
	}

	[Fact]
	public void SticksVm_EditsTheProfile_WhenItHasItsOwnSticks()
	{
		SticksViewModel viewModel = new(settings, controller, selection);
		selection.Select(Game);
		viewModel.Strip.CustomizeCommand.Execute(null);

		viewModel.Left.Deadzone = 12;

		Assert.Equal(0.12, Saved.Sticks!.Left.Deadzone, 3);
		Assert.Equal(0, settings.Current.EffectiveSticks.Left.Deadzone);
		Assert.Equal("Game uses its own sticks.", viewModel.Strip.Text);
	}

	[Fact]
	public void TriggersVm_EditsTheProfile_WhenItHasItsOwnTriggers()
	{
		TriggersViewModel viewModel = new(settings, controller, selection);
		TriggerSettings saved = settings.Current.EffectiveTriggers;
		selection.Select(Game);
		viewModel.Strip.CustomizeCommand.Execute(null);

		viewModel.Left.Strength = 2;

		Assert.Equal(2, Saved.Triggers!.Left.Strength);
		Assert.Equal(saved, settings.Current.EffectiveTriggers);
		Assert.Equal("Game uses its own triggers.", viewModel.Strip.Text);
		Assert.True(viewModel.Strip.CanEdit);
	}

	[Fact]
	public void TriggersVm_ReadOnly_WhileTheProfileFollowsTheDefault()
	{
		TriggersViewModel viewModel = new(settings, controller, selection);

		selection.Select(Game);
		viewModel.Left.Strength = 2;

		Assert.False(viewModel.Strip.CanEdit);
		Assert.True(viewModel.Strip.IsVisible);
		Assert.Equal("Follows your default triggers.", viewModel.Strip.Text);
		Assert.Equal(settings.Current.EffectiveTriggers.Left.Strength, viewModel.Left.Strength);
		Assert.Null(Saved.Triggers);
	}

	[Fact]
	public void Strip_HiddenOnDefault()
	{
		TriggersViewModel viewModel = new(settings, controller, selection);

		Assert.False(viewModel.Strip.IsVisible);
		Assert.True(viewModel.Strip.CanEdit);
	}

	[Fact]
	public void GyroVm_EditsTheProfileGyro()
	{
		GyroViewModel viewModel = new(settings, controller, new FakeGyroService(), selection);
		selection.Select(Game);
		Assert.Equal("Follows your default gyro.", viewModel.Strip.Text);
		viewModel.Strip.CustomizeCommand.Execute(null);

		viewModel.Enabled = true;

		Assert.True(Saved.Gyro!.Enabled);
		Assert.Equal(3, Saved.Gyro.Sensitivity);
		Assert.False(settings.Current.EffectiveGyro.Enabled);
		Assert.Equal("Game uses its own gyro.", viewModel.Strip.Text);
	}
}
