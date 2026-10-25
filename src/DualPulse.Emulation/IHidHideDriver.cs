namespace DualPulse.Emulation;

// The HidHide filter driver's configuration: apps allowed to see hidden devices (full image names in DOS device form,
// e.g. \Device\HarddiskVolume3\...\DualPulse.exe), hidden devices (device instance ids, e.g. HID\VID_054C&...), and
// whether hiding is active. These lists are system-wide and persist until changed.
public interface IHidHideDriver
{
	bool IsInstalled { get; }

	// Whether the driver is registered as a filter on the HID device class. Without that it accepts settings but
	// hides nothing (seen when only the driver file was installed, without HidHide's setup).
	bool IsAttached { get; }

	IReadOnlyList<string> GetAllowedApps();

	void SetAllowedApps(IReadOnlyList<string> apps);

	IReadOnlyList<string> GetHiddenDevices();

	void SetHiddenDevices(IReadOnlyList<string> devices);

	bool GetActive();

	void SetActive(bool active);
}
