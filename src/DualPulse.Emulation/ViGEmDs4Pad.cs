using System.Runtime.Versioning;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;

namespace DualPulse.Emulation;

[SupportedOSPlatform("windows")]
public sealed class ViGEmDs4Pad : IVirtualPad
{
	private static readonly (Buttons From, DualShock4Button To)[] ButtonMap =
	[
		(Buttons.Square, DualShock4Button.Square),
		(Buttons.Cross, DualShock4Button.Cross),
		(Buttons.Circle, DualShock4Button.Circle),
		(Buttons.Triangle, DualShock4Button.Triangle),
		(Buttons.L1, DualShock4Button.ShoulderLeft),
		(Buttons.R1, DualShock4Button.ShoulderRight),
		(Buttons.L2, DualShock4Button.TriggerLeft),
		(Buttons.R2, DualShock4Button.TriggerRight),
		(Buttons.Create, DualShock4Button.Share),
		(Buttons.Options, DualShock4Button.Options),
		(Buttons.L3, DualShock4Button.ThumbLeft),
		(Buttons.R3, DualShock4Button.ThumbRight)
	];

	private readonly object gate = new();

	private readonly IDualShock4Controller pad;

	private bool disposed;

	public ViGEmDs4Pad(IDualShock4Controller pad)
	{
		this.pad = pad;
		pad.AutoSubmitReport = false;
		// FeedbackReceived is marked obsolete in favour of AwaitRawOutputReport (raw report bytes, layout unverified);
		// the event still works in 1.21.256. Revisit when DS4 feedback is reworked.
#pragma warning disable CS0618
		pad.FeedbackReceived += OnFeedback;
#pragma warning restore CS0618
		pad.Connect();
	}

	public PadKind Kind => PadKind.DualShock4;

	public event Action<RumbleFeedback>? Feedback;

	public void Submit(in InputSnapshot input)
	{
		Ds4PadState state = InputMapper.ToDs4(input);
		lock (gate)
		{
			if (disposed)
			{
				return;
			}
			pad.SetAxisValue(DualShock4Axis.LeftThumbX, state.LeftX);
			pad.SetAxisValue(DualShock4Axis.LeftThumbY, state.LeftY);
			pad.SetAxisValue(DualShock4Axis.RightThumbX, state.RightX);
			pad.SetAxisValue(DualShock4Axis.RightThumbY, state.RightY);
			pad.SetSliderValue(DualShock4Slider.LeftTrigger, state.L2);
			pad.SetSliderValue(DualShock4Slider.RightTrigger, state.R2);
			foreach ((Buttons from, DualShock4Button to) in ButtonMap)
			{
				pad.SetButtonState(to, (state.Buttons & from) != 0);
			}
			pad.SetButtonState(DualShock4SpecialButton.Ps, (state.Buttons & Buttons.Ps) != 0);
			pad.SetButtonState(DualShock4SpecialButton.Touchpad, (state.Buttons & Buttons.Touchpad) != 0);
			pad.SetDPadDirection(DPad(state.Buttons));
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
#pragma warning disable CS0618
			pad.FeedbackReceived -= OnFeedback;
#pragma warning restore CS0618
			pad.Disconnect();
		}
	}

	private static DualShock4DPadDirection DPad(Buttons buttons)
	{
		bool up = (buttons & Buttons.DpadUp) != 0;
		bool down = (buttons & Buttons.DpadDown) != 0;
		bool left = (buttons & Buttons.DpadLeft) != 0;
		bool right = (buttons & Buttons.DpadRight) != 0;
		return (up, down, left, right) switch
		{
			(true, _, true, _) => DualShock4DPadDirection.Northwest,
			(true, _, _, true) => DualShock4DPadDirection.Northeast,
			(_, true, true, _) => DualShock4DPadDirection.Southwest,
			(_, true, _, true) => DualShock4DPadDirection.Southeast,
			(true, _, _, _) => DualShock4DPadDirection.North,
			(_, true, _, _) => DualShock4DPadDirection.South,
			(_, _, true, _) => DualShock4DPadDirection.West,
			(_, _, _, true) => DualShock4DPadDirection.East,
			_ => DualShock4DPadDirection.None
		};
	}

	private void OnFeedback(object? sender, DualShock4FeedbackReceivedEventArgs e)
	{
		Feedback?.Invoke(new RumbleFeedback(e.LargeMotor, e.SmallMotor, new Rgb(e.LightbarColor.Red, e.LightbarColor.Green, e.LightbarColor.Blue)));
	}
}
