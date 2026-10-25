namespace DualPulse.Core.Protocol;

public readonly record struct Rgb(byte R, byte G, byte B);

public enum MicLed : byte
{
	Off = 0,
	On = 1,
	Pulse = 2
}

// Where the controller plays audio sent over Bluetooth (report 0x39).
public enum AudioOutput
{
	Off,
	Speaker,
	Headset
}

public enum LedBrightness : byte
{
	High = 0,
	Medium = 1,
	Low = 2
}

// The complete output state of a controller. Every output report carries all of it, so only the newest state
// ever needs sending. LeftMotor is the large (low-frequency) motor in the left grip. HapticsMode keeps the actuators
// free for the Bluetooth haptics stream (report 0x32): no rumble, no power reduction. AudioOutput and AudioVolume
// (0-100) select and set the speaker or headset for Bluetooth audio. GameBlock is the 47-byte common block a game sent
// to a virtual DualSense; while set it is sent as it is instead of the block composed from the fields above.
public sealed record OutputState(
	Rgb Lightbar,
	byte PlayerLeds,
	LedBrightness PlayerLedBrightness,
	MicLed MicLed,
	TriggerEffect LeftTrigger,
	TriggerEffect RightTrigger,
	byte LeftMotor,
	byte RightMotor,
	bool ImprovedRumble,
	bool FadeOutBootLight,
	bool HapticsMode = false,
	AudioOutput AudioOutput = AudioOutput.Off,
	byte AudioVolume = 70,
	byte[]? GameBlock = null)
{
	public const int CommonBlockLength = 47;

	// Bluetooth audio goes to the headset jack: the game's choice (audio control path bits other than 0x30, the
	// speaker) while it drives the controller, otherwise the chosen output.
	public bool UsesHeadset => GameBlock is { Length: >= CommonBlockLength } block ? (block[7] & 0x30) != 0x30 : AudioOutput == AudioOutput.Headset;

	public static OutputState Default { get; } = new(
		Lightbar: new Rgb(0, 0, 0),
		PlayerLeds: 0b00100,
		PlayerLedBrightness: LedBrightness.High,
		MicLed: MicLed.Off,
		LeftTrigger: TriggerEffect.Off,
		RightTrigger: TriggerEffect.Off,
		LeftMotor: 0,
		RightMotor: 0,
		ImprovedRumble: false,
		FadeOutBootLight: true);
}
