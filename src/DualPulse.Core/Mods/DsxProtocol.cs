using System.Globalization;
using System.Text.Json;
using DualPulse.Core.Protocol;

namespace DualPulse.Core.Mods;

// The DSX mod protocol (v2 and v3.1+), implemented from its published description: a JSON packet
// {"instructions":[{"type":N,"parameters":[...]}]} where parameter 0 is the controller index. Instruction types:
// 0 status, 1 trigger, 2 lightbar, 3 player LEDs (five on/off values), 4 trigger threshold, 5 mic LED, 6 player LEDs
// (pattern), 7 reset to user settings. Parameters may arrive as numbers, numeric strings or booleans. Anything that
// can't be understood is counted as rejected, never thrown.
public static class DsxProtocol
{
	private const string StatusText = "DSX Received UDP Instructions";

	public static IReadOnlyList<ModInstruction> Parse(ReadOnlySpan<byte> utf8Json, out int rejected)
	{
		rejected = 0;
		List<ModInstruction> instructions = [];
		JsonDocument document;
		try
		{
			Utf8JsonReader reader = new(utf8Json, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
			document = JsonDocument.ParseValue(ref reader);
		}
		catch (JsonException)
		{
			rejected = 1;
			return instructions;
		}
		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object || !TryGetProperty(document.RootElement, "instructions", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
			{
				rejected = 1;
				return instructions;
			}
			foreach (JsonElement element in list.EnumerateArray())
			{
				ModInstruction? instruction = element.ValueKind == JsonValueKind.Object ? ParseInstruction(element) : null;
				if (instruction == null)
				{
					rejected++;
				}
				else
				{
					instructions.Add(instruction);
				}
			}
		}
		return instructions;
	}

	public static byte[] StatusReply(DsxStatus status)
	{
		using MemoryStream stream = new();
		using (Utf8JsonWriter json = new(stream))
		{
			json.WriteStartObject();
			json.WriteString("Status", StatusText);
			json.WriteString("TimeReceived", status.TimeReceived.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
			json.WriteBoolean("isControllerConnected", status.ControllerConnected);
			json.WriteNumber("BatteryLevel", status.BatteryLevel);
			json.WriteStartArray("Devices");
			if (status.ControllerConnected)
			{
				json.WriteStartObject();
				json.WriteNumber("Index", 0);
				json.WriteString("MacAddress", status.MacAddress ?? "");
				json.WriteNumber("DeviceType", 0);
				json.WriteNumber("ConnectionType", status.Bluetooth ? 1 : 0);
				json.WriteNumber("BatteryLevel", status.BatteryLevel);
				json.WriteBoolean("IsSupportAT", true);
				json.WriteBoolean("IsSupportLightBar", true);
				json.WriteBoolean("IsSupportPlayerLED", true);
				json.WriteBoolean("IsSupportMicLED", true);
				json.WriteEndObject();
			}
			json.WriteEndArray();
			json.WriteEndObject();
		}
		return stream.ToArray();
	}

	private static ModInstruction? ParseInstruction(JsonElement element)
	{
		if (!TryGetProperty(element, "type", out JsonElement typeElement) || ToInt(typeElement) is not { } type)
		{
			return null;
		}
		List<int?> parameters = [];
		if (TryGetProperty(element, "parameters", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
		{
			parameters.AddRange(list.EnumerateArray().Select(ToInt));
		}
		int index = parameters.Count > 0 ? parameters[0] ?? 0 : 0;
		return type switch
		{
			0 => new ModStatusRequest(),
			1 => ParseTrigger(index, parameters),
			2 => ParseLightbar(index, parameters),
			3 => ParseLegacyPlayerLeds(index, parameters),
			4 => Arguments(parameters, 1, 2) is [var trigger, var threshold] ? new ModTriggerThreshold(index, Side(trigger), (byte)Math.Clamp(threshold, 0, 255)) : null,
			5 => Arguments(parameters, 1, 1) is [var mic] ? MicLedFor(index, mic) : null,
			6 => Arguments(parameters, 1, 1) is [var pattern] ? PlayerPatternFor(index, pattern) : null,
			7 => new ModReset(index),
			_ => null
		};
	}

	private static ModInstruction? ParseTrigger(int index, List<int?> parameters)
	{
		if (Arguments(parameters, 1, 2) is not [var side, var mode])
		{
			return null;
		}
		ModTrigger? trigger = Side(side);
		if (trigger == null)
		{
			return new ModTriggerEffect(index, null, TriggerEffect.Off, "Both triggers off");
		}
		int[] arguments = parameters.Skip(3).Select(p => p ?? 0).ToArray();
		return DsxTriggerModes.ToEffect(mode, arguments) is { } effect ? new ModTriggerEffect(index, trigger, effect.Effect, effect.Description) : null;
	}

	private static ModInstruction? ParseLightbar(int index, List<int?> parameters)
	{
		if (Arguments(parameters, 1, 3) is not [var red, var green, var blue])
		{
			return null;
		}
		double brightness = parameters.Count > 4 && parameters[4] is { } value ? Math.Clamp(value, 0, 255) / 255.0 : 1.0;
		return new ModLightbar(index, new Rgb(Scale(red, brightness), Scale(green, brightness), Scale(blue, brightness)));
	}

	private static ModInstruction? ParseLegacyPlayerLeds(int index, List<int?> parameters)
	{
		if (Arguments(parameters, 1, 5) is not { } leds)
		{
			return null;
		}
		byte bits = 0;
		for (int led = 0; led < 5; led++)
		{
			if (leds[led] != 0)
			{
				bits |= (byte)(1 << led);
			}
		}
		return new ModPlayerLeds(index, bits);
	}

	private static ModInstruction? MicLedFor(int index, int value)
	{
		return value switch
		{
			0 => new ModMicLed(index, MicLed.On),
			1 => new ModMicLed(index, MicLed.Pulse),
			2 => new ModMicLed(index, MicLed.Off),
			_ => null
		};
	}

	// One to four players light the patterns Sony uses; "five" lights all, and 5 turns them off.
	private static ModInstruction? PlayerPatternFor(int index, int value)
	{
		byte? bits = value switch
		{
			0 => 0b00100,
			1 => 0b01010,
			2 => 0b10101,
			3 => 0b11011,
			4 => 0b11111,
			5 => 0b00000,
			_ => null
		};
		return bits is { } pattern ? new ModPlayerLeds(index, pattern) : null;
	}

	private static ModTrigger? Side(int value)
	{
		return value switch
		{
			1 => ModTrigger.Left,
			2 => ModTrigger.Right,
			_ => null
		};
	}

	// count parameters starting at start, or null if any is missing.
	private static int[]? Arguments(List<int?> parameters, int start, int count)
	{
		if (parameters.Count < start + count)
		{
			return null;
		}
		int[] values = new int[count];
		for (int i = 0; i < count; i++)
		{
			if (parameters[start + i] is not { } value)
			{
				return null;
			}
			values[i] = value;
		}
		return values;
	}

	private static byte Scale(int value, double factor)
	{
		return (byte)Math.Round(Math.Clamp(value, 0, 255) * factor, MidpointRounding.AwayFromZero);
	}

	private static int? ToInt(JsonElement element)
	{
		switch (element.ValueKind)
		{
		case JsonValueKind.Number:
			return element.TryGetDouble(out double number) ? (int)Math.Clamp(Math.Round(number), int.MinValue, int.MaxValue) : null;
		case JsonValueKind.String:
			return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
				? (int)Math.Clamp(Math.Round(parsed), int.MinValue, int.MaxValue)
				: bool.TryParse(element.GetString(), out bool flag) ? (flag ? 1 : 0) : null;
		case JsonValueKind.True:
			return 1;
		case JsonValueKind.False:
			return 0;
		default:
			return null;
		}
	}

	private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
	{
		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}
		value = default;
		return false;
	}
}
