using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class GameOutputTests
{
	private static byte[] Block(byte flag0 = 0, byte flag1 = 0, byte flag2 = 0)
	{
		byte[] block = new byte[OutputState.CommonBlockLength];
		block[0] = flag0;
		block[1] = flag1;
		block[38] = flag2;
		return block;
	}

	[Fact]
	public void Accept_SendsTheGamesFlags_WithTheFieldsItDidntSetFilledIn()
	{
		// The game sets the audio route only in blocks flagged 0x80; its other blocks carry 0 there, which reads as
		// "headset". Unflagged fields come from what the game set before.
		GameOutput game = new();
		byte[] route = Block(flag0: 0x80);
		route[7] = 0x3C;
		game.Accept(route);
		byte[] triggers = Block(flag0: 0x0C);
		triggers[11] = 0x26;

		byte[] sent = game.Accept(triggers)!;

		Assert.Equal(0x0C, sent[0]);
		Assert.Equal(0x26, sent[11]);
		Assert.Equal(0x3C, sent[7]);
	}

	[Fact]
	public void Accept_KeepsFlaggingATriggerTheGameSet()
	{
		// A trigger the game set stays its own in later blocks; one it never set has no flag, and DualPulse's goes there.
		GameOutput game = new();
		byte[] right = Block(flag0: 0x04);
		right[10] = 0x26;
		game.Accept(right);

		byte[] sent = game.Accept(Block(flag1: 0x04))!;

		Assert.Equal(0x04, sent[0] & 0x0C);
		Assert.Equal(0x26, sent[10]);
	}

	[Fact]
	public void Accept_DefaultsTheAudioRouteToTheSpeaker()
	{
		// Byte 7 matters to DualPulse itself: it picks the speaker or headset path for Bluetooth audio.
		byte[] sent = new GameOutput().Accept(Block(flag0: 0x0C))!;

		Assert.False((OutputState.Default with { GameBlock = sent }).UsesHeadset);
	}

	[Fact]
	public void Carried_KeepsTheSecondAudioControlByte()
	{
		// Flag1 0x80 makes byte 37 valid; carrying the flag with a zero byte would reset the game's setting.
		GameOutput game = new();
		byte[] block = Block(flag1: 0x80);
		block[37] = 0x44;
		game.Accept(block);

		Assert.Equal(0x44, game.Carried![37]);
	}

	[Fact]
	public void Accept_RepeatedBlocks_AreNotSentAgain()
	{
		// Games re-send their output every frame.
		GameOutput game = new();
		byte[] block = Block(flag0: 0x04);
		block[11] = 0x21;

		Assert.NotNull(game.Accept(block));
		Assert.Null(game.Accept([.. block]));
		block[11] = 0x22;
		Assert.NotNull(game.Accept(block));
	}

	[Fact]
	public void Carried_HoldsEverythingTheGameSet_WithAllItsFlags()
	{
		// A new connection gets the game's whole state: triggers set earlier survive later lights-only blocks.
		GameOutput game = new();
		Assert.Null(game.Carried);
		byte[] triggers = Block(flag0: 0x0C);
		triggers[11] = 0x26;
		triggers[22] = 0x21;
		game.Accept(triggers);
		byte[] lights = Block(flag1: 0x04);
		lights[44] = 0xFF;
		game.Accept(lights);

		byte[] carried = game.Carried!;

		Assert.Equal(0x0C, carried[0]);
		Assert.Equal(0x04, carried[1]);
		Assert.Equal(0x26, carried[11]);
		Assert.Equal(0x21, carried[22]);
		Assert.Equal(0xFF, carried[44]);
	}

	[Fact]
	public void Carried_LeavesTheMotorBitsOut()
	{
		// Carried to a new connection, a motor bit the game sent once would switch its haptics off for good.
		GameOutput game = new();
		game.Accept(Block(flag0: 0x0D, flag2: 0x04));

		byte[] carried = game.Carried!;

		Assert.Equal(0x0C, carried[0]);
		Assert.Equal(0, carried[38] & 0x04);
	}

	[Fact]
	public void Motors_AreReportedWhenTheGameSetsThem()
	{
		GameOutput game = new();
		Assert.Null(game.Motors);
		byte[] rumble = Block(flag0: 0x03);
		rumble[2] = 40;
		rumble[3] = 200;

		game.Accept(rumble);

		Assert.Equal(((byte)200, (byte)40), game.Motors);
	}
}
