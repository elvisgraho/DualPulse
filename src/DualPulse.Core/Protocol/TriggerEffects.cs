namespace DualPulse.Core.Protocol;

// Adaptive trigger effect encodings, implemented from Nielk1's documentation of the DualSense trigger effects
// (https://gist.github.com/Nielk1/6d54cc2c00d2201ccb8c2720ad7538db). The trigger travel is split into 10 zones.
public static class TriggerEffects
{
	private const byte OffCode = 0x05;

	private const byte FeedbackCode = 0x21;

	private const byte BowCode = 0x22;

	private const byte GallopingCode = 0x23;

	private const byte WeaponCode = 0x25;

	private const byte MachineCode = 0x27;

	private const byte VibrationCode = 0x26;

	public static TriggerEffect Off()
	{
		return TriggerEffect.Off;
	}

	// Resistance from startZone to the end of travel. strength 0 turns the effect off.
	public static TriggerEffect Feedback(byte startZone, byte strength)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan(startZone, (byte)9);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(strength, (byte)8);
		return strength == 0 ? Off() : PerZone(FeedbackCode, startZone, strength, frequency: 0);
	}

	// A "trigger break": resistance between startZone and endZone that gives way. strength 0 turns it off.
	public static TriggerEffect Weapon(byte startZone, byte endZone, byte strength)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(startZone, (byte)2);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(startZone, (byte)7);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(endZone, (byte)8);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endZone, startZone);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(strength, (byte)8);
		if (strength == 0)
		{
			return Off();
		}
		Span<byte> bytes = stackalloc byte[TriggerEffect.Length];
		ushort zones = (ushort)((1 << startZone) | (1 << endZone));
		bytes[0] = WeaponCode;
		bytes[1] = (byte)(zones & 0xFF);
		bytes[2] = (byte)(zones >> 8);
		bytes[3] = (byte)(strength - 1);
		return new TriggerEffect(bytes);
	}

	// Vibration from startZone to the end of travel. amplitude or frequency 0 turns the effect off.
	public static TriggerEffect Vibration(byte startZone, byte amplitude, byte frequencyHz)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan(startZone, (byte)9);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(amplitude, (byte)8);
		return amplitude == 0 || frequencyHz == 0 ? Off() : PerZone(VibrationCode, startZone, amplitude, frequencyHz);
	}

	// Resistance that snaps back after endZone, like drawing a bow. strength and snapForce are 1-8.
	public static TriggerEffect Bow(byte startZone, byte endZone, byte strength, byte snapForce)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan(startZone, (byte)8);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(endZone, (byte)8);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endZone, startZone);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(strength, (byte)8);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(snapForce, (byte)8);
		if (strength == 0 || snapForce == 0)
		{
			return Off();
		}
		return Pattern(BowCode, startZone, endZone, (byte)(((strength - 1) & 0x07) | (((snapForce - 1) & 0x07) << 3)));
	}

	// A galloping rhythm between the zones: two "feet" per cycle at frequencyHz. secondFoot comes after firstFoot (0-7).
	public static TriggerEffect Galloping(byte startZone, byte endZone, byte firstFoot, byte secondFoot, byte frequencyHz)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan(startZone, (byte)8);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(endZone, (byte)9);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endZone, startZone);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(firstFoot, (byte)6);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(secondFoot, (byte)7);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(secondFoot, firstFoot);
		if (frequencyHz == 0)
		{
			return Off();
		}
		return Pattern(GallopingCode, startZone, endZone, (byte)((secondFoot & 0x07) | ((firstFoot & 0x07) << 3)), frequencyHz);
	}

	// Vibration alternating between two amplitudes (0-7) between the zones; period is in tenths of a second.
	public static TriggerEffect Machine(byte startZone, byte endZone, byte amplitudeA, byte amplitudeB, byte frequencyHz, byte period)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan(startZone, (byte)8);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(endZone, (byte)9);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endZone, startZone);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(amplitudeA, (byte)7);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(amplitudeB, (byte)7);
		if (frequencyHz == 0)
		{
			return Off();
		}
		return Pattern(MachineCode, startZone, endZone, (byte)((amplitudeA & 0x07) | ((amplitudeB & 0x07) << 3)), frequencyHz, period);
	}

	// A resistance strength (0-8, 0 = none) for each of the 10 zones.
	public static TriggerEffect MultiplePositionFeedback(ReadOnlySpan<byte> strengths)
	{
		return Zoned(FeedbackCode, strengths, frequency: 0);
	}

	// A vibration amplitude (0-8, 0 = none) for each of the 10 zones, at frequencyHz.
	public static TriggerEffect MultiplePositionVibration(byte frequencyHz, ReadOnlySpan<byte> amplitudes)
	{
		return frequencyHz == 0 ? Off() : Zoned(VibrationCode, amplitudes, frequencyHz);
	}

	// Resistance changing linearly from startStrength to endStrength between the zones, then held to the end.
	public static TriggerEffect SlopeFeedback(byte startZone, byte endZone, byte startStrength, byte endStrength)
	{
		ArgumentOutOfRangeException.ThrowIfGreaterThan(startZone, (byte)8);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(endZone, (byte)9);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(endZone, startZone);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(startStrength, (byte)8);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(endStrength, (byte)8);
		Span<byte> strengths = stackalloc byte[10];
		double slope = (endStrength - startStrength) / (double)(endZone - startZone);
		for (int zone = startZone; zone < 10; zone++)
		{
			strengths[zone] = zone <= endZone ? (byte)Math.Round(startStrength + slope * (zone - startZone), MidpointRounding.AwayFromZero) : endStrength;
		}
		return MultiplePositionFeedback(strengths);
	}

	// Bytes sent as they are (up to 11; the rest are zero). For effects defined only by their raw values, such as the
	// legacy presets mods still send.
	public static TriggerEffect Raw(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length > TriggerEffect.Length)
		{
			throw new ArgumentException($"A trigger effect is at most {TriggerEffect.Length} bytes.", nameof(bytes));
		}
		Span<byte> padded = stackalloc byte[TriggerEffect.Length];
		bytes.CopyTo(padded);
		return new TriggerEffect(padded);
	}

	// Effects with a start/end zone pair: both zones as bits, then the effect's own bytes.
	private static TriggerEffect Pattern(byte code, byte startZone, byte endZone, params ReadOnlySpan<byte> values)
	{
		Span<byte> bytes = stackalloc byte[TriggerEffect.Length];
		ushort zones = (ushort)((1 << startZone) | (1 << endZone));
		bytes[0] = code;
		bytes[1] = (byte)(zones & 0xFF);
		bytes[2] = (byte)(zones >> 8);
		values.CopyTo(bytes[3..]);
		return new TriggerEffect(bytes);
	}

	private static TriggerEffect Zoned(byte code, ReadOnlySpan<byte> values, byte frequency)
	{
		if (values.Length != 10)
		{
			throw new ArgumentException("One value per zone (10) is needed.", nameof(values));
		}
		uint zoneValues = 0;
		ushort activeZones = 0;
		for (int zone = 0; zone < 10; zone++)
		{
			ArgumentOutOfRangeException.ThrowIfGreaterThan(values[zone], (byte)8);
			if (values[zone] > 0)
			{
				zoneValues |= (uint)((values[zone] - 1) & 0x07) << (3 * zone);
				activeZones |= (ushort)(1 << zone);
			}
		}
		if (activeZones == 0)
		{
			return Off();
		}
		Span<byte> bytes = stackalloc byte[TriggerEffect.Length];
		bytes[0] = code;
		bytes[1] = (byte)(activeZones & 0xFF);
		bytes[2] = (byte)(activeZones >> 8);
		bytes[3] = (byte)(zoneValues & 0xFF);
		bytes[4] = (byte)((zoneValues >> 8) & 0xFF);
		bytes[5] = (byte)((zoneValues >> 16) & 0xFF);
		bytes[6] = (byte)((zoneValues >> 24) & 0xFF);
		bytes[9] = frequency;
		return new TriggerEffect(bytes);
	}

	// Shared layout of Feedback and Vibration: active-zone bitmask, 3-bit value per zone, optional frequency at 9.
	private static TriggerEffect PerZone(byte code, byte startZone, byte value, byte frequency)
	{
		uint zoneValues = 0;
		ushort activeZones = 0;
		uint zoneValue = (uint)((value - 1) & 0x07);
		for (int zone = startZone; zone < 10; zone++)
		{
			zoneValues |= zoneValue << (3 * zone);
			activeZones |= (ushort)(1 << zone);
		}
		Span<byte> bytes = stackalloc byte[TriggerEffect.Length];
		bytes[0] = code;
		bytes[1] = (byte)(activeZones & 0xFF);
		bytes[2] = (byte)(activeZones >> 8);
		bytes[3] = (byte)(zoneValues & 0xFF);
		bytes[4] = (byte)((zoneValues >> 8) & 0xFF);
		bytes[5] = (byte)((zoneValues >> 16) & 0xFF);
		bytes[6] = (byte)((zoneValues >> 24) & 0xFF);
		bytes[9] = frequency;
		return new TriggerEffect(bytes);
	}
}
