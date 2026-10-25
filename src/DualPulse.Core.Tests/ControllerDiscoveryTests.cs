using Xunit;
using DualPulse.Core.Devices;

namespace DualPulse.Core.Tests;

public class ControllerDiscoveryTests
{
	// Real device paths observed for one DualSense connected by Bluetooth and USB at the same time.
	private const string BluetoothPath = "\\\\?\\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#c&2f9f8ee2&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

	private const string UsbPath = "\\\\?\\hid#vid_054c&pid_0ce6&mi_03#a&e0adb69&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

	[Fact]
	public void IsBluetooth_DetectsHidOverBluetoothPaths()
	{
		Assert.True(ControllerDiscovery.IsBluetooth(BluetoothPath));
		Assert.True(ControllerDiscovery.IsBluetooth(BluetoothPath.ToUpperInvariant()));
		Assert.False(ControllerDiscovery.IsBluetooth(UsbPath));
		Assert.False(ControllerDiscovery.IsBluetooth(null));
	}

	[Fact]
	public void ChoosePreferredPath_PrefersUsbWhenBothArePresent()
	{
		Assert.Equal(UsbPath, ControllerDiscovery.ChoosePreferredPath(new string[2] { BluetoothPath, UsbPath }));
		Assert.Equal(UsbPath, ControllerDiscovery.ChoosePreferredPath(new string[2] { UsbPath, BluetoothPath }));
	}

	[Fact]
	public void ChoosePreferredPath_FallsBackToBluetooth()
	{
		Assert.Equal(BluetoothPath, ControllerDiscovery.ChoosePreferredPath(new string[1] { BluetoothPath }));
	}

	[Fact]
	public void ChoosePreferredPath_ReturnsNullWhenNothingIsConnected()
	{
		Assert.Null(ControllerDiscovery.ChoosePreferredPath(new string[0]));
	}
}
