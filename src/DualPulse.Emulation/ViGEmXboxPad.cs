using System.Runtime.Versioning;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;

namespace DualPulse.Emulation;

[SupportedOSPlatform("windows")]
public sealed class ViGEmXboxPad : IVirtualPad
{
	private static readonly (XboxButtons From, Xbox360Button To)[] ButtonMap =
	[
		(XboxButtons.A, Xbox360Button.A),
		(XboxButtons.B, Xbox360Button.B),
		(XboxButtons.X, Xbox360Button.X),
		(XboxButtons.Y, Xbox360Button.Y),
		(XboxButtons.LeftShoulder, Xbox360Button.LeftShoulder),
		(XboxButtons.RightShoulder, Xbox360Button.RightShoulder),
		(XboxButtons.Back, Xbox360Button.Back),
		(XboxButtons.Start, Xbox360Button.Start),
		(XboxButtons.Guide, Xbox360Button.Guide),
		(XboxButtons.LeftThumb, Xbox360Button.LeftThumb),
		(XboxButtons.RightThumb, Xbox360Button.RightThumb),
		(XboxButtons.Up, Xbox360Button.Up),
		(XboxButtons.Down, Xbox360Button.Down),
		(XboxButtons.Left, Xbox360Button.Left),
		(XboxButtons.Right, Xbox360Button.Right)
	];

	private readonly object gate = new();

	private readonly IXbox360Controller pad;

	private bool disposed;

	public ViGEmXboxPad(IXbox360Controller pad)
	{
		this.pad = pad;
		pad.AutoSubmitReport = false;
		pad.FeedbackReceived += OnFeedback;
		pad.Connect();
	}

	public PadKind Kind => PadKind.Xbox360;

	public event Action<RumbleFeedback>? Feedback;

	public void Submit(in InputSnapshot input)
	{
		XboxPadState state = InputMapper.ToXbox(input);
		lock (gate)
		{
			if (disposed)
			{
				return;
			}
			pad.SetAxisValue(Xbox360Axis.LeftThumbX, state.LeftX);
			pad.SetAxisValue(Xbox360Axis.LeftThumbY, state.LeftY);
			pad.SetAxisValue(Xbox360Axis.RightThumbX, state.RightX);
			pad.SetAxisValue(Xbox360Axis.RightThumbY, state.RightY);
			pad.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
			pad.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);
			foreach ((XboxButtons from, Xbox360Button to) in ButtonMap)
			{
				pad.SetButtonState(to, (state.Buttons & from) != 0);
			}
			pad.SubmitReport();
		}
	}

	public void Dispose()
	{
		lock (gate)
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
			pad.FeedbackReceived -= OnFeedback;
			pad.Disconnect();
		}
	}

	private void OnFeedback(object? sender, Xbox360FeedbackReceivedEventArgs e)
	{
		Feedback?.Invoke(new RumbleFeedback(e.LargeMotor, e.SmallMotor, null));
	}
}
