namespace DualPulse.Core.Devices;

// Chooses which device node to bind to. A DualSense plugged in by cable while paired over Bluetooth shows up twice
// (both nodes deliver input), and Windows can keep a stale node listed after a switch, so the choice is explicit.
public static class ControllerDiscovery
{
	// HID-over-Bluetooth service class GUID, present in the device path of Bluetooth HID nodes.
	private const string BluetoothHidServiceGuid = "{00001124-0000-1000-8000-00805f9b34fb}";

	// The serial DualPulse gives the virtual DualSense it creates (through VIIPER), so it is never taken for a real
	// controller: it has the same ids and is a USB device, which would otherwise be preferred. VIIPER writes the shell
	// colour code into characters 5-6 and a counter into the last two (to keep serials unique), so those are
	// placeholders and ignored when matching.
	public const string VirtualSerialNumber = "DPLS00DUALPULSE-VIRTUAL00";

	// The virtual DualSense without its sound device: a serial of its own, since Windows remembers a device's layout
	// by its serial number.
	public const string ControllerOnlyVirtualSerialNumber = "DPLS01DUALPULSE-VIRTUAL00";

	public static bool IsOwnVirtual(HidDeviceInfo device) => IsVirtualSerial(device.SerialNumber);

	public static bool IsVirtualSerial(string? serial)
	{
		return serial != null && serial.Length == VirtualSerialNumber.Length
			&& serial.AsSpan(0, 4).SequenceEqual(VirtualSerialNumber.AsSpan(0, 4))
			&& serial.AsSpan(6, serial.Length - 8).SequenceEqual(VirtualSerialNumber.AsSpan(6, VirtualSerialNumber.Length - 8));
	}

	public static bool IsBluetooth(string? devicePath)
	{
		return devicePath != null && devicePath.Contains(BluetoothHidServiceGuid, StringComparison.OrdinalIgnoreCase);
	}

	// Among nodes of the same controller, prefer USB (lower latency, no radio) over Bluetooth.
	public static string? ChoosePreferredPath(IEnumerable<string?> devicePaths)
	{
		List<string> paths = devicePaths.OfType<string>().ToList();
		return paths.FirstOrDefault(path => !IsBluetooth(path)) ?? paths.FirstOrDefault();
	}
}
