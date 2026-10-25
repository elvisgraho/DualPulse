# R5 Emulation page acceptance

Hardware: DualSense, firmware 06.30, over USB. ViGEmBus driver 1.21.442 (package 1.22). HidHide installed from the
official package during the test. Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Virtual controller choice (Xbox 360 / DualShock 4 / Native) on the Emulation page; status "Active" | Pass |
| ViGEmBus shown as installed with its driver version | Pass |
| Hide on, controller reconnected: `joy.cpl` lists only the Xbox 360 controller | Pass |
| DualPulse keeps the controller while it is hidden (it is on HidHide's allowed list) | Pass |
| Hide off: the DualSense reappears in `joy.cpl` immediately, no reconnect needed | Pass |

Findings fixed during the test:

- HidHide lists must be read with a size query first; the driver rejects other buffer sizes once a list is non-empty.
  Before the fix, switching hiding off couldn't read the list.
- Only the HidHide driver file was installed (by the legacy app); it wasn't registered as a filter on the HID device
  class, so it accepted settings but hid nothing. DualPulse now detects this ("present but not set up for
  controllers") and links to the official release; installing the official HidHide package fixed it.
- Hiding applies when an app next opens the device, so turning it on needs a reconnect; the page says so.

HidHide's driver-level control codes were checked read-only against the installed driver before anything was written.
DualPulse only adds its own entries and removes exactly the devices it recorded.
