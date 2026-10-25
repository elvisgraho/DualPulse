# Virtual DualSense (Phase V): setup and first test

Most games with native DualSense support (adaptive triggers, haptics, speaker) only turn those features on for a USB
DualSense. In this mode DualPulse plugs in a virtual USB DualSense of its own. It relays what the game sends to your
real controller over Bluetooth, and relays your controller's input to the game.

The virtual controller is DualPulse's own code (`VirtualDualSense`, served over USB/IP on localhost). Windows still
needs a USB/IP driver to plug it in: **usbip-win2**. Nothing else has to be installed or running.

## One-time setup (needs admin)

1. Install the usbip-win2 driver (0.9.8.1 or newer) from https://github.com/vadimgrn/usbip-win2/releases. Reboot if
   asked. The installer may add its test-signing certificate ("USBIP") to the trusted roots. You can remove it later
   with `certlm.msc`. DualPulse finds `usbip.exe` in `C:\Program Files\USBip`, so it doesn't have to be on the PATH.
2. In DualPulse, on the Emulation page, turn on **Hide the real controller**, so games see only the virtual one.

## First test

1. Probe (DualPulse closed):

   ```
   DualPulse.Probe dualsense-pad
   ```

   This plugs in the virtual DualSense the way the app does. It plays a 440 Hz tone to its speaker endpoint, then
   unplugs it. Expected: `audio transfers` around 300 and `peak` around 9830 (the tone's 0.3 level), `lost False`.
   For more detail, `DualPulse.Probe usbip-dualsense 14 --tone --trace` logs every control request.
2. In DualPulse, on the Emulation page, set **What games see** to **DualSense**. Status should read Active. It reads
   "usbip-win2 isn't installed" without the driver, or "Couldn't plug in the virtual DualSense" when attaching fails
   (the log says why). Start a game with native DualSense support over Bluetooth. Check triggers, lights, haptics and
   speaker.

## How it works

- `UsbIpDualSensePad` runs a USB/IP server on a free loopback port. It plugs the device in with
  `usbip -t PORT attach -r 127.0.0.1 -b 1-1 --once --terse` and unplugs it with `usbip detach -p HUBPORT`.
- The descriptors and feature reports were captured from a real DualSense. The MAC address is replaced, and a serial
  string is added so DualPulse never takes the virtual pad for a real controller.
- The real controller is a high-speed USB device. The virtual one is full speed, so every endpoint uses
  `bInterval 1`. With the high-speed values, Windows never starts the audio stream.
- Isochronous audio runs on a 1 ms frame clock. Windows sends 10 ms transfers; each one completes after its 10
  frames, with consecutive start frames. Completing them instantly makes Windows race ahead and reset the pipe.

## Cable haptics after Bluetooth

On the cable, a game's haptics are sound on channels 3-4 of the controller's own USB audio device; DualPulse plays
them there. But a DualSense that has been connected over Bluetooth since it was switched on ignores that sound (it
still arrives: Windows meters it at full level) until it is switched off and on. This happens without DualPulse too
(tested 2026-10-25 with DualPulse closed). DualPulse shows a notice when the controller is plugged in from Bluetooth
in DualSense mode. To keep haptics while charging mid-game, charge from a USB charger or power bank: the controller
then stays on Bluetooth.

Separately, any report that selects classic rumble (flag0 0x02 without 0x01) stops audio haptics; DualPulse sends
the motor bits only while the motors run.

## Notes

- While a game drives the virtual DualSense, its output overrides DualPulse's lighting and trigger settings.
  DualPulse's own sound-to-haptics and controller audio stand down.
- Over USB, input and the game's output block are relayed, but game audio is not. Over USB the game can use the real
  controller directly anyway.
- Other operating systems: the device and server are portable. Only the attacher is Windows-specific. Linux would
  need one for the `vhci-hcd` driver's `usbip` tool, which needs root.
- History: Phase V first used VIIPER for the virtual device. That build dropped the device when game audio flowed,
  and it was an extra program to install and run. It was replaced on 2026-10-24.
