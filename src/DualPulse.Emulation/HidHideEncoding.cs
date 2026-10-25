using System.Text;

namespace DualPulse.Emulation;

// Formats HidHide's driver interface uses: lists are MULTI_SZ (UTF-16 strings, each null-terminated, plus a closing
// null), and allowed apps are named by their DOS device path (\Device\HarddiskVolumeN\...) instead of a drive letter.
public static class HidHideEncoding
{
	public static byte[] ToMultiSz(IReadOnlyList<string> entries)
	{
		StringBuilder text = new();
		foreach (string entry in entries)
		{
			text.Append(entry).Append('\0');
		}
		text.Append('\0');
		if (entries.Count == 0)
		{
			text.Append('\0');
		}
		return Encoding.Unicode.GetBytes(text.ToString());
	}

	public static IReadOnlyList<string> FromMultiSz(ReadOnlySpan<byte> buffer)
	{
		string text = Encoding.Unicode.GetString(buffer[..(buffer.Length & ~1)]);
		List<string> entries = [];
		foreach (string entry in text.Split('\0'))
		{
			if (entry.Length == 0)
			{
				break;
			}
			entries.Add(entry);
		}
		return entries;
	}

	// queryDosDevice maps a drive ("C:") to its device name ("\Device\HarddiskVolume3"); null if the path isn't on a
	// lettered drive or the drive is unknown.
	public static string? ToDosDeviceName(string fullPath, Func<string, string?> queryDosDevice)
	{
		if (fullPath.Length < 3 || fullPath[1] != ':' || fullPath[2] != '\\' || !char.IsLetter(fullPath[0]))
		{
			return null;
		}
		string? device = queryDosDevice(fullPath[..2].ToUpperInvariant());
		return device == null ? null : device + fullPath[2..];
	}
}
