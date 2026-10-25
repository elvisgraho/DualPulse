namespace DualPulse.Core.Power;

// Drops a controller's Bluetooth link, which makes it switch itself off. The OS owns the link, so each platform has
// its own implementation; one without support refuses.
public interface IBluetoothRadio
{
	// Returns true when a radio accepted the disconnect of the device with this address (see BluetoothPower).
	bool Disconnect(long mac);
}
