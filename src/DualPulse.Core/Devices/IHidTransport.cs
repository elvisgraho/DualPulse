namespace DualPulse.Core.Devices;

// SerialNumber is the device's serial string when it has one (used to recognise DualPulse's own virtual DualSense).
public sealed record HidDeviceInfo(string Path, int VendorId, int ProductId, int MaxInputLength, int MaxOutputLength, int MaxFeatureLength,
	string? SerialNumber = null);

// An open HID device. Read throws TimeoutException when no report arrives in time and IOException when the device
// is gone or the connection was disposed.
public interface IHidConnection : IDisposable
{
	int Read(Span<byte> buffer, TimeSpan timeout);

	void Write(ReadOnlySpan<byte> report);

	void GetFeature(Span<byte> buffer);
}

public interface IHidTransport
{
	IReadOnlyList<HidDeviceInfo> Enumerate();

	event EventHandler? DevicesChanged;

	IHidConnection Open(HidDeviceInfo device);
}
