using DualPulse.Core.Devices;

namespace DualPulse.Core.Power;

// A Bluetooth controller's address, which an IBluetoothRadio needs to drop its link (the controller powers down when
// the link is lost). It comes from feature report 0x09 (pairing info), whose bytes 1-6 hold the MAC
// least-significant byte first.
public static class BluetoothPower
{
	private const byte PairingInfoReportId = 0x09;

	public static bool TryReadMac(IHidConnection connection, out long mac)
	{
		byte[] buffer = new byte[64];
		buffer[0] = PairingInfoReportId;
		connection.GetFeature(buffer);
		mac = ParseMacAddress(buffer);
		return mac != 0;
	}

	public static long ParseMacAddress(ReadOnlySpan<byte> pairingInfoReport)
	{
		if (pairingInfoReport.Length < 7 || pairingInfoReport[0] != PairingInfoReportId)
		{
			return 0;
		}
		long address = 0;
		for (int i = 6; i >= 1; i--)
		{
			address = (address << 8) | pairingInfoReport[i];
		}
		return address;
	}

	public static string FormatMacAddress(long address)
	{
		return string.Join(":", Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));
	}
}
