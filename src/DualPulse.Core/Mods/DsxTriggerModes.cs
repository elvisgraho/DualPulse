using DualPulse.Core.Protocol;

namespace DualPulse.Core.Mods;

// The DSX mod protocol's 27 trigger modes as effects. 0-19 are the legacy v2 modes (fixed presets are their original
// raw values; parameter modes map onto the standard effects), 20-26 the v3.1 modes (Sony's standard effect set).
// Parameters are clamped into each effect's valid range so a sloppy mod still gets a sensible effect.
public static class DsxTriggerModes
{
	private static readonly string[] Names =
	[
		"Normal", "GameCube", "Very soft", "Soft", "Hard", "Very hard", "Hardest", "Rigid", "Vibrate", "Choppy", "Medium",
		"Vibrate pulse", "Custom values", "Resistance", "Bow", "Galloping", "Semi-automatic gun", "Automatic gun", "Machine",
		"Vibrate 10 Hz", "Off", "Feedback", "Weapon", "Vibration", "Slope feedback", "Multi-position feedback",
		"Multi-position vibration"
	];

	// Mode byte for each legacy "custom values" sub-mode (0-16).
	private static readonly byte[] CustomModeBytes = [0, 1, 33, 5, 37, 2, 34, 6, 38, 6, 38, 6, 38, 39, 39, 39, 39];

	public static (TriggerEffect Effect, string Description)? ToEffect(int mode, IReadOnlyList<int> args)
	{
		if (mode < 0 || mode >= Names.Length)
		{
			return null;
		}
		TriggerEffect? effect = Build(mode, args);
		if (effect is not { } built)
		{
			return null;
		}
		string description = args.Count == 0 ? Names[mode] : $"{Names[mode]} ({string.Join(", ", args)})";
		return (built, description);
	}

	private static TriggerEffect? Build(int mode, IReadOnlyList<int> a)
	{
		switch (mode)
		{
		case 0:
		case 20:
			return TriggerEffect.Off;
		case 1:
			return TriggerEffects.Raw([2, 144, 160, 255]);
		case 2:
			return TriggerEffects.Raw([2, 128, 160, 255]);
		case 3:
			return TriggerEffects.Raw([2, 69, 160, 255]);
		case 4:
			return TriggerEffects.Raw([2, 32, 160, 255]);
		case 5:
			return TriggerEffects.Raw([2, 16, 160, 255]);
		case 6:
			return TriggerEffects.Raw([2, 0, 255, 255]);
		case 7:
			return TriggerEffects.Raw([2, 0, 255, 0]);
		case 8:
			return Need(a, 1) ? LegacyVibrate(B(a[0], 0, 255)) : null;
		case 9:
			return TriggerEffects.Raw([33, 2, 39, 33, 0, 39, 38, 2]);
		case 10:
			return TriggerEffects.Raw([34, 35, 1, 6, 6, 1, 33, 1]);
		case 11:
			return TriggerEffects.Raw([39, 37, 35, 6, 39, 33, 35, 34]);
		case 12:
			return Custom(a);
		case 13:
			return Need(a, 2) ? TriggerEffects.Feedback(B(a[0], 0, 9), B(a[1], 0, 8)) : null;
		case 14:
			return Need(a, 4) ? Bow(a) : null;
		case 15:
			return Need(a, 5) ? Galloping(a) : null;
		case 16:
			return Need(a, 3) ? Weapon(a[0], a[1], B(a[2], 0, 8)) : null;
		case 17:
			return Need(a, 3) ? TriggerEffects.Vibration(B(a[0], 0, 9), B(a[1], 0, 8), B(a[2], 0, 255)) : null;
		case 18:
			return Need(a, 6) ? Machine(a) : null;
		case 19:
			return LegacyVibrate(10);
		case 21:
			return Need(a, 2) ? TriggerEffects.Feedback(B(a[0], 0, 9), B(a[1], 1, 8)) : null;
		case 22:
			return Need(a, 3) ? Weapon(a[0], a[1], B(a[2], 1, 8)) : null;
		case 23:
			return Need(a, 3) ? TriggerEffects.Vibration(B(a[0], 0, 9), B(a[1], 1, 8), B(a[2], 1, 255)) : null;
		case 24:
			return Need(a, 4) ? Slope(a) : null;
		case 25:
			return Need(a, 10) ? TriggerEffects.MultiplePositionFeedback(Zones(a, 0)) : null;
		case 26:
			return Need(a, 11) ? TriggerEffects.MultiplePositionVibration(B(a[0], 1, 255), Zones(a, 1)) : null;
		default:
			return null;
		}
	}

	private static TriggerEffect LegacyVibrate(byte frequency)
	{
		return TriggerEffects.Raw([0x26, 0, 255, 0, 0, 0, 255, frequency]);
	}

	// Sub-mode, then the seven values that follow the mode byte. Missing values are zero.
	private static TriggerEffect? Custom(IReadOnlyList<int> a)
	{
		if (!Need(a, 1) || a[0] < 0 || a[0] >= CustomModeBytes.Length)
		{
			return null;
		}
		Span<byte> bytes = stackalloc byte[8];
		bytes[0] = CustomModeBytes[a[0]];
		for (int i = 1; i < 8; i++)
		{
			bytes[i] = i < a.Count ? B(a[i], 0, 255) : (byte)0;
		}
		return TriggerEffects.Raw(bytes);
	}

	private static TriggerEffect Weapon(int start, int end, byte strength)
	{
		byte from = B(start, 2, 7);
		return TriggerEffects.Weapon(from, B(end, from + 1, 8), strength);
	}

	private static TriggerEffect Bow(IReadOnlyList<int> a)
	{
		byte from = B(a[0], 0, 7);
		return TriggerEffects.Bow(from, B(a[1], from + 1, 8), B(a[2], 0, 8), B(a[3], 0, 8));
	}

	private static TriggerEffect Galloping(IReadOnlyList<int> a)
	{
		byte from = B(a[0], 0, 8);
		byte firstFoot = B(a[2], 0, 6);
		return TriggerEffects.Galloping(from, B(a[1], from + 1, 9), firstFoot, B(a[3], firstFoot + 1, 7), B(a[4], 0, 255));
	}

	private static TriggerEffect Machine(IReadOnlyList<int> a)
	{
		byte from = B(a[0], 0, 8);
		return TriggerEffects.Machine(from, B(a[1], from + 1, 9), B(a[2], 0, 7), B(a[3], 0, 7), B(a[4], 0, 255), B(a[5], 0, 255));
	}

	private static TriggerEffect Slope(IReadOnlyList<int> a)
	{
		byte from = B(a[0], 0, 8);
		return TriggerEffects.SlopeFeedback(from, B(a[1], from + 1, 9), B(a[2], 1, 8), B(a[3], 1, 8));
	}

	private static byte[] Zones(IReadOnlyList<int> a, int offset)
	{
		byte[] zones = new byte[10];
		for (int zone = 0; zone < 10; zone++)
		{
			zones[zone] = B(a[offset + zone], 0, 8);
		}
		return zones;
	}

	private static bool Need(IReadOnlyList<int> a, int count) => a.Count >= count;

	private static byte B(int value, int min, int max) => (byte)Math.Clamp(value, min, max);
}
