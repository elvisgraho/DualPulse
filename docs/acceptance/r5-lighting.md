# R5 Lighting page acceptance

Hardware: DualSense, firmware 06.30, over USB. Steam running (automated tests). Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Swatches set the lightbar; preview strip and hex field follow | Pass (after re-plugging; see note) |
| Hue slider moves the lightbar through the colours | Pass |
| Brightness dims the lightbar and keeps the colour | Pass |
| Breathing: slow fade, never fully dark | Pass |
| Rainbow: cycles all colours | Pass |
| Battery: colour follows the battery level (about 70%, yellow-green) | Pass |
| Player LEDs: All, then Low brightness | Pass |
| Mic LED: Pulse, then Off | Pass |

Note: before the checks, colour changes didn't reach the controller over USB while Windows listed it on USB and
Bluetooth at once (input still flowing, lightbar pulsing). Re-plugging fixed it. Cause not yet known; DualPulse now
logs rejected writes, and the episode is tracked as a "Watch" item in `TODO.md`.

Static mode sends nothing while idle (`Lighting_Static_SendsNothingWhileIdle`). Breathing and Rainbow update about
30 times a second, Battery once a second, and only write when the colour changes.
