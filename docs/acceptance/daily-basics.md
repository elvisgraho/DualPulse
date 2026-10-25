# Phase D (daily-driver basics) acceptance

Hardware: DualSense over USB. Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Tray menu shows connection and battery ("USB · 70% (charging)") | Pass |
| Tray "Emulation" check switches to native and back to the previous controller type | Pass |
| Tray "Turn controller off" is greyed out over USB | Pass |
| Settings "Start with Windows" writes `HKCU\...\Run\DualPulse` = `"<exe>" --minimized` | Pass (registry checked) |
| Low-battery pop-up appears bottom-right, doesn't take focus, closes after 6 s | Pass (checked with `--preview-notification`) |

Not yet checked by hand: starting at an actual sign-in, and the pop-up at a real 20% / 10% battery (covered by
`BatteryAlertServiceTests`). The Run entry points at the current build folder; it updates itself if the exe moves.
