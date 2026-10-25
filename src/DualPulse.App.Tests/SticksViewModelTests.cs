using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class SticksViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakeControllerService controller = new();

	private SticksViewModel CreateViewModel() => new(settings, controller);

	[Fact]
	public void Sticks_StartUnchanged()
	{
		SticksViewModel viewModel = CreateViewModel();

		Assert.True(viewModel.Linked);
		Assert.Equal(0, viewModel.Left.Deadzone);
		Assert.Equal(100, viewModel.Left.FullTiltAt);
		Assert.Equal("Left and right stick", viewModel.Left.Title);
	}

	[Fact]
	public void Sticks_Edits_AreSaved_AndApplyToBothWhileLinked()
	{
		SticksViewModel viewModel = CreateViewModel();

		viewModel.Left.Deadzone = 10;
		viewModel.Left.FullTiltAt = 90;

		StickSettings sticks = settings.Current.EffectiveSticks;
		Assert.Equal(new StickRange(0.1, 0.9), sticks.Left);
		Assert.Equal(sticks.Left, sticks.EffectiveRight);
	}

	[Fact]
	public void Sticks_Unlinked_EditEachStick()
	{
		SticksViewModel viewModel = CreateViewModel();

		viewModel.Linked = false;
		viewModel.Right.Deadzone = 20;

		Assert.Equal("Left stick", viewModel.Left.Title);
		Assert.Equal(0.2, settings.Current.EffectiveSticks.EffectiveRight.Deadzone, 3);
		Assert.Equal(0, settings.Current.EffectiveSticks.Left.Deadzone);
	}

	[Theory]
	[InlineData(PadKind.Xbox360, false)]
	[InlineData(null, true)]
	public void Sticks_InNativeMode_SayTheyNeedAVirtualController(PadKind? kind, bool note)
	{
		settings.Update(s => s with { Emulation = kind });

		Assert.Equal(note, CreateViewModel().ShowNativeNote);
	}
}
