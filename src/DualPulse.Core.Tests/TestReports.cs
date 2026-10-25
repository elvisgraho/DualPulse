using Microsoft.Extensions.Logging;

namespace DualPulse.Core.Tests;

internal static class TestReports
{
	// Feature report 0x20 from a real DualSense (firmware 06.30).
	public static readonly byte[] Firmware0630 = Hex("20 4A 75 6C 20 20 34 20 32 30 32 35 31 30 3A 33 38 3A 34 30 03 00 0B 00 11 08 00 00 2A 00 10 01 00 28 03 00 00 00 00 00 00 00 00 00 30 06 00 00 01 00 03 00 10 10 03 00 06 00 00 00 49 9F 80 30");

	// Feature report 0x09 (pairing info) from a real DualSense, with both Bluetooth addresses replaced (controller 12:34:56:78:9A:BC).
	public static readonly byte[] Pairing = Hex("09 BC 9A 78 56 34 12 08 25 00 66 55 44 33 22 11 6A 0C 5A 6A");

	public static byte[] Firmware(ushort updateVersion)
	{
		byte[] report = (byte[])Firmware0630.Clone();
		report[44] = (byte)(updateVersion & 0xFF);
		report[45] = (byte)(updateVersion >> 8);
		return report;
	}

	// A captured input report (Fixtures/<name>.hex).
	public static byte[] Fixture(string name)
	{
		return Hex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".hex")));
	}

	// A captured Bluetooth report with its 4-bit sequence number replaced.
	public static byte[] BluetoothWithSequence(string name, int sequence)
	{
		byte[] report = Fixture(name + "-bt");
		report[1] = (byte)((report[1] & 0x0F) | ((sequence & 0x0F) << 4));
		return report;
	}

	private static byte[] Hex(string spaced)
	{
		return spaced.Trim().Split(' ').Select(b => Convert.ToByte(b, 16)).ToArray();
	}

	public static void WaitUntil(Func<bool> condition, int timeoutMs = 3000)
	{
		if (!SpinWait.SpinUntil(condition, timeoutMs))
		{
			throw new TimeoutException("Condition not reached in time.");
		}
	}
}

// Captures log entries so tests can check what was reported.
internal sealed class ListLogger<T> : ILogger<T>
{
	public List<(LogLevel Level, string Message)> Entries { get; } = [];

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		lock (Entries)
		{
			Entries.Add((logLevel, formatter(state, exception)));
		}
	}
}
