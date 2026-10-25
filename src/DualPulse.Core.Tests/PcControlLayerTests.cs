using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class PcControlLayerTests
{
	private readonly PcControl control = new();

	private readonly RecordingOutput output = new();

	private TimeSpan now;

	private void Report(Buttons buttons, byte rightX = 128, byte rightY = 128, TouchPoint touch = default, int afterMs = 8)
	{
		now += TimeSpan.FromMilliseconds(afterMs);
		control.Process(new InputSnapshot { LeftX = 128, LeftY = 128, RightX = rightX, RightY = rightY, Buttons = buttons, Touch0 = touch }, now, output);
	}

	[Fact]
	public void HoldingL1_OpensTheSwitcherAtOnce_ReleasingSwitches()
	{
		Report(Buttons.L1);
		Assert.Equal(["Alt down", "Tab down", "Tab up"], output.Events);

		Report(Buttons.None);
		Assert.Equal("Alt up", output.Events[^1]);
	}

	[Fact]
	public void InTheSwitcher_DpadR1AndL2_MoveThroughWindows()
	{
		Report(Buttons.L1);
		output.Events.Clear();

		Report(Buttons.L1 | Buttons.DpadRight);
		Report(Buttons.L1);
		Report(Buttons.L1 | Buttons.R1);
		Report(Buttons.L1);
		Report(Buttons.L1 | Buttons.L2);

		Assert.Equal(["Right down", "Right up", "Tab down", "Tab up", "Shift down", "Tab down", "Tab up", "Shift up"], output.Events);
	}

	[Fact]
	public void InTheSwitcher_TheRightStickSteps_InsteadOfMovingThePointer()
	{
		Report(Buttons.L1);
		output.Events.Clear();

		Report(Buttons.L1, rightX: 255);
		Report(Buttons.L1, rightX: 255, afterMs: 200);
		Assert.Equal(["Right down", "Right up"], output.Events);

		Report(Buttons.L1, rightX: 255, afterMs: 200);
		Assert.Equal(4, output.Events.Count);
		Assert.Equal((0, 0), output.Moved);
	}

	[Fact]
	public void InTheSwitcher_CrossPicks_CircleCancels()
	{
		Report(Buttons.L1);
		Report(Buttons.L1 | Buttons.Cross);
		Assert.Equal("Alt up", output.Events[^1]);
		int afterPick = output.Events.Count;
		Report(Buttons.None);
		Assert.Equal(afterPick, output.Events.Count);

		output.Events.Clear();
		Report(Buttons.L1);
		Report(Buttons.L1 | Buttons.Circle);
		Assert.Equal(["Alt down", "Tab down", "Tab up", "Escape down", "Escape up", "Alt up"], output.Events);
	}

	[Fact]
	public void InTheSwitcher_TheTouchpadDoesNotMoveThePointer()
	{
		Report(Buttons.L1, touch: new TouchPoint(true, 1, 100, 100));
		Report(Buttons.L1, touch: new TouchPoint(true, 1, 600, 100));

		Assert.Equal((0, 0), output.Moved);
	}

	[Fact]
	public void HoldingR1_GivesMediaControls()
	{
		Report(Buttons.R1);
		Assert.Empty(output.Events);

		Report(Buttons.R1 | Buttons.DpadUp);
		Report(Buttons.R1);
		Report(Buttons.R1 | Buttons.DpadRight);
		Report(Buttons.R1);
		Report(Buttons.R1 | Buttons.Cross);
		Report(Buttons.R1);
		Report(Buttons.R1 | Buttons.Square);
		Report(Buttons.None);

		Assert.Equal(["VolumeUp down", "VolumeUp up", "NextTrack down", "NextTrack up", "PlayPause down", "PlayPause up", "VolumeMute down", "VolumeMute up"],
			output.Events);
	}

	[Fact]
	public void MediaLayer_RightStickUp_StepsTheVolume()
	{
		Report(Buttons.R1);
		Report(Buttons.R1, rightY: 0);

		Assert.Equal(["VolumeUp down", "VolumeUp up"], output.Events);
	}

	[Fact]
	public void LayersOff_L1AndR1_FollowTheButtonTable()
	{
		control.WindowSwitcherEnabled = false;
		control.MediaLayerEnabled = false;
		control.ButtonActions = new Dictionary<Buttons, PcAction> { [Buttons.L1] = PcAction.Alt, [Buttons.R1] = PcAction.Tab };

		Report(Buttons.L1);
		Report(Buttons.L1 | Buttons.R1);

		Assert.Equal(["Alt down", "Tab down"], output.Events);
	}

	[Fact]
	public void Release_ClosesAnOpenSwitcher()
	{
		Report(Buttons.L1);

		control.Release(output);

		Assert.Equal("Alt up", output.Events[^1]);
	}
}
