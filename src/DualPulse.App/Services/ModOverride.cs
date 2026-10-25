using DualPulse.Core.Devices;
using DualPulse.Core.Mods;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

// What a game mod currently controls. While a part is set here the user's own settings for it are not applied;
// Clear() hands everything back (the controller and lighting services re-apply the settings on Released).
public sealed class ModOverride
{
	private readonly object gate = new();

	private TriggerEffect? leftTrigger;

	private TriggerEffect? rightTrigger;

	private Rgb? lightbar;

	private byte? playerLeds;

	private MicLed? micLed;

	public event Action? Released;

	public bool IsActive
	{
		get
		{
			lock (gate)
			{
				return leftTrigger != null || rightTrigger != null || lightbar != null || playerLeds != null || micLed != null;
			}
		}
	}

	public bool ControlsLeftTrigger { get { lock (gate) { return leftTrigger != null; } } }

	public bool ControlsRightTrigger { get { lock (gate) { return rightTrigger != null; } } }

	public bool ControlsLightbar { get { lock (gate) { return lightbar != null; } } }

	public bool ControlsPlayerLeds { get { lock (gate) { return playerLeds != null; } } }

	public bool ControlsMicLed { get { lock (gate) { return micLed != null; } } }

	// Records a mod instruction; returns false for instructions that don't change the controller.
	public bool Set(ModInstruction instruction)
	{
		lock (gate)
		{
			switch (instruction)
			{
			case ModTriggerEffect { Trigger: ModTrigger.Left } effect:
				leftTrigger = effect.Effect;
				return true;
			case ModTriggerEffect { Trigger: ModTrigger.Right } effect:
				rightTrigger = effect.Effect;
				return true;
			case ModTriggerEffect effect:
				leftTrigger = effect.Effect;
				rightTrigger = effect.Effect;
				return true;
			case ModLightbar colour:
				lightbar = colour.Colour;
				return true;
			case ModPlayerLeds leds:
				playerLeds = leds.Bits;
				return true;
			case ModMicLed mic:
				micLed = mic.MicLed;
				return true;
			default:
				return false;
			}
		}
	}

	// Writes the parts the mod controls to the controller.
	public void ApplyTo(ControllerSession session)
	{
		TriggerEffect? left;
		TriggerEffect? right;
		Rgb? colour;
		byte? leds;
		MicLed? mic;
		lock (gate)
		{
			(left, right, colour, leds, mic) = (leftTrigger, rightTrigger, lightbar, playerLeds, micLed);
		}
		if (left == null && right == null && colour == null && leds == null && mic == null)
		{
			return;
		}
		session.Apply(state => state with
		{
			LeftTrigger = left ?? state.LeftTrigger,
			RightTrigger = right ?? state.RightTrigger,
			Lightbar = colour ?? state.Lightbar,
			PlayerLeds = leds ?? state.PlayerLeds,
			MicLed = mic ?? state.MicLed
		});
	}

	public void Clear()
	{
		bool wasActive;
		lock (gate)
		{
			wasActive = leftTrigger != null || rightTrigger != null || lightbar != null || playerLeds != null || micLed != null;
			(leftTrigger, rightTrigger, lightbar, playerLeds, micLed) = (null, null, null, null, null);
		}
		if (wasActive)
		{
			Released?.Invoke();
		}
	}
}
