namespace DualPulse.Core.Protocol;

// What a game has set on a virtual DualSense, merged from its output blocks. A block's flags say which field groups
// it carries; only those replace the merged state. Over USB games send whole blocks every frame but set fields only
// in some of them, so:
// - Accept returns the block to send now: the game's own flags, with every field it didn't flag filled in from the
//   merged state (an unflagged audio route reads 0, "headset"; the speaker is the default), and the flag of every
//   trigger and light the game has set (an unflagged one is DualPulse's). Null when it is the same as the last one sent.
// - Without removes the lights or triggers from a block, for a user who keeps their own.
// - Carried is the merged state with every flag the game has used (but the motor bits), for a new connection.
// Thread-safe.
public sealed class GameOutput
{
	// Flag byte (0, 1 or 38 in the common block), bit, and the bytes that bit makes valid.
	private static readonly (int Flag, byte Bit, int Start, int Length)[] Groups =
	[
		(0, 0x01, 2, 2), // motors (compatible vibration)
		(0, 0x02, 2, 2), // motors (haptics select)
		(0, 0x04, 10, 11), // right trigger effect
		(0, 0x08, 21, 11), // left trigger effect
		(0, 0x10, 4, 1), // headphone volume
		(0, 0x20, 5, 1), // speaker volume
		(0, 0x40, 6, 1), // microphone volume
		(0, 0x80, 7, 1), // audio route
		(1, 0x01, 8, 1), // mute LED
		(1, 0x02, 9, 1), // power save
		(1, 0x04, 44, 3), // lightbar colour
		(1, 0x10, 43, 1), // player LEDs
		(1, 0x40, 36, 1), // motor power reduction
		(1, 0x80, 37, 1), // audio control 2 (speaker preamp, beamforming)
		(38, 0x01, 42, 1), // LED brightness
		(38, 0x02, 41, 1), // lightbar setup
	];

	private const int Flag2 = 38;

	private const byte SpeakerRoute = 0x30;

	// Both trigger effects (flag0).
	private const byte TriggerFlags = 0x0C;

	// Mute LED, lightbar colour and player LEDs (flag1); LED brightness and lightbar setup (flag 2).
	private const byte LightFlags1 = 0x01 | 0x04 | 0x10;

	private const byte LightFlags2 = 0x01 | 0x02;

	public static byte[] Without(byte[] block, bool lights, bool triggers)
	{
		byte[] copy = [.. block];
		if (triggers)
		{
			copy[0] &= unchecked((byte)~TriggerFlags);
		}
		if (lights && copy.Length > Flag2)
		{
			copy[1] &= unchecked((byte)~LightFlags1);
			copy[Flag2] &= unchecked((byte)~LightFlags2);
		}
		return copy;
	}

	private readonly object gate = new();

	private readonly byte[] merged = new byte[OutputState.CommonBlockLength];

	private byte flags0;

	private byte flags1;

	private byte flags2;

	private byte[]? lastSent;

	private (byte Large, byte Small)? motors;

	public GameOutput()
	{
		merged[7] = SpeakerRoute;
	}

	// The game's rumble motors (Large = left), once it has set them.
	public (byte Large, byte Small)? Motors
	{
		get
		{
			lock (gate)
			{
				return motors;
			}
		}
	}

	public byte[]? Carried
	{
		get
		{
			lock (gate)
			{
				if (lastSent == null)
				{
					return null;
				}
				// Without the motor bits: a game may send one only at start, and carried over it would switch the new
				// connection's actuators out of audio haptics for good (motor rumble comes through the rumble service).
				byte[] carried = [.. merged];
				carried[0] = (byte)(flags0 & ~0x03);
				carried[1] = flags1;
				carried[Flag2] = (byte)(flags2 & ~0x04);
				return carried;
			}
		}
	}

	public byte[]? Accept(byte[] block)
	{
		if (block.Length < OutputState.CommonBlockLength)
		{
			return null;
		}
		lock (gate)
		{
			byte[] send = block[..OutputState.CommonBlockLength];
			foreach ((int flag, byte bit, int start, int length) in Groups)
			{
				Span<byte> field = send.AsSpan(start, length);
				if ((block[flag] & bit) != 0)
				{
					field.CopyTo(merged.AsSpan(start, length));
				}
				else
				{
					merged.AsSpan(start, length).CopyTo(field);
				}
			}
			flags0 |= block[0];
			flags1 |= block[1];
			flags2 |= block[Flag2];
			send[0] |= (byte)(flags0 & TriggerFlags);
			send[1] |= (byte)(flags1 & LightFlags1);
			send[Flag2] |= (byte)(flags2 & LightFlags2);
			if ((block[0] & 0x03) != 0)
			{
				motors = (block[3], block[2]);
			}
			if (lastSent != null && send.AsSpan().SequenceEqual(lastSent))
			{
				return null;
			}
			lastSent = send;
			return send;
		}
	}
}
