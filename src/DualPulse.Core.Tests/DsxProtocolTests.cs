using System.Text;
using System.Text.Json;
using DualPulse.Core.Mods;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class DsxProtocolTests
{
	private static IReadOnlyList<ModInstruction> Parse(string json, out int rejected)
	{
		return DsxProtocol.Parse(Encoding.UTF8.GetBytes(json), out rejected);
	}

	private static ModTriggerEffect Trigger(string parameters)
	{
		IReadOnlyList<ModInstruction> parsed = Parse($$"""{"instructions":[{"type":1,"parameters":[{{parameters}}]}]}""", out int rejected);
		Assert.Equal(0, rejected);
		return Assert.IsType<ModTriggerEffect>(Assert.Single(parsed));
	}

	private static void AssertEffect(TriggerEffect expected, ModTriggerEffect actual)
	{
		Assert.Equal(expected.Bytes.ToArray(), actual.Effect.Bytes.ToArray());
	}

	[Fact]
	public void Trigger_V3Modes()
	{
		ModTriggerEffect feedback = Trigger("0,2,21,1,8");
		Assert.Equal(ModTrigger.Right, feedback.Trigger);
		AssertEffect(TriggerEffects.Feedback(1, 8), feedback);
		Assert.Equal(ModTrigger.Left, Trigger("0,1,20").Trigger);
		AssertEffect(TriggerEffect.Off, Trigger("0,1,20"));
		AssertEffect(TriggerEffects.Weapon(2, 6, 8), Trigger("0,1,22,2,6,8"));
		AssertEffect(TriggerEffects.Vibration(1, 8, 10), Trigger("0,1,23,1,8,10"));
		AssertEffect(TriggerEffects.SlopeFeedback(1, 9, 8, 1), Trigger("0,1,24,1,9,8,1"));
		AssertEffect(TriggerEffects.MultiplePositionFeedback([8, 8, 8, 0, 0, 0, 8, 8, 0, 0]), Trigger("0,1,25,8,8,8,0,0,0,8,8,0,0"));
		AssertEffect(TriggerEffects.MultiplePositionVibration(10, [8, 8, 8, 8, 8, 0, 0, 0, 8, 8]), Trigger("0,1,26,10,8,8,8,8,8,0,0,0,8,8"));
	}

	[Fact]
	public void Trigger_LegacyPresetsUseTheirRawBytes()
	{
		AssertEffect(TriggerEffect.Off, Trigger("0,1,0"));
		AssertEffect(TriggerEffects.Raw([2, 144, 160, 255]), Trigger("0,1,1"));
		AssertEffect(TriggerEffects.Raw([2, 69, 160, 255]), Trigger("0,1,3"));
		AssertEffect(TriggerEffects.Raw([2, 0, 255, 255]), Trigger("0,1,6"));
		AssertEffect(TriggerEffects.Raw([2, 0, 255, 0]), Trigger("0,1,7"));
		AssertEffect(TriggerEffects.Raw([0x26, 0, 255, 0, 0, 0, 255, 25]), Trigger("0,1,8,25"));
		AssertEffect(TriggerEffects.Raw([33, 2, 39, 33, 0, 39, 38, 2]), Trigger("0,1,9"));
		AssertEffect(TriggerEffects.Raw([0x26, 0, 255, 0, 0, 0, 255, 10]), Trigger("0,1,19"));
	}

	[Fact]
	public void Trigger_LegacyParameterModes()
	{
		AssertEffect(TriggerEffects.Feedback(0, 8), Trigger("0,1,13,0,8"));
		AssertEffect(TriggerEffects.Bow(0, 8, 2, 5), Trigger("0,1,14,0,8,2,5"));
		AssertEffect(TriggerEffects.Galloping(0, 9, 2, 4, 10), Trigger("0,1,15,0,9,2,4,10"));
		AssertEffect(TriggerEffects.Weapon(2, 7, 8), Trigger("0,1,16,2,7,8"));
		AssertEffect(TriggerEffects.Vibration(0, 8, 10), Trigger("0,1,17,0,8,10"));
		AssertEffect(TriggerEffects.Machine(0, 9, 7, 7, 10, 0), Trigger("0,1,18,0,9,7,7,10,0"));
	}

	[Fact]
	public void Trigger_CustomValues_ModeByteThenSevenValues()
	{
		// Custom mode 8 is "PulseAB" (mode byte 38).
		AssertEffect(TriggerEffects.Raw([38, 10, 20, 30, 40, 50, 60, 70]), Trigger("0,1,12,8,10,20,30,40,50,60,70"));
	}

	[Fact]
	public void Trigger_OutOfRangeValuesAreClamped()
	{
		AssertEffect(TriggerEffects.Weapon(2, 3, 8), Trigger("0,1,22,0,0,99"));
		AssertEffect(TriggerEffects.Vibration(9, 1, 255), Trigger("0,1,23,40,0,900"));
	}

	[Fact]
	public void Parameters_AcceptNumbersStringsAndBooleans()
	{
		AssertEffect(TriggerEffects.Feedback(1, 8), Trigger("\"0\",\"2\",21,\"1\",8.0"));
		IReadOnlyList<ModInstruction> parsed = Parse("""{"instructions":[{"type":3,"parameters":[0,true,false,true,false,true]}]}""", out _);
		Assert.Equal(0b10101, Assert.IsType<ModPlayerLeds>(Assert.Single(parsed)).Bits);
	}

	[Fact]
	public void Lightbar_V2WithoutBrightness_V3ScalesByBrightness()
	{
		IReadOnlyList<ModInstruction> parsed = Parse("""{"instructions":[{"type":2,"parameters":[0,255,128,0]},{"type":2,"parameters":[0,200,100,50,128]}]}""", out _);

		Assert.Equal(new Rgb(255, 128, 0), Assert.IsType<ModLightbar>(parsed[0]).Colour);
		Assert.Equal(new Rgb(100, 50, 25), Assert.IsType<ModLightbar>(parsed[1]).Colour);
	}

	[Theory]
	[InlineData(0, 0b00100)]
	[InlineData(1, 0b01010)]
	[InlineData(2, 0b10101)]
	[InlineData(3, 0b11011)]
	[InlineData(4, 0b11111)]
	[InlineData(5, 0b00000)]
	public void PlayerLeds_NewRevision(int value, int bits)
	{
		IReadOnlyList<ModInstruction> parsed = Parse($$"""{"instructions":[{"type":6,"parameters":[0,{{value}}]}]}""", out _);

		Assert.Equal((byte)bits, Assert.IsType<ModPlayerLeds>(Assert.Single(parsed)).Bits);
	}

	[Fact]
	public void MicLed_ThresholdResetAndStatus()
	{
		IReadOnlyList<ModInstruction> parsed = Parse("""
			{"instructions":[
				{"type":5,"parameters":[0,1]},
				{"type":4,"parameters":[0,2,120]},
				{"type":7,"parameters":[0]},
				{"type":0,"parameters":[]}
			]}
			""", out int rejected);

		Assert.Equal(0, rejected);
		Assert.Equal(MicLed.Pulse, Assert.IsType<ModMicLed>(parsed[0]).MicLed);
		ModTriggerThreshold threshold = Assert.IsType<ModTriggerThreshold>(parsed[1]);
		Assert.Equal((ModTrigger.Right, (byte)120), (threshold.Trigger, threshold.Threshold));
		Assert.IsType<ModReset>(parsed[2]);
		Assert.IsType<ModStatusRequest>(parsed[3]);
	}

	[Fact]
	public void Malformed_IsRejectedWithoutThrowing()
	{
		Assert.Empty(Parse("{not json", out int invalidJson));
		Assert.Equal(1, invalidJson);

		IReadOnlyList<ModInstruction> parsed = Parse("""
			{"instructions":[
				{"type":99,"parameters":[0]},
				{"type":1,"parameters":[0,1]},
				{"type":1,"parameters":[0,1,77]},
				{"type":2,"parameters":[0,255]},
				{"type":7,"parameters":[0]}
			]}
			""", out int rejected);

		Assert.Equal(4, rejected);
		Assert.IsType<ModReset>(Assert.Single(parsed));
	}

	[Fact]
	public void StatusReply_HasTheFieldsModsRead()
	{
		byte[] reply = DsxProtocol.StatusReply(new DsxStatus(true, 70, "12:34:56:78:9A:BC", Bluetooth: true, new DateTime(2026, 10, 23, 20, 0, 0)));

		using JsonDocument document = JsonDocument.Parse(reply);
		JsonElement root = document.RootElement;
		Assert.False(string.IsNullOrEmpty(root.GetProperty("Status").GetString()));
		Assert.Equal("2026-10-23 20:00:00.000", root.GetProperty("TimeReceived").GetString());
		Assert.True(root.GetProperty("isControllerConnected").GetBoolean());
		Assert.Equal(70, root.GetProperty("BatteryLevel").GetInt32());
		JsonElement device = Assert.Single(root.GetProperty("Devices").EnumerateArray());
		Assert.Equal(0, device.GetProperty("Index").GetInt32());
		Assert.Equal("12:34:56:78:9A:BC", device.GetProperty("MacAddress").GetString());
		Assert.Equal(0, device.GetProperty("DeviceType").GetInt32());
		Assert.Equal(1, device.GetProperty("ConnectionType").GetInt32());
		Assert.True(device.GetProperty("IsSupportAT").GetBoolean());
		Assert.True(device.GetProperty("IsSupportMicLED").GetBoolean());

		using JsonDocument none = JsonDocument.Parse(DsxProtocol.StatusReply(new DsxStatus(false, 0, null, false, DateTime.Now)));
		Assert.False(none.RootElement.GetProperty("isControllerConnected").GetBoolean());
		Assert.Empty(none.RootElement.GetProperty("Devices").EnumerateArray());
	}
}
