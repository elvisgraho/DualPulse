# Phase V (virtual DualSense) acceptance

Spec: `docs/superpowers/specs/2026-10-23-virtual-dualsense-design.md`. Setup and test: `docs/virtual-dualsense.md`.
Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Game output block passes through as report 0x31's common block, CRC valid (unit tests) | Pass |
| USB input report round-trips captured Bluetooth input through the parser (unit tests) | Pass |
| VIIPER client: requests, errors, streams, audio frames against a fake server (unit tests) | Pass |
| Virtual DualSense pad: creation, input, output/haptics/speaker events, removal against a fake server | Pass |
| Emulation wiring: game block drives the controller, haptics as 0x32, speaker as 0x39, every report forwarded | Pass |
| Own audio features stand down; Emulation page choice and VIIPER status (unit tests) | Pass |
| Probe without VIIPER reports "VIIPER is not running" | Pass |
| VIIPER and usbip-win2 installed; probe: virtual DualSense appears in Windows, host output blocks arrive | Pass |
| DualPulse in DualSense mode: device created in ~10 ms, stays in Windows, not mistaken for a real controller | Pass (after fixes) |
| Game haptics and speaker through VIIPER audio streams | Blocked (VIIPER drops the device on >392-byte audio chunks) |
| A native DualSense game over Bluetooth: triggers, lights, haptics, speaker | Open |

Found on hardware: VIIPER needs `usbip` on its PATH; DualPulse listed the virtual pad as a second USB controller
(VIIPER rewrites serial characters 5-6 and the last two, so matching now ignores those); pad creation could wait
without limit or logging (now bounded, logged, off the caller's thread); opening the audio streams made VIIPER drop the
device (audio off by default).


## In-house USB/IP device (replaces VIIPER), 2026-10-24

| Check | Result |
| --- | --- |
| USB/IP protocol: device list, import, submits, unlinks, isochronous returns with the device's start frame (unit tests) | Pass |
| Virtual DualSense: descriptors, strings, HID and audio class requests, input, output, audio split, full-speed intervals (unit tests) | Pass |
| Isochronous audio on a 1 ms frame clock: consecutive start frames, catch-up after a gap, unlink, microphone silence (unit tests) | Pass |
| `UsbIpDualSensePad`: plug-in, input, output block, Lost on host release, unplug on dispose, failed attach (unit tests) | Pass |
| `UsbIpWinAttacher`: finds usbip.exe, reads the hub port, reports failures (unit tests) | Pass |
| Windows enumerates the device (composite device, HID, speaker endpoint), output blocks arrive | Pass |
| Game audio through usbip-win2: 100 transfers/s, a 0.3 tone arrives at peak 9831 on speaker and haptics channels | Pass |
| Probe `dualsense-pad` (the app's classes end to end): plugged in, tone arrives, unplugged cleanly | Pass |
| DualPulse in DualSense mode with a native DualSense game over Bluetooth: triggers, lights, haptics, speaker | Open |

Found while bringing it up:
- Windows never streamed audio. The real controller's high-speed `bInterval` values (4, 6) are invalid at full speed.
  usbip-win2 turns full-speed intervals into microframes, so they became 8 ms periods. All endpoints now use 1.
- Instant isochronous completions that echoed the host's start frame made Windows reset the pipe thousands of times a
  second. Transfers are now paced on a frame clock.
- The probe's tone had never played: WASAPI shared mode rejects a plain 4-channel float format and needs the
  endpoint's extensible mix format. The exception was swallowed in a background task.
