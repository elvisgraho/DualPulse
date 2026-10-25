using System.Buffers.Binary;
using System.IO.Hashing;

namespace DualPulse.Core.Protocol;

// Builds DualSense output reports from an OutputState: USB 0x02 (48 bytes, common block at 1) and Bluetooth 0x31
// (78 bytes, common block at 2 or 3 depending on the layout, CRC-32 over 0xA2 + bytes 0..73 at 74..77).
// Offsets per docs/bluetooth-report-audit.md.
public static class OutputReportBuilder
{
	public const int UsbLength = 48;

	public const int BluetoothLength = 78;

	private const byte UsbReportId = 0x02;

	private const byte BluetoothReportId = 0x31;

	private const byte BluetoothCrcSeed = 0xA2;

	// valid_flag0
	private const byte CompatibleVibration = 0x01;
	private const byte HapticsSelect = 0x02;
	private const byte RightTriggerEffect = 0x04;
	private const byte LeftTriggerEffect = 0x08;
	private const byte HeadphoneVolumeControl = 0x10;
	private const byte SpeakerVolumeControl = 0x20;
	private const byte AudioControl = 0x80;

	// valid_flag1
	private const byte MicLedControl = 0x01;
	private const byte LightbarControl = 0x04;
	private const byte PlayerLedControl = 0x10;
	private const byte PowerSaveControl = 0x02;
	private const byte HapticsLowPassFilter = 0x20;
	private const byte MotorPowerControl = 0x40;

	// valid_flag2
	private const byte LedBrightnessControl = 0x01;
	private const byte LightbarSetupControl = 0x02;
	private const byte CompatibleVibration2 = 0x04;

	// Common block offsets.
	private const int Flag0 = 0;
	private const int Flag1 = 1;
	private const int MotorRight = 2;
	private const int MotorLeft = 3;
	private const int HeadphoneVolumeOffset = 4;
	private const int SpeakerVolumeOffset = 5;
	private const int AudioControlOffset = 7;
	private const int SpeakerVolumeQuiet = 0x3D;
	private const int MicLedOffset = 8;
	private const int PowerSaveOffset = 9;
	private const int MotorPowerReductionOffset = 36;

	// Power save control bit that mutes the microphone.
	private const byte MicMutePowerSave = 0x10;
	private const int RightTriggerOffset = 10;
	private const int LeftTriggerOffset = 21;
	private const int Flag2 = 38;
	private const int LightbarSetup = 41;
	private const int LedBrightnessOffset = 42;
	private const int PlayerLedsOffset = 43;
	private const int Red = 44;

	private const byte FadeOutBootLightValue = 0x02;

	// Player LEDs change instantly instead of fading in.
	private const byte PlayerLedsInstant = 0x20;

	// triggers: whether DualPulse's own trigger effects are applied (flag0 0x04/0x08). The session sends them only when
	// they change, so a game driving the triggers natively isn't overridden by every lightbar or LED update.
	// resetPower: also apply power save off and no motor power reduction (after a game that may have set them lets go).
	// motors: whether the motors (and the classic-rumble selection that goes with them) are applied. Selecting classic
	// rumble switches a DualSense's haptics off until it is power-cycled, so the session sends it only while the motors
	// run, and once more to stop them.
	public static int Build(OutputState state, Transport transport, byte sequence, BluetoothLayout layout, Span<byte> destination, bool triggers = true,
		bool resetPower = false, bool motors = true)
	{
		int length = transport == Transport.Usb ? UsbLength : BluetoothLength;
		if (destination.Length < length)
		{
			throw new ArgumentException($"Destination must be at least {length} bytes.", nameof(destination));
		}
		Span<byte> report = destination[..length];
		report.Clear();
		int commonOffset;
		if (transport == Transport.Usb)
		{
			report[0] = UsbReportId;
			commonOffset = 1;
		}
		else if (layout == BluetoothLayout.Sequenced)
		{
			report[0] = BluetoothReportId;
			report[1] = (byte)((sequence & 0x0F) << 4);
			report[2] = 0x10;
			commonOffset = 3;
		}
		else
		{
			report[0] = BluetoothReportId;
			report[1] = 0x02;
			commonOffset = 2;
		}
		WriteCommon(state, transport, report[commonOffset..], triggers, resetPower, motors);
		if (transport == Transport.Bluetooth)
		{
			Span<byte> crcInput = stackalloc byte[BluetoothLength - 4 + 1];
			crcInput[0] = BluetoothCrcSeed;
			report[..(BluetoothLength - 4)].CopyTo(crcInput[1..]);
			BinaryPrimitives.WriteUInt32LittleEndian(report[(BluetoothLength - 4)..], Crc32.HashToUInt32(crcInput));
		}
		return length;
	}

	// A game's haptics are audio (USB channels 3-4; over Bluetooth DualPulse's audio reports), so the actuators must
	// stay in audio-haptics mode: the rumble bits go (they would switch the actuators to motor emulation, and a game may
	// send one only at start), and the haptics setup DualPulse uses itself goes in (power save off, keeping only the
	// game's mic mute; low-pass filter; motor power reduction 0 unless the game sets it). Over Bluetooth the speaker
	// volume is also mapped onto its audible range (0x3D-0x64; 0 stays mute).
	private static void AdaptGameBlock(Span<byte> common, bool bluetooth)
	{
		bool gamePowerSave = (common[Flag1] & PowerSaveControl) != 0;
		bool gameMotorPower = (common[Flag1] & MotorPowerControl) != 0;
		common[Flag0] &= unchecked((byte)~(CompatibleVibration | HapticsSelect));
		common[Flag2] &= unchecked((byte)~CompatibleVibration2);
		common[MotorRight] = 0;
		common[MotorLeft] = 0;
		common[Flag1] |= PowerSaveControl | HapticsLowPassFilter | MotorPowerControl;
		common[PowerSaveOffset] = gamePowerSave ? (byte)(common[PowerSaveOffset] & MicMutePowerSave) : (byte)0;
		if (!gameMotorPower)
		{
			common[MotorPowerReductionOffset] = 0;
		}
		if (bluetooth && (common[Flag0] & SpeakerVolumeControl) != 0)
		{
			int volume = Math.Min((int)common[SpeakerVolumeOffset], 0x64);
			common[SpeakerVolumeOffset] = volume == 0 ? (byte)0 : (byte)(SpeakerVolumeQuiet + (0x64 - SpeakerVolumeQuiet) * (volume - 1) / 0x63);
		}
	}

	private static void WriteCommon(OutputState state, Transport transport, Span<byte> common, bool triggers, bool resetPower, bool motors)
	{
		// A game driving a virtual DualSense: its block goes out adapted (see AdaptGameBlock), its haptics being audio on
		// either transport. Its motor rumble goes out only while the rumble service runs the motors (its scaled values;
		// the motor bits switch the actuators out of audio haptics until the next block). Over Bluetooth with haptics on,
		// that rumble plays through the haptics stream instead.
		if (state.GameBlock is { Length: >= OutputState.CommonBlockLength } block)
		{
			block.AsSpan(0, OutputState.CommonBlockLength).CopyTo(common);
			byte motorFlags = (byte)(common[Flag0] & (CompatibleVibration | HapticsSelect));
			byte motorFlag2 = (byte)(common[Flag2] & CompatibleVibration2);
			AdaptGameBlock(common, transport == Transport.Bluetooth);
			bool motorsRun = state.LeftMotor != 0 || state.RightMotor != 0;
			if (motorFlags != 0 && motorsRun && !(transport == Transport.Bluetooth && state.HapticsMode))
			{
				common[Flag0] |= motorFlags;
				common[Flag2] |= motorFlag2;
				common[MotorRight] = state.RightMotor;
				common[MotorLeft] = state.LeftMotor;
			}
			// A trigger the game never set is DualPulse's (something that sets only the lights mustn't freeze it).
			if (triggers && (common[Flag0] & RightTriggerEffect) == 0)
			{
				common[Flag0] |= RightTriggerEffect;
				state.RightTrigger.Bytes.CopyTo(common[RightTriggerOffset..]);
			}
			if (triggers && (common[Flag0] & LeftTriggerEffect) == 0)
			{
				common[Flag0] |= LeftTriggerEffect;
				state.LeftTrigger.Bytes.CopyTo(common[LeftTriggerOffset..]);
			}
			// Lights the game never set are DualPulse's too.
			if ((common[Flag1] & LightbarControl) == 0)
			{
				common[Flag1] |= LightbarControl;
				common[Red] = state.Lightbar.R;
				common[Red + 1] = state.Lightbar.G;
				common[Red + 2] = state.Lightbar.B;
			}
			if ((common[Flag1] & PlayerLedControl) == 0)
			{
				common[Flag1] |= PlayerLedControl;
				common[PlayerLedsOffset] = (byte)((state.PlayerLeds & 0x1F) | PlayerLedsInstant);
			}
			if ((common[Flag2] & LedBrightnessControl) == 0)
			{
				common[Flag2] |= LedBrightnessControl;
				common[LedBrightnessOffset] = (byte)state.PlayerLedBrightness;
			}
			if ((common[Flag1] & MicLedControl) == 0)
			{
				common[Flag1] |= MicLedControl;
				common[MicLedOffset] = (byte)state.MicLed;
			}
			return;
		}
		if (state.HapticsMode)
		{
			// Rumble bits would take the actuators out of audio mode; power save and power reduction stay at 0 (all on).
			common[Flag0] = RightTriggerEffect | LeftTriggerEffect;
			common[Flag1] = MicLedControl | LightbarControl | PlayerLedControl | PowerSaveControl | HapticsLowPassFilter | MotorPowerControl;
			common[Flag2] = LedBrightnessControl | LightbarSetupControl;
			common[PowerSaveOffset] = 0;
			common[MotorPowerReductionOffset] = 0;
		}
		else
		{
			common[Flag0] = RightTriggerEffect | LeftTriggerEffect;
			common[Flag1] = MicLedControl | LightbarControl | PlayerLedControl;
			common[Flag2] = LedBrightnessControl | LightbarSetupControl;
			if (motors)
			{
				common[Flag0] |= (byte)(HapticsSelect | (state.ImprovedRumble ? 0 : CompatibleVibration));
				common[Flag2] |= state.ImprovedRumble ? CompatibleVibration2 : (byte)0;
				common[MotorRight] = state.RightMotor;
				common[MotorLeft] = state.LeftMotor;
			}
			if (resetPower)
			{
				common[Flag1] |= PowerSaveControl | MotorPowerControl;
				common[PowerSaveOffset] = 0;
				common[MotorPowerReductionOffset] = 0;
			}
		}
		common[MicLedOffset] = (byte)state.MicLed;
		// Bluetooth audio: audio control selects the path (0x30 speaker, 0x00 headset). Speaker volume bytes below about
		// 0x3D are silent, so 1-100 % maps onto 0x3D-0x64 (0 stays mute); headphones scale up to 0x7F.
		int volume = Math.Min((int)state.AudioVolume, 100);
		if (state.AudioOutput == AudioOutput.Speaker)
		{
			common[Flag0] |= AudioControl | SpeakerVolumeControl;
			common[SpeakerVolumeOffset] = volume == 0 ? (byte)0 : (byte)(SpeakerVolumeQuiet + (0x64 - SpeakerVolumeQuiet) * (volume - 1) / 99);
			common[AudioControlOffset] = 0x30;
		}
		else if (state.AudioOutput == AudioOutput.Headset)
		{
			common[Flag0] |= AudioControl | HeadphoneVolumeControl;
			common[HeadphoneVolumeOffset] = (byte)(volume * 0x7F / 100);
			common[AudioControlOffset] = 0x00;
		}
		if (!triggers)
		{
			common[Flag0] &= unchecked((byte)~(RightTriggerEffect | LeftTriggerEffect));
		}
		state.RightTrigger.Bytes.CopyTo(common[RightTriggerOffset..]);
		state.LeftTrigger.Bytes.CopyTo(common[LeftTriggerOffset..]);
		common[LightbarSetup] = state.FadeOutBootLight ? FadeOutBootLightValue : (byte)0;
		common[LedBrightnessOffset] = (byte)state.PlayerLedBrightness;
		common[PlayerLedsOffset] = (byte)((state.PlayerLeds & 0x1F) | PlayerLedsInstant);
		common[Red] = state.Lightbar.R;
		common[Red + 1] = state.Lightbar.G;
		common[Red + 2] = state.Lightbar.B;
	}
}
