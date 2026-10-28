using System.IO.Hashing;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public class OutputReportBuilderTests
{
	private static byte[] Build(OutputState state, Transport transport, byte sequence = 0, BluetoothLayout layout = BluetoothLayout.Sequenced, bool triggers = true)
	{
		byte[] buffer = new byte[OutputReportBuilder.BluetoothLength];
		int length = OutputReportBuilder.Build(state, transport, sequence, layout, buffer, triggers);
		return buffer[..length];
	}

	[Theory]
	[InlineData(true, 0x10)]
	[InlineData(false, 0x00)]
	public void MicMuted_SetsOrClearsTheControllersOwnMicMute(bool muted, int powerSave)
	{
		// Windows' mute on the controller's microphone doesn't silence what programs record; the controller's own does.
		byte[] r = Build(OutputState.Default with { MicMuted = muted }, Transport.Usb);

		Assert.Equal(0x02, r[1 + 1] & 0x02);
		Assert.Equal(powerSave, r[1 + 9]);
	}

	[Fact]
	public void MicMuted_AlsoWithHapticsAndWithAGame()
	{
		byte[] haptics = Build(OutputState.Default with { MicMuted = true, HapticsMode = true }, Transport.Usb);
		byte[] game = Build(OutputState.Default with { MicMuted = true, GameBlock = GameBlock() }, Transport.Usb);

		Assert.Equal(0x10, haptics[1 + 9] & 0x10);
		Assert.Equal(0x02, game[1 + 1] & 0x02);
		Assert.Equal(0x10, game[1 + 9] & 0x10);
	}

	[Fact]
	public void Usb_Default_HasIdAndFlags()
	{
		byte[] r = Build(OutputState.Default, Transport.Usb);

		Assert.Equal(OutputReportBuilder.UsbLength, r.Length);
		Assert.Equal(0x02, r[0]);
		Assert.Equal(0x0F, r[1]);
		Assert.Equal(0x15, r[2]);
		Assert.Equal(0x03, r[39]);
		Assert.Equal(0x02, r[42]);
		Assert.Equal(0x24, r[44]);
	}

	[Fact]
	public void Usb_LightbarAndMotors_AtCommonOffsets()
	{
		OutputState state = OutputState.Default with { Lightbar = new Rgb(255, 0, 128), LeftMotor = 200, RightMotor = 50 };

		byte[] r = Build(state, Transport.Usb);

		Assert.Equal(new byte[] { 0xFF, 0x00, 0x80 }, r[45..48]);
		Assert.Equal(50, r[3]);
		Assert.Equal(200, r[4]);
	}

	[Fact]
	public void WithoutMotors_TheRumbleSelectionStaysOut()
	{
		// Selecting classic rumble switches a DualSense's haptics off until it is power-cycled, so it goes out only while
		// the motors run (the session decides).
		byte[] buffer = new byte[OutputReportBuilder.BluetoothLength];
		OutputReportBuilder.Build(OutputState.Default with { ImprovedRumble = true }, Transport.Usb, 0, BluetoothLayout.Sequenced, buffer, motors: false);

		Assert.Equal(0, buffer[1] & 0x03);
		Assert.Equal(0, buffer[39] & 0x04);
		Assert.Equal(0x0C, buffer[1] & 0x0C);
	}

	[Fact]
	public void Usb_ImprovedRumble_UsesFlag2Bit2()
	{
		byte[] r = Build(OutputState.Default with { ImprovedRumble = true }, Transport.Usb);

		Assert.Equal(0x0E, r[1]);
		Assert.Equal(0x07, r[39]);
	}

	[Fact]
	public void Bt_Sequenced_HeaderAndOffsets()
	{
		OutputState state = OutputState.Default with
		{
			Lightbar = new Rgb(1, 2, 3),
			RightTrigger = TriggerEffects.Feedback(0, 8),
			LeftTrigger = TriggerEffects.Weapon(2, 8, 8)
		};

		byte[] r = Build(state, Transport.Bluetooth, sequence: 5);

		Assert.Equal(OutputReportBuilder.BluetoothLength, r.Length);
		Assert.Equal(0x31, r[0]);
		Assert.Equal(0x50, r[1]);
		Assert.Equal(0x10, r[2]);
		Assert.Equal(0x0F, r[3]);
		Assert.Equal(new byte[] { 1, 2, 3 }, r[47..50]);
		Assert.Equal(TriggerEffects.Feedback(0, 8).Bytes.ToArray(), r[13..24]);
		Assert.Equal(TriggerEffects.Weapon(2, 8, 8).Bytes.ToArray(), r[24..35]);
	}

	[Fact]
	public void Bt_Legacy_HeaderAndOffsets()
	{
		byte[] r = Build(OutputState.Default with { Lightbar = new Rgb(1, 2, 3) }, Transport.Bluetooth, layout: BluetoothLayout.Legacy);

		Assert.Equal(0x31, r[0]);
		Assert.Equal(0x02, r[1]);
		Assert.Equal(0x0F, r[2]);
		Assert.Equal(new byte[] { 1, 2, 3 }, r[46..49]);
	}

	[Theory]
	[InlineData(BluetoothLayout.Sequenced)]
	[InlineData(BluetoothLayout.Legacy)]
	public void Bt_Crc_MatchesStandardCrc32WithA2Prefix(BluetoothLayout layout)
	{
		byte[] r = Build(OutputState.Default with { Lightbar = new Rgb(9, 8, 7) }, Transport.Bluetooth, 3, layout);

		byte[] withPrefix = [0xA2, .. r[..74]];
		Assert.Equal(Crc32.HashToUInt32(withPrefix), BitConverter.ToUInt32(r, 74));
	}

	[Fact]
	public void Triggers_WrittenAtBothSides()
	{
		OutputState state = OutputState.Default with { RightTrigger = TriggerEffects.Feedback(0, 8), LeftTrigger = TriggerEffects.Weapon(2, 8, 8) };

		byte[] r = Build(state, Transport.Usb);

		Assert.Equal(new byte[] { 0x21, 0xFF, 0x03 }, r[11..14]);
		Assert.Equal(new byte[] { 0x25, 0x04, 0x01, 0x07 }, r[22..26]);
	}

	[Fact]
	public void Build_DestinationTooSmall_Throws()
	{
		Assert.Throws<ArgumentException>(() => OutputReportBuilder.Build(OutputState.Default, Transport.Bluetooth, 0, BluetoothLayout.Sequenced, new byte[10]));
	}

	[Fact]
	public void Bluetooth_HapticsMode_KeepsTheActuatorsInAudioMode()
	{
		OutputState state = OutputState.Default with { HapticsMode = true, ImprovedRumble = true, LeftMotor = 200, RightMotor = 100 };

		byte[] r = Build(state, Transport.Bluetooth);

		// Sequenced Bluetooth layout: the common block starts at byte 3.
		Assert.Equal(0x0C, r[3 + 0]);
		Assert.Equal(0x02 | 0x04 | 0x10 | 0x20 | 0x40 | 0x01, r[3 + 1]);
		Assert.Equal(0, r[3 + 2]);
		Assert.Equal(0, r[3 + 3]);
		Assert.Equal(0, r[3 + 9]);
		Assert.Equal(0, r[3 + 36]);
		Assert.Equal(0x01 | 0x02, r[3 + 38]);
	}

	[Fact]
	public void Bluetooth_Speaker_EnablesAudioControl_AndSpeakerVolume()
	{
		byte[] r = Build(OutputState.Default with { HapticsMode = true, AudioOutput = AudioOutput.Speaker, AudioVolume = 100 }, Transport.Bluetooth);

		Assert.Equal(0x0C | 0x80 | 0x20, r[3 + 0]);
		Assert.Equal(0x64, r[3 + 5]);
		Assert.Equal(0x30, r[3 + 7]);
	}

	[Fact]
	public void Bluetooth_Headset_EnablesAudioControl_AndHeadphoneVolume_Scaled()
	{
		byte[] r = Build(OutputState.Default with { HapticsMode = true, AudioOutput = AudioOutput.Headset, AudioVolume = 50 }, Transport.Bluetooth);

		Assert.Equal(0x0C | 0x80 | 0x10, r[3 + 0]);
		Assert.Equal(0x7F * 50 / 100, r[3 + 4]);
		Assert.Equal(0x00, r[3 + 7]);
	}

	[Fact]
	public void AudioOff_LeavesTheAudioFieldsAlone()
	{
		byte[] r = Build(OutputState.Default with { HapticsMode = true }, Transport.Bluetooth);

		Assert.Equal(0x0C, r[3 + 0]);
		Assert.Equal(0, r[3 + 4]);
		Assert.Equal(0, r[3 + 5]);
		Assert.Equal(0, r[3 + 7]);
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(1, 0x3D)]
	[InlineData(50, 0x50)]
	[InlineData(100, 0x64)]
	public void Speaker_Volume_UsesTheAudibleRange(int volume, int expected)
	{
		// Speaker volume bytes below about 0x3D are silent; 0 stays mute.
		byte[] r = Build(OutputState.Default with { AudioOutput = AudioOutput.Speaker, AudioVolume = (byte)volume }, Transport.Bluetooth);

		Assert.InRange(r[3 + 5], expected - 1, expected + 1);
	}

	private static byte[] GameBlock()
	{
		byte[] block = new byte[47];
		for (int i = 0; i < block.Length; i++)
		{
			block[i] = (byte)(i + 1);
		}
		// Motors (0x01) and both trigger effects; power save (0x02), the mute LED, lightbar and player LEDs.
		block[0] = 0x0D;
		block[1] = 0x17;
		return block;
	}

	[Fact]
	public void GameBlock_PassesThroughAsTheCommonBlock_Usb()
	{
		byte[] r = Build(OutputState.Default with { GameBlock = GameBlock(), Lightbar = new Rgb(9, 9, 9), LeftMotor = 200 }, Transport.Usb);

		// Triggers, volumes and lights as the game sent them; the motors it flags (flag0 0x01 here) carry DualPulse's
		// (scaled) values while they run.
		Assert.Equal(GameBlock()[4..9], r[(1 + 4)..(1 + 9)]);
		Assert.Equal(GameBlock()[10..36], r[(1 + 10)..(1 + 36)]);
		Assert.Equal(GameBlock()[39..47], r[(1 + 39)..(1 + 47)]);
		Assert.Equal(0x01, r[1] & 0x01);
		Assert.Equal(200, r[1 + 3]);
	}

	[Fact]
	public void GameBlock_TriggersTheGameDidntSet_AreDualPulses()
	{
		// Something that only sets the lights (Steam, Windows) mustn't freeze the triggers at whatever was there before.
		byte[] block = new byte[OutputState.CommonBlockLength];
		block[0] = 0x04;
		block[10] = 0x26;
		block[1] = 0x04;
		TriggerEffect weapon = TriggerEffects.Weapon(2, 6, 8);

		byte[] r = Build(OutputState.Default with { GameBlock = block, LeftTrigger = weapon, RightTrigger = weapon }, Transport.Usb);

		Assert.Equal(0x0C, r[1] & 0x0C);
		Assert.Equal(0x26, r[1 + 10]);
		Assert.Equal(weapon.Bytes, r[(1 + 21)..(1 + 32)]);

		byte[] untouched = Build(OutputState.Default with { GameBlock = block, LeftTrigger = weapon }, Transport.Usb, triggers: false);
		Assert.Equal(0x04, untouched[1] & 0x0C);
	}

	[Fact]
	public void GameBlock_LightsTheGameDidntSet_AreYours()
	{
		byte[] block = new byte[OutputState.CommonBlockLength];
		block[0] = 0x0C;

		byte[] r = Build(OutputState.Default with { GameBlock = block, Lightbar = new Rgb(9, 8, 7), PlayerLeds = 0x04 }, Transport.Usb);

		Assert.Equal(0x04 | 0x10, r[1 + 1] & (0x04 | 0x10));
		Assert.Equal([9, 8, 7], r[(1 + 44)..(1 + 47)]);
		Assert.Equal(0x04, r[1 + 43] & 0x1F);
	}

	[Fact]
	public void GameBlock_Bluetooth_TriggersAndLightsPassThrough_WithAValidCrc()
	{
		byte[] r = Build(OutputState.Default with { GameBlock = GameBlock() }, Transport.Bluetooth);

		Assert.Equal(GameBlock()[10..36], r[(3 + 10)..(3 + 36)]);
		Assert.Equal(GameBlock()[39..47], r[(3 + 39)..(3 + 47)]);
		byte[] crcInput = new byte[75];
		crcInput[0] = 0xA2;
		r.AsSpan(0, 74).CopyTo(crcInput.AsSpan(1));
		Assert.Equal(System.IO.Hashing.Crc32.HashToUInt32(crcInput), System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(74)));
	}

	[Fact]
	public void UsesHeadset_FollowsTheGamesAudioControl_OrTheChosenOutput()
	{
		byte[] speaker = new byte[47];
		speaker[7] = 0x30;
		byte[] headset = new byte[47];

		Assert.False((OutputState.Default with { GameBlock = speaker }).UsesHeadset);
		Assert.True((OutputState.Default with { GameBlock = headset }).UsesHeadset);
		Assert.True((OutputState.Default with { AudioOutput = AudioOutput.Headset }).UsesHeadset);
		Assert.False((OutputState.Default with { AudioOutput = AudioOutput.Speaker }).UsesHeadset);
	}

	private static byte[] GameFlags(byte flag0, byte flag1 = 0, byte flag2 = 0)
	{
		byte[] block = new byte[47];
		block[0] = flag0;
		block[1] = flag1;
		block[38] = flag2;
		return block;
	}

	[Fact]
	public void GameBlock_Bluetooth_KeepsTheActuatorsInAudioHaptics()
	{
		// Over Bluetooth the game's haptics arrive as audio; its rumble bits would switch the actuators out of that mode.
		byte[] block = GameFlags(0x0D, flag2: 0x04);
		block[2] = 50;
		block[3] = 60;
		block[9] = 0x07;
		block[36] = 0x33;

		byte[] r = Build(OutputState.Default with { GameBlock = block, HapticsMode = true }, Transport.Bluetooth);

		Assert.Equal(0x0C, r[3]);
		// The rumble bit (0x04) is gone; the LED bits DualPulse fills in may be set.
		Assert.Equal(0x00, r[3 + 38] & 0x04);
		Assert.Equal(0x62, r[3 + 1] & 0x62);
		Assert.Equal(0, r[3 + 9]);
		Assert.Equal(0, r[3 + 36]);
	}

	[Fact]
	public void GameBlock_Bluetooth_KeepsTheGamesMotorPowerAndMicMute()
	{
		byte[] block = GameFlags(0x00, flag1: 0x42);
		block[9] = 0x1F;
		block[36] = 0x55;

		byte[] r = Build(OutputState.Default with { GameBlock = block }, Transport.Bluetooth);

		Assert.Equal(0x55, r[3 + 36]);
		Assert.Equal(0x10, r[3 + 9]);
	}

	[Theory]
	[InlineData(0x00, 0x00)]
	[InlineData(0x01, 0x3D)]
	[InlineData(0x64, 0x64)]
	[InlineData(0x7F, 0x64)]
	public void GameBlock_Bluetooth_MapsTheGamesSpeakerVolumeOntoTheAudibleRange(byte game, byte expected)
	{
		// Below about 0x3D the speaker is silent over Bluetooth.
		byte[] block = GameFlags(0x20);
		block[5] = game;

		Assert.Equal(expected, Build(OutputState.Default with { GameBlock = block }, Transport.Bluetooth)[3 + 5]);
	}

	[Fact]
	public void GameBlock_Usb_MotorsComeFromTheRumbleService()
	{
		// The game's raw motor values would skip the rumble strength and mute.
		byte[] block = GameFlags(0x03);
		block[2] = 120;
		block[3] = 200;

		byte[] r = Build(OutputState.Default with { GameBlock = block, RightMotor = 60, LeftMotor = 100 }, Transport.Usb);

		Assert.Equal(0x03, r[1] & 0x03);
		Assert.Equal(60, r[1 + 2]);
		Assert.Equal(100, r[1 + 3]);
	}

	[Fact]
	public void GameBlock_BluetoothWithoutHaptics_KeepsTheGamesMotorRumble_Scaled()
	{
		byte[] block = GameFlags(0x03);
		block[3] = 200;

		byte[] r = Build(OutputState.Default with { GameBlock = block, HapticsMode = false, LeftMotor = 100 }, Transport.Bluetooth);

		Assert.Equal(0x03, r[3] & 0x03);
		Assert.Equal(100, r[3 + 3]);
	}

	[Fact]
	public void GameBlock_Usb_KeepsTheActuatorsInAudioHaptics_WhileNoMotorRumbles()
	{
		// Over USB too, a game's haptics are audio; a motor bit (Cyberpunk sends one at start) left the actuators in motor
		// emulation, so the haptics never played.
		byte[] block = GameFlags(0x2D, flag2: 0x04);
		block[5] = 0x10;
		block[9] = 0x07;

		byte[] r = Build(OutputState.Default with { GameBlock = block }, Transport.Usb);

		Assert.Equal(0x2C, r[1]);
		Assert.Equal(0, r[1 + 38] & 0x04);
		Assert.Equal(0x62, r[1 + 1] & 0x62);
		Assert.Equal(0, r[1 + 9]);
		// The speaker volume isn't remapped over USB (that is for Bluetooth).
		Assert.Equal(0x10, r[1 + 5]);
	}

	[Fact]
	public void GameBlock_BluetoothWithoutHaptics_NoMotorRumble_StaysInAudioHaptics()
	{
		byte[] block = GameFlags(0x03);

		byte[] r = Build(OutputState.Default with { GameBlock = block, HapticsMode = false }, Transport.Bluetooth);

		Assert.Equal(0, r[3] & 0x03);
	}
}
