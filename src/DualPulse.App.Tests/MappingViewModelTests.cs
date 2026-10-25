using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class MappingViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakePcControlService pc = new();

	private MappingViewModel CreateViewModel() => new(settings, pc);

	[Fact]
	public void PcControl_ToggleAndStatus()
	{
		MappingViewModel viewModel = CreateViewModel();
		Assert.Equal("Off", viewModel.StatusText);

		viewModel.TogglePcControlCommand.Execute(null);

		Assert.True(pc.IsActive);
		Assert.True(viewModel.PcControlOn);
		Assert.Equal("On: the controller drives the mouse and keyboard", viewModel.StatusText);
	}

	[Fact]
	public void PointerOptions_Persist()
	{
		MappingViewModel viewModel = CreateViewModel();

		viewModel.TouchpadSpeed = 1.5;
		viewModel.StickMouseEnabled = false;
		viewModel.StickScrollEnabled = false;
		viewModel.TouchpadEnabled = false;
		viewModel.StickSpeed = 0.5;

		Assert.Equal(new MappingSettings(TouchpadEnabled: false, TouchpadSpeed: 1.5, StickMouseEnabled: false, StickSpeed: 0.5, StickScrollEnabled: false),
			settings.Current.EffectiveMapping);
	}

	[Fact]
	public void ButtonRows_ShowTheDefaults()
	{
		MappingViewModel viewModel = CreateViewModel();

		ButtonRow cross = viewModel.ButtonRows.Single(r => r.Button == Buttons.Cross);
		Assert.Equal("Cross", cross.Label);
		Assert.Equal(PcAction.Enter, cross.SelectedAction.Action);
		Assert.Equal("Enter", cross.SelectedAction.Label);
		Assert.DoesNotContain(viewModel.ButtonRows, r => r.Button == Buttons.Ps);
		Assert.Equal(PcAction.None, viewModel.ButtonRows.Single(r => r.Button == Buttons.R3).SelectedAction.Action);
	}

	[Fact]
	public void ChangingARow_SavesTheWholeTable()
	{
		MappingViewModel viewModel = CreateViewModel();
		ButtonRow cross = viewModel.ButtonRows.Single(r => r.Button == Buttons.Cross);

		cross.SelectedAction = viewModel.ActionChoices.Single(c => c.Action == PcAction.PlayPause);

		IReadOnlyDictionary<Buttons, PcAction> saved = settings.Current.EffectiveMapping.ButtonActions();
		Assert.Equal(PcAction.PlayPause, saved[Buttons.Cross]);
		Assert.Equal(PcAction.Escape, saved[Buttons.Circle]);
	}

	[Fact]
	public void ResetButtons_RestoresTheDefaults()
	{
		MappingViewModel viewModel = CreateViewModel();
		viewModel.ButtonRows.Single(r => r.Button == Buttons.Cross).SelectedAction = viewModel.ActionChoices[0];

		viewModel.ResetButtonsCommand.Execute(null);

		Assert.Null(settings.Current.EffectiveMapping.Buttons);
		Assert.Equal(PcAction.Enter, viewModel.ButtonRows.Single(r => r.Button == Buttons.Cross).SelectedAction.Action);
	}

	[Fact]
	public void HoldLayers_PersistAndLockTheirButtons()
	{
		MappingViewModel viewModel = CreateViewModel();
		ButtonRow l1 = viewModel.ButtonRows.Single(r => r.Button == Buttons.L1);
		ButtonRow r1 = viewModel.ButtonRows.Single(r => r.Button == Buttons.R1);
		Assert.True(viewModel.WindowSwitcherEnabled);
		Assert.False(l1.IsEditable);
		Assert.Equal("Hold: window switcher", l1.Note);
		Assert.Equal("Hold: media controls", r1.Note);

		viewModel.WindowSwitcherEnabled = false;
		viewModel.MediaLayerEnabled = false;

		Assert.False(settings.Current.EffectiveMapping.WindowSwitcherEnabled);
		Assert.False(settings.Current.EffectiveMapping.MediaLayerEnabled);
		Assert.True(l1.IsEditable);
		Assert.Equal("", r1.Note);
	}
}
