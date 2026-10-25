using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class TriggersViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakeControllerService controller = new();

	private TriggersViewModel CreateViewModel() => new(settings, controller);

	private static TriggerKindChoice Kind(TriggerEditorViewModel editor, TriggerKind kind) => editor.Kinds.Single(k => k.Kind == kind);

	[Fact]
	public void Triggers_Range_PersistsAsFractions_AndClamps()
	{
		TriggersViewModel viewModel = CreateViewModel();

		viewModel.Left.Deadzone = 10;
		viewModel.Left.StartAt = 50;

		TriggerEffectSettings left = settings.Current.EffectiveTriggers.Left;
		Assert.Equal(0.1, left.Deadzone, 3);
		Assert.Equal(0.3, left.StartAt, 3);
		Assert.Equal(30, viewModel.Left.StartAt);
	}

	[Fact]
	public void Triggers_Range_DoesNotMakeAPresetCustom_AndSurvivesChoosingAPreset()
	{
		TriggersViewModel viewModel = CreateViewModel();
		viewModel.ApplyPresetCommand.Execute(TriggerPreset.Weapon);

		viewModel.Left.Deadzone = 10;
		Assert.Equal(TriggerPreset.Weapon, settings.Current.TriggerPreset);

		viewModel.ApplyPresetCommand.Execute(TriggerPreset.Feedback);
		Assert.Equal(10, viewModel.Left.Deadzone);
		Assert.Equal(TriggerKind.Resistance, settings.Current.EffectiveTriggers.Left.Kind);
	}

	[Fact]
	public void Triggers_KindLabels()
	{
		TriggersViewModel viewModel = CreateViewModel();

		Assert.Equal(["Off", "Resistance", "Weapon", "Vibration", "Ramp", "Custom zones", "Custom vibration"], viewModel.Left.Kinds.Select(k => k.Label));
		Assert.Equal(["Off", "Feedback", "Weapon", "Vibration"], viewModel.Presets.Select(p => p.ToString()));
	}

	[Theory]
	[InlineData(TriggerKind.Resistance, true, false, false, false, false)]
	[InlineData(TriggerKind.Weapon, true, true, false, false, false)]
	[InlineData(TriggerKind.Vibration, true, false, false, true, false)]
	[InlineData(TriggerKind.Ramp, true, true, true, false, false)]
	[InlineData(TriggerKind.Zones, false, false, false, false, true)]
	[InlineData(TriggerKind.ZoneVibration, false, false, false, true, true)]
	public void Triggers_EachEffect_ShowsOnlyItsControls(TriggerKind kind, bool startAndStrength, bool end, bool endStrength, bool frequency, bool bars)
	{
		TriggersViewModel viewModel = CreateViewModel();

		viewModel.Left.SelectedKind = Kind(viewModel.Left, kind);

		Assert.Equal(startAndStrength, viewModel.Left.ShowStartAndStrength);
		Assert.Equal(end, viewModel.Left.ShowEnd);
		Assert.Equal(endStrength, viewModel.Left.ShowEndStrength);
		Assert.Equal(frequency, viewModel.Left.ShowFrequency);
		Assert.Equal(bars, viewModel.Left.ShowZoneBars);
	}

	[Fact]
	public void Triggers_DrawnZones_AreSaved()
	{
		TriggersViewModel viewModel = CreateViewModel();
		viewModel.Left.SelectedKind = Kind(viewModel.Left, TriggerKind.Zones);

		viewModel.Left.Zones = [8, 8, 6, 4, 2, 0, 0, 0, 0, 0];

		Assert.Equal([8, 8, 6, 4, 2, 0, 0, 0, 0, 0], settings.Current.EffectiveTriggers.Left.EffectiveZones);
		Assert.Equal([8, 8, 6, 4, 2, 0, 0, 0, 0, 0], viewModel.Left.Zones);
	}

	[Fact]
	public void Triggers_LinkedEdit_AppliesToBoth_AndMarksCustom()
	{
		TriggersViewModel viewModel = CreateViewModel();

		viewModel.Left.SelectedKind = Kind(viewModel.Left, TriggerKind.Resistance);
		viewModel.Left.Strength = 3;

		TriggerSettings triggers = settings.Current.EffectiveTriggers;
		Assert.True(triggers.Linked);
		Assert.Equal(TriggerKind.Resistance, triggers.Left.Kind);
		Assert.Equal(3, triggers.Left.Strength);
		Assert.Equal(triggers.Left, triggers.EffectiveRight);
		Assert.Equal(TriggerPreset.Custom, settings.Current.TriggerPreset);
		Assert.Equal("L2 and R2", viewModel.Left.Title);
	}

	[Fact]
	public void Triggers_Unlinked_RightEditsOnlyRight()
	{
		TriggersViewModel viewModel = CreateViewModel();

		viewModel.Linked = false;
		viewModel.Right.SelectedKind = Kind(viewModel.Right, TriggerKind.Vibration);
		viewModel.Right.Frequency = 80;

		TriggerSettings triggers = settings.Current.EffectiveTriggers;
		Assert.False(triggers.Linked);
		Assert.Equal(TriggerKind.Off, triggers.Left.Kind);
		Assert.Equal(TriggerKind.Vibration, triggers.Right.Kind);
		Assert.Equal(80, triggers.Right.Frequency);
		Assert.Equal("L2", viewModel.Left.Title);
		Assert.Equal("R2", viewModel.Right.Title);
	}

	[Fact]
	public void Triggers_Preset_SetsValuesAndPreset()
	{
		TriggersViewModel viewModel = CreateViewModel();
		viewModel.Linked = false;

		viewModel.ApplyPresetCommand.Execute(TriggerPreset.Weapon);

		Assert.Equal(TriggerPreset.Weapon, settings.Current.TriggerPreset);
		Assert.Equal(TriggerSettings.FromPreset(TriggerPreset.Weapon), settings.Current.Triggers);
		Assert.True(viewModel.Linked);
		Assert.Equal(TriggerKind.Weapon, viewModel.Left.SelectedKind.Kind);
	}

	[Fact]
	public void Triggers_WeaponRanges_KeepAValidEffect()
	{
		TriggersViewModel viewModel = CreateViewModel();

		viewModel.Left.SelectedKind = Kind(viewModel.Left, TriggerKind.Weapon);
		Assert.Equal(2, viewModel.Left.StartMinimum);
		Assert.Equal(7, viewModel.Left.StartMaximum);
		Assert.True(viewModel.Left.ShowEnd);
		Assert.False(viewModel.Left.ShowFrequency);

		viewModel.Left.Start = 7;

		Assert.Equal(8, viewModel.Left.EndMinimum);
		Assert.Equal(8, viewModel.Left.End);
		Assert.Equal(8, settings.Current.EffectiveTriggers.Left.End);
	}

	[Fact]
	public void Triggers_EditingBackToPresetValues_IsRecognised()
	{
		TriggersViewModel viewModel = CreateViewModel();
		viewModel.ApplyPresetCommand.Execute(TriggerPreset.Weapon);

		viewModel.Left.Strength = 7;
		Assert.Equal(TriggerPreset.Custom, settings.Current.TriggerPreset);

		viewModel.Left.Strength = 8;
		Assert.Equal(TriggerPreset.Weapon, settings.Current.TriggerPreset);
	}

	[Fact]
	public void Triggers_ExternalPreset_UpdatesEditors()
	{
		TriggersViewModel viewModel = CreateViewModel();

		settings.Update(s => s with { TriggerPreset = TriggerPreset.Vibration, Triggers = TriggerSettings.FromPreset(TriggerPreset.Vibration) });

		Assert.Equal(TriggerKind.Vibration, viewModel.Left.SelectedKind.Kind);
		Assert.True(viewModel.Left.ShowFrequency);
	}

	[Fact]
	public void Triggers_Travel_ZeroWithoutController()
	{
		TriggersViewModel viewModel = CreateViewModel();

		viewModel.RefreshTravel();

		Assert.Equal(0, viewModel.LeftTravel);
		Assert.Equal(0, viewModel.RightTravel);
	}

	[Fact]
	public void Triggers_ResistanceAtLastZone_Works()
	{
		TriggersViewModel viewModel = CreateViewModel();
		viewModel.Left.SelectedKind = Kind(viewModel.Left, TriggerKind.Resistance);

		viewModel.Left.Start = 9;

		Assert.Equal(9, settings.Current.EffectiveTriggers.Left.Start);
	}
}
