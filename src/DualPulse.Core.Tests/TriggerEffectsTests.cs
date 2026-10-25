using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

// Expected bytes follow Nielk1's DualSense trigger effect documentation
// (https://gist.github.com/Nielk1/6d54cc2c00d2201ccb8c2720ad7538db).
public class TriggerEffectsTests
{
	[Fact]
	public void Off_IsEffect05()
	{
		Assert.Equal(new byte[11] { 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, TriggerEffects.Off().Bytes.ToArray());
		Assert.Equal(TriggerEffects.Off().Bytes.ToArray(), TriggerEffect.Off.Bytes.ToArray());
	}

	[Fact]
	public void Feedback_FullRangeMaxStrength()
	{
		// 0x21, active zones 0..9 (0x03FF), strength 7 (3 bits) in every zone (0x3FFFFFFF).
		Assert.Equal(new byte[11] { 0x21, 0xFF, 0x03, 0xFF, 0xFF, 0xFF, 0x3F, 0, 0, 0, 0 }, TriggerEffects.Feedback(0, 8).Bytes.ToArray());
	}

	[Fact]
	public void Feedback_ZeroStrength_IsOff()
	{
		Assert.Equal(0x05, TriggerEffects.Feedback(3, 0).Bytes[0]);
	}

	[Theory]
	[InlineData(10, 4)]
	[InlineData(0, 9)]
	public void Feedback_OutOfRange_Throws(byte start, byte strength)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffects.Feedback(start, strength));
	}

	[Fact]
	public void Weapon_StartEndStrength()
	{
		// 0x25, start/end zone bits (1<<2 | 1<<8 = 0x0104), strength - 1.
		Assert.Equal(new byte[11] { 0x25, 0x04, 0x01, 0x07, 0, 0, 0, 0, 0, 0, 0 }, TriggerEffects.Weapon(2, 8, 8).Bytes.ToArray());
	}

	[Theory]
	[InlineData(1, 8, 4)]
	[InlineData(5, 5, 4)]
	[InlineData(2, 9, 4)]
	[InlineData(2, 8, 9)]
	public void Weapon_InvalidZonesOrStrength_Throws(byte start, byte end, byte strength)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => TriggerEffects.Weapon(start, end, strength));
	}

	[Fact]
	public void Vibration_FullRangeMaxAmplitude()
	{
		// 0x26, active zones 0..9, amplitude 7 in every zone, frequency at byte 9.
		Assert.Equal(new byte[11] { 0x26, 0xFF, 0x03, 0xFF, 0xFF, 0xFF, 0x3F, 0, 0, 30, 0 }, TriggerEffects.Vibration(0, 8, 30).Bytes.ToArray());
	}

	[Fact]
	public void Vibration_ZeroAmplitudeOrFrequency_IsOff()
	{
		Assert.Equal(0x05, TriggerEffects.Vibration(2, 0, 30).Bytes[0]);
		Assert.Equal(0x05, TriggerEffects.Vibration(2, 6, 0).Bytes[0]);
	}

	private static byte[] Effect(params byte[] head)
	{
		byte[] bytes = new byte[TriggerEffect.Length];
		head.CopyTo(bytes, 0);
		return bytes;
	}

	[Fact]
	public void Bow_ZonesAndForcePair()
	{
		Assert.Equal(Effect(0x22, 0x01, 0x01, 0x3F), TriggerEffects.Bow(0, 8, 8, 8).Bytes.ToArray());
		Assert.Equal(Effect(0x22, 0x24, 0x00, 0x1A), TriggerEffects.Bow(2, 5, 3, 4).Bytes.ToArray());
	}

	[Fact]
	public void Galloping_ZonesFeetAndFrequency()
	{
		Assert.Equal(Effect(0x23, 0x01, 0x02, 0x14, 10), TriggerEffects.Galloping(0, 9, 2, 4, 10).Bytes.ToArray());
	}

	[Fact]
	public void Machine_ZonesStrengthsFrequencyPeriod()
	{
		Assert.Equal(Effect(0x27, 0x01, 0x02, 0x3F, 10, 0), TriggerEffects.Machine(0, 9, 7, 7, 10, 0).Bytes.ToArray());
	}

	[Fact]
	public void MultiplePositionFeedback_PerZoneStrength()
	{
		byte[] strengths = [8, 8, 8, 0, 0, 0, 8, 8, 0, 0];

		Assert.Equal(Effect(0x21, 0xC7, 0x00, 0xFF, 0x01, 0xFC, 0x00), TriggerEffects.MultiplePositionFeedback(strengths).Bytes.ToArray());
		Assert.Equal(Effect(0x26, 0xC7, 0x00, 0xFF, 0x01, 0xFC, 0x00, 0, 0, 10), TriggerEffects.MultiplePositionVibration(10, strengths).Bytes.ToArray());
		Assert.Equal(0x05, TriggerEffects.MultiplePositionFeedback(new byte[10]).Bytes[0]);
	}

	[Fact]
	public void SlopeFeedback_InterpolatesThenHoldsTheEndStrength()
	{
		Assert.Equal(TriggerEffects.MultiplePositionFeedback([1, 2, 3, 3, 3, 3, 3, 3, 3, 3]).Bytes.ToArray(), TriggerEffects.SlopeFeedback(0, 2, 1, 3).Bytes.ToArray());
	}

	[Fact]
	public void Raw_PadsToElevenBytes()
	{
		Assert.Equal(Effect(2, 144, 160, 255), TriggerEffects.Raw([2, 144, 160, 255]).Bytes.ToArray());
		Assert.Throws<ArgumentException>(() => TriggerEffects.Raw(new byte[12]));
	}
}
