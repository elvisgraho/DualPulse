using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace DualPulse.Core.Features;

// DualSense firmware info from feature report 0x20 (64 bytes): build date/time as ASCII at bytes 1-19,
// hardware version (le32) at 24, firmware version (le32) at 28, "update version" (le16) at 44. The update
// version is what Sony shows as the firmware number (0x0630 -> "06.30") and what feature checks use.
public sealed partial class FirmwareInfo
{
	public const byte ReportId = 0x20;

	// Firmware with update version 02.24+ supports the improved rumble emulation (COMPATIBLE_VIBRATION2).
	public const ushort ImprovedRumbleMinimumUpdateVersion = 0x0224;

	public required string BuildDate { get; init; }

	public required uint HardwareVersion { get; init; }

	public required uint FirmwareVersion { get; init; }

	public required ushort UpdateVersion { get; init; }

	public string UpdateVersionText => $"{UpdateVersion >> 8:X2}.{UpdateVersion & 0xFF:X2}";

	public bool SupportsImprovedRumble => UpdateVersion >= ImprovedRumbleMinimumUpdateVersion;

	public string Summary => $"Firmware {UpdateVersionText} (build {BuildDate}), hardware 0x{HardwareVersion:X4}, firmware ID 0x{FirmwareVersion:X8}";

	public static FirmwareInfo? Parse(ReadOnlySpan<byte> report)
	{
		if (report.Length < 46 || report[0] != ReportId)
		{
			return null;
		}
		string date = Encoding.ASCII.GetString(report.Slice(1, 11)).Trim('\0', ' ');
		string time = Encoding.ASCII.GetString(report.Slice(12, 8)).Trim('\0', ' ');
		return new FirmwareInfo
		{
			BuildDate = RepeatedSpaces().Replace(date, " ") + " " + time,
			HardwareVersion = BinaryPrimitives.ReadUInt32LittleEndian(report.Slice(24, 4)),
			FirmwareVersion = BinaryPrimitives.ReadUInt32LittleEndian(report.Slice(28, 4)),
			UpdateVersion = BinaryPrimitives.ReadUInt16LittleEndian(report.Slice(44, 2))
		};
	}

	[GeneratedRegex(" +")]
	private static partial Regex RepeatedSpaces();
}
