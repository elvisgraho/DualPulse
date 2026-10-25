using System.Collections.Concurrent;
using DualPulse.Core.Devices;

namespace DualPulse.Core.Tests;

// In-memory IHidTransport for session/manager tests: devices can appear and vanish, connections serve queued
// reports, can fail on demand, record writes and answer the firmware feature report.
internal sealed class FakeHidTransport : IHidTransport
{
	public const string BluetoothPath = @"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#c&1#{4d1e55b2-f16f-11cf-88cb-001111000030}";

	public const string UsbPath = @"\\?\hid#vid_054c&pid_0ce6&mi_03#a&1#{4d1e55b2-f16f-11cf-88cb-001111000030}";

	private readonly object gate = new();

	private readonly List<FakeDevice> devices = [];

	public event EventHandler? DevicesChanged;

	public IReadOnlyList<HidDeviceInfo> Enumerate()
	{
		lock (gate)
		{
			return devices.Select(d => d.Info).ToList();
		}
	}

	public IHidConnection Open(HidDeviceInfo device)
	{
		FakeDevice? target;
		lock (gate)
		{
			target = devices.FirstOrDefault(d => d.Info.Path == device.Path);
		}
		if (target == null || target.FailOpen)
		{
			throw new IOException("Device could not be opened.");
		}
		return target.Connect();
	}

	public FakeDevice Add(string path, int productId = 0x0CE6, bool failOpen = false, string? serialNumber = null)
	{
		bool bluetooth = ControllerDiscovery.IsBluetooth(path);
		FakeDevice device = new(new HidDeviceInfo(path, 0x054C, productId, bluetooth ? 78 : 64, bluetooth ? 547 : 48, bluetooth ? 547 : 64, serialNumber)) { FailOpen = failOpen };
		lock (gate)
		{
			devices.Add(device);
		}
		DevicesChanged?.Invoke(this, EventArgs.Empty);
		return device;
	}

	public void Remove(FakeDevice device)
	{
		lock (gate)
		{
			devices.Remove(device);
		}
		device.Connections.ToList().ForEach(c => c.Fail());
		DevicesChanged?.Invoke(this, EventArgs.Empty);
	}
}

internal sealed class FakeDevice(HidDeviceInfo info)
{
	public HidDeviceInfo Info { get; } = info;

	public volatile bool FailOpen;

	public byte[]? FirmwareReport = TestReports.Firmware0630;

	public byte[]? PairingReport = TestReports.Pairing;

	public ConcurrentQueue<FakeConnection> Connections { get; } = new();

	public FakeConnection Connect()
	{
		FakeConnection connection = new(this);
		Connections.Enqueue(connection);
		return connection;
	}

	public FakeConnection Latest => Connections.Last();
}

internal sealed class FakeConnection(FakeDevice device) : IHidConnection
{
	private readonly BlockingCollection<byte[]?> reports = new();

	private readonly object writeGate = new();

	private readonly List<byte[]> writes = [];

	private volatile bool disposed;

	// Called on the reader's thread just before each report is returned (e.g. to advance fake time).
	public Action? OnRead { get; set; }

	public bool IsDisposed => disposed;

	// While set, writes throw as if the device rejected them.
	public volatile bool FailWrites;

	public int PendingReports => reports.Count;

	public void Feed(byte[] report)
	{
		reports.Add(report);
	}

	public void Fail()
	{
		if (!reports.IsAddingCompleted)
		{
			reports.Add(null);
		}
	}

	public IReadOnlyList<byte[]> Writes
	{
		get
		{
			lock (writeGate)
			{
				return writes.ToList();
			}
		}
	}

	public int Read(Span<byte> buffer, TimeSpan timeout)
	{
		byte[]? report;
		try
		{
			if (!reports.TryTake(out report, timeout))
			{
				throw new TimeoutException();
			}
		}
		catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
		{
			throw new IOException("Connection closed.");
		}
		if (report == null || disposed)
		{
			throw new IOException("Device gone.");
		}
		OnRead?.Invoke();
		report.CopyTo(buffer);
		return report.Length;
	}

	public void Write(ReadOnlySpan<byte> report)
	{
		if (disposed)
		{
			throw new IOException("Connection closed.");
		}
		if (FailWrites)
		{
			throw new IOException("Write rejected.");
		}
		lock (writeGate)
		{
			writes.Add(report.ToArray());
		}
	}

	public void GetFeature(Span<byte> buffer)
	{
		byte[]? report = buffer[0] switch { 0x20 => device.FirmwareReport, 0x09 => device.PairingReport, _ => null };
		if (report != null)
		{
			report.AsSpan(0, Math.Min(report.Length, buffer.Length)).CopyTo(buffer);
			return;
		}
		throw new IOException("Feature report not supported.");
	}

	public void Dispose()
	{
		disposed = true;
		reports.CompleteAdding();
	}
}
