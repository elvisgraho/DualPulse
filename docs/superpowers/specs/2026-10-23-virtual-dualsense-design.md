# Virtual DualSense through VIIPER (Phase V)

Date: 2026-10-23. Status: the user approved "implement"; these design decisions were made while the user was away
and are marked **Decision** for review. This spec also carries the build order.

## Goal

Games with native DualSense support see a USB DualSense and drive their own adaptive triggers, lightbar, player LEDs,
haptics and speaker. DualPulse relays all of it to the real controller over Bluetooth, and relays the real
controller's input to the virtual one.

## VIIPER facts (DualSenseClient fork, docs and MIT Go client; read for facts, no code copied)

- Server: `viiper server`, API on `localhost:3242`. Localhost needs no password and no encryption by default.
  Needs usbip-win2 0.9.8.1 (signed driver; the user installs it).
- Management request:
  - Framing: `<path>[ <payload>]\0`; the response is JSON read until the server closes.
  - `bus/create` → `{"busId":N}`.
  - `bus/{id}/add {"type":"dualsense"}` → `{"busId":N,"devId":"1",...}`.
  - `bus/{id}/remove <devId>` and `bus/remove <id>`.
- Streams: open TCP, write the path followed by `\0`, then binary:
  - `bus/{b}/{d}`: device stream; the server sends 47-byte output frames. These are the same common block that
    report 0x31 carries: flags, motors, volumes, audio control, mute LED, both 11-byte trigger blocks, motor power,
    audio control 2, flags, lightbar setup, LED brightness, player LEDs, RGB.
  - `bus/{b}/{d}/raw`: client writes exact 64-byte USB input reports (0x01).
  - `bus/{b}/{d}/audio/haptics`: server sends `u16 LE length` + 2-channel S16LE 48 kHz haptics PCM.
  - `bus/{b}/{d}/audio`: the same framing, 4-channel S16LE 48 kHz (channels 1–2 speaker, 3–4 haptics). Length
    0xFFFF is a reset barrier.

## Decisions

- **Decision: own VIIPER client.** The NuGet `Viiper.Client` is the upstream build without the audio streams the
  fork adds. A small client of our own (management requests and streams over TCP) avoids mixing two VIIPER versions
  and keeps all VIIPER code in a separate process (GPL-3.0 core) behind TCP.
- **Decision: a pad kind.** `PadKind.DualSense` is a third choice on the Emulation page ("DualSense (needs VIIPER)").
  - A composite backend routes Xbox 360 and DS4 to ViGEm, and DualSense to VIIPER.
  - The existing pipeline (PC control, gyro, trigger range) still shapes the input.
- **Decision: input from the snapshot.**
  - `UsbInputReport.Build(InputSnapshot)` writes the USB report 0x01 layout, round-trip tested against the parser.
  - This keeps the gyro/PC-control/range processing, instead of passing raw bytes through.
- **Decision: the game's output block passes through.**
  - `OutputState.GameBlock` (47 bytes) replaces the composed common block while set, so the game's own flags
    (haptics mode, audio path, triggers, lights) reach the controller exactly.
  - DualPulse's lighting/trigger settings apply until the game sends its first output, and again after unbinding.
- **Decision: game audio.**
  - Haptics from `audio/haptics` go through `HapticsAudioConverter` (Natural, intensity 1) into the session mixer.
  - Speaker audio from `audio` (channels 1–2) goes into the session's speaker stream.
  - Speaker vs headset follows the game's audio control byte.
- **Decision: DualPulse's own audio features stand down** (system-audio haptics, controller audio) while the
  DualSense pad is active, so they don't mix into the game's stream.
- **Decision: USB connections** forward input and the output block only. Game audio to a USB-connected controller is
  out of scope; over USB the game can use the real controller directly.

## Build order and tests

1. `OutputState.GameBlock` passthrough in the 0x31 builder (both transports), and the headset flag from the game's
   audio control byte.
2. `UsbInputReport.Build`, round-trip with `DualSenseInputParser`.
3. VIIPER client (Emulation): request framing and JSON parsing, and stream path writing and frame reading, against
   an in-test TCP listener.
4. `ViiperBackend` and `ViiperDualSensePad : IDualSensePad` (input writer; output, haptics and speaker readers), and
   `CompositePadBackend`.
5. EmulationService wiring for `IDualSensePad` (fake pad), with the audio services standing down. Emulation page
   choice.
6. Probe `viiper-dualsense` (creates the device, prints its output blocks); hardware check with a game; acceptance
   doc.

## Addendum (2026-10-24): in-house device replaces VIIPER

- **Why:** VIIPER was an extra program to install and keep running. Its build also dropped the device as soon as game
  audio flowed. The device itself is small: descriptors, a few control requests, four endpoints.
- **What:** `VirtualDualSense` (Core) answers the USB requests from descriptors captured from a real controller.
  `UsbIpServer` serves it over USB/IP on a loopback port. `UsbIpDualSensePad` (Emulation) is the `IDualSensePad`, and
  `UsbIpWinAttacher` plugs it in through usbip-win2's CLI. Only the usbip-win2 driver remains as an install.
- **Decisions:** the device is exported at full speed with `bInterval 1` on every endpoint. Isochronous transfers run
  on a 1 ms frame clock and return their start frames. The VIIPER client, pad and probe command were removed.
- **Open:** a Linux attacher (vhci-hcd's `usbip attach`, needs root). Until it exists, other platforms have no virtual
  pads.
