using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

public enum TriggerKind
{
	Off,
	Resistance,
	Weapon,
	Vibration,
	// Resistance growing (or easing) from Start to End (Sony's slope feedback).
	Ramp,
	// A resistance strength per zone (Zones), drawn by the user.
	Zones,
	// A vibration amplitude per zone (Zones) at Frequency.
	ZoneVibration
}

// One trigger's effect. Zones are the 10 steps of trigger travel (0 = top). Values are clamped to what the effect
// accepts when it is built, so a hand-edited settings file can't produce an invalid report. Deadzone and StartAt
// shape the analog value games see (TriggerRange), separate from the resistance effect.
// Ramp uses Strength at Start and EndStrength at End; Zones and ZoneVibration use the 10 Zones values (0-8).
public sealed record TriggerEffectSettings(TriggerKind Kind = TriggerKind.Off, int Start = 3, int End = 6, int Strength = 6, int Frequency = 30,
	double Deadzone = 0, double StartAt = 0, int EndStrength = 8, IReadOnlyList<int>? Zones = null)
{
	// What a zone effect starts with before anything is drawn: light at the top, firm towards the end.
	private static readonly int[] DefaultZones = [0, 0, 1, 2, 3, 4, 6, 8, 8, 8];

	// Always 10 values, each 0-8.
	public IReadOnlyList<int> EffectiveZones => [.. Enumerable.Range(0, 10).Select(zone =>
		Math.Clamp(Zones is { } zones && zone < zones.Count ? zones[zone] : DefaultZones[zone], 0, 8))];

	public TriggerEffect ToEffect()
	{
		byte strength = (byte)Math.Clamp(Strength, 1, 8);
		switch (Kind)
		{
		case TriggerKind.Resistance:
			return TriggerEffects.Feedback((byte)Math.Clamp(Start, 0, 9), strength);
		case TriggerKind.Weapon:
			byte start = (byte)Math.Clamp(Start, 2, 7);
			return TriggerEffects.Weapon(start, (byte)Math.Clamp(End, start + 1, 8), strength);
		case TriggerKind.Vibration:
			return TriggerEffects.Vibration((byte)Math.Clamp(Start, 0, 9), strength, (byte)Math.Clamp(Frequency, 1, 255));
		case TriggerKind.Ramp:
			byte from = (byte)Math.Clamp(Start, 0, 8);
			return TriggerEffects.SlopeFeedback(from, (byte)Math.Clamp(End, from + 1, 9), strength, (byte)Math.Clamp(EndStrength, 1, 8));
		case TriggerKind.Zones:
			return TriggerEffects.MultiplePositionFeedback([.. EffectiveZones.Select(zone => (byte)zone)]);
		case TriggerKind.ZoneVibration:
			return TriggerEffects.MultiplePositionVibration((byte)Math.Clamp(Frequency, 1, 255), [.. EffectiveZones.Select(zone => (byte)zone)]);
		default:
			return TriggerEffect.Off;
		}
	}
}

// Both triggers. When Linked, the left settings apply to both and Right is ignored.
public sealed record TriggerSettings(bool Linked = true, TriggerEffectSettings? Left = null, TriggerEffectSettings? Right = null)
{
	public TriggerEffectSettings Left { get; init; } = Left ?? new TriggerEffectSettings();

	public TriggerEffectSettings Right { get; init; } = Right ?? new TriggerEffectSettings();

	public TriggerEffectSettings EffectiveRight => Linked ? Left : Right;

	public static TriggerSettings FromPreset(TriggerPreset preset)
	{
		TriggerEffectSettings effect = PresetEffect(preset);
		return new TriggerSettings(Linked: true, Left: effect, Right: effect);
	}

	// The preset these settings equal, or Custom. The range (deadzone, start point), and what only Ramp and the zone
	// effects use, are not part of a preset.
	public TriggerPreset MatchPreset()
	{
		if (Linked)
		{
			foreach (TriggerPreset preset in new[] { TriggerPreset.Off, TriggerPreset.Feedback, TriggerPreset.Weapon, TriggerPreset.Vibration })
			{
				if (Left with { Deadzone = 0, StartAt = 0, EndStrength = 8, Zones = null } == PresetEffect(preset))
				{
					return preset;
				}
			}
		}
		return TriggerPreset.Custom;
	}

	private static TriggerEffectSettings PresetEffect(TriggerPreset preset)
	{
		return preset switch
		{
			TriggerPreset.Feedback => new TriggerEffectSettings(TriggerKind.Resistance, Start: 3, Strength: 6),
			TriggerPreset.Weapon => new TriggerEffectSettings(TriggerKind.Weapon, Start: 2, End: 6, Strength: 8),
			TriggerPreset.Vibration => new TriggerEffectSettings(TriggerKind.Vibration, Start: 2, Strength: 6, Frequency: 30),
			_ => new TriggerEffectSettings()
		};
	}
}
