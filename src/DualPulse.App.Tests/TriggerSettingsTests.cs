using DualPulse.App.Services;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class TriggerSettingsTests
{
	private static bool Same(TriggerEffect a, TriggerEffect b) => a.Bytes.SequenceEqual(b.Bytes);

	[Fact]
	public void Presets_ProduceTheExistingEffects()
	{
		Assert.True(Same(TriggerEffect.Off, TriggerSettings.FromPreset(TriggerPreset.Off).Left.ToEffect()));
		Assert.True(Same(TriggerEffects.Feedback(3, 6), TriggerSettings.FromPreset(TriggerPreset.Feedback).Left.ToEffect()));
		Assert.True(Same(TriggerEffects.Weapon(2, 6, 8), TriggerSettings.FromPreset(TriggerPreset.Weapon).Left.ToEffect()));
		Assert.True(Same(TriggerEffects.Vibration(2, 6, 30), TriggerSettings.FromPreset(TriggerPreset.Vibration).Left.ToEffect()));
		Assert.True(TriggerSettings.FromPreset(TriggerPreset.Weapon).Linked);
	}

	[Fact]
	public void MatchPreset_IgnoresTheRange()
	{
		TriggerSettings weapon = TriggerSettings.FromPreset(TriggerPreset.Weapon);
		TriggerEffectSettings withRange = weapon.Left with { Deadzone = 0.1, StartAt = 0.2 };

		Assert.Equal(TriggerPreset.Weapon, (weapon with { Left = withRange }).MatchPreset());
	}

	[Fact]
	public void ToEffect_ClampsToValidRanges()
	{
		// Weapon needs start 2-7 and an end after the start (up to 8); strength 1-8.
		TriggerEffectSettings weapon = new(TriggerKind.Weapon, Start: 0, End: 0, Strength: 99);
		Assert.True(Same(TriggerEffects.Weapon(2, 3, 8), weapon.ToEffect()));
		Assert.True(Same(TriggerEffects.Weapon(7, 8, 1), new TriggerEffectSettings(TriggerKind.Weapon, Start: 9, End: 9, Strength: 0).ToEffect()));
		Assert.True(Same(TriggerEffects.Feedback(9, 1), new TriggerEffectSettings(TriggerKind.Resistance, Start: 12, Strength: 0).ToEffect()));
		Assert.True(Same(TriggerEffects.Vibration(0, 8, 1), new TriggerEffectSettings(TriggerKind.Vibration, Start: -3, Strength: 20, Frequency: 0).ToEffect()));
	}

	[Fact]
	public void EffectiveTriggers_FollowPresetUntilCustomised()
	{
		AppSettings legacy = new(TriggerPreset: TriggerPreset.Vibration);
		Assert.Equal(TriggerSettings.FromPreset(TriggerPreset.Vibration), legacy.EffectiveTriggers);

		TriggerSettings custom = new(Linked: false, Left: new TriggerEffectSettings(TriggerKind.Resistance), Right: new TriggerEffectSettings(TriggerKind.Weapon));
		AppSettings customised = legacy with { TriggerPreset = TriggerPreset.Custom, Triggers = custom };
		Assert.Equal(custom, customised.EffectiveTriggers);
	}

	[Fact]
	public void MatchPreset_RecognisesPresetsOnly()
	{
		Assert.Equal(TriggerPreset.Weapon, TriggerSettings.FromPreset(TriggerPreset.Weapon).MatchPreset());
		Assert.Equal(TriggerPreset.Off, new TriggerSettings().MatchPreset());
		TriggerSettings tweaked = TriggerSettings.FromPreset(TriggerPreset.Weapon) with { Left = new TriggerEffectSettings(TriggerKind.Weapon, Start: 3, End: 6, Strength: 8) };
		Assert.Equal(TriggerPreset.Custom, tweaked.MatchPreset());
	}
}
