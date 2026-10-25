# Phase L (LEDs and mic) acceptance

Spec: `docs/superpowers/specs/2026-10-23-leds-and-mic-design.md`. Hardware: DualSense over USB and Bluetooth.
Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Mute button: mute LED lights and the Windows microphone mutes; second press undoes both | Pass |
| Windows mute from another process reaches DualPulse (probe `mic watch` + `mic toggle`) | Pass |
| Battery level pattern at 100%: all five LEDs, same as "All" | Pass |
| Battery bands, charging blink, low-battery flash (unit tests with a fake clock) | Pass |
| Lower battery bands and the low-battery flash on the controller | Open (needs a lower battery) |

Found during the check: the lightbar sometimes stayed the controller's own blue after plugging or unplugging the
cable. The controller ignores the first output report while it settles after a USB/Bluetooth switch, and output was
only sent on change. The session now re-sends its output at 0.5, 1.5 and 2.5 s after connecting; confirmed by the
user (blue for about a second, then the chosen colour). Also found by a test: before the first input report the
battery read as empty and would have flashed the LEDs at every connect; the flash now waits for a real reading.

Known limit: switching the Windows default recording device elsewhere is picked up at the next Mute press.
