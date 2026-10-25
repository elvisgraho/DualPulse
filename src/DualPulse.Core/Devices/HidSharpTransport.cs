using HidSharp;

namespace DualPulse.Core.Devices;

// IHidTransport over HidSharp, limited to supported Sony controllers.
public sealed class HidSharpTransport : IHidTransport, IDisposable
{
	public const int SonyVendorId = 0x054C;

	public static readonly IReadOnlySet<int> SupportedProductIds = new HashSet<int> { 0x0CE6, 0x0DF2 };

	public HidSharpTransport()
	{
		DeviceList.Local.Changed += OnDeviceListChanged;
	}

	public event EventHandler? DevicesChanged;

	// The serial string, or null when the device has none or won't say (some refuse while another app holds them).
	private static string? SerialOf(HidDevice device)
	{
		try
		{
			return device.GetSerialNumber();
		}
		catch (Exception)
		{
			return null;
		}
	}

	public IReadOnlyList<HidDeviceInfo> Enumerate()
	{
		List<HidDeviceInfo> devices = [];
		foreach (HidDevice device in DeviceList.Local.GetHidDevices(SonyVendorId).Where(d => SupportedProductIds.Contains(d.ProductID)))
		{
			try
			{
				devices.Add(new HidDeviceInfo(device.DevicePath, device.VendorID, device.ProductID, device.GetMaxInputReportLength(), device.GetMaxOutputReportLength(), device.GetMaxFeatureReportLength(),
					SerialOf(device)));
			}
			catch (Exception)
			{
				// A node can vanish between enumeration and the length queries; it shows up again on the next poll.
			}
		}
		return devices;
	}

	public IHidConnection Open(HidDeviceInfo device)
	{
		HidDevice hidDevice = DeviceList.Local.GetHidDevices(SonyVendorId).FirstOrDefault(d => d.DevicePath == device.Path)
			?? throw new IOException("Device is no longer connected.");
		return new HidSharpConnection(hidDevice.Open(), device);
	}

	public void Dispose()
	{
		DeviceList.Local.Changed -= OnDeviceListChanged;
	}

	private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
	{
		DevicesChanged?.Invoke(this, EventArgs.Empty);
	}

	private sealed class HidSharpConnection(HidStream stream, HidDeviceInfo device) : IHidConnection
	{
		private readonly byte[] readBuffer = new byte[Math.Max(device.MaxInputLength, 1)];

		public int Read(Span<byte> buffer, TimeSpan timeout)
		{
			stream.ReadTimeout = (int)timeout.TotalMilliseconds;
			try
			{
				int count = stream.Read(readBuffer, 0, readBuffer.Length);
				readBuffer.AsSpan(0, count).CopyTo(buffer);
				return count;
			}
			catch (ObjectDisposedException exception)
			{
				throw new IOException("Connection closed.", exception);
			}
		}

		public void Write(ReadOnlySpan<byte> report)
		{
			byte[] buffer = report.ToArray();
			stream.Write(buffer, 0, buffer.Length);
		}

		public void GetFeature(Span<byte> buffer)
		{
			byte[] feature = new byte[Math.Max(buffer.Length, device.MaxFeatureLength)];
			feature[0] = buffer[0];
			stream.GetFeature(feature);
			feature.AsSpan(0, buffer.Length).CopyTo(buffer);
		}

		public void Dispose()
		{
			stream.Dispose();
		}
	}
}
