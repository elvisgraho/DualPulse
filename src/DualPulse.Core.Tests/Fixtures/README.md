# Input report fixtures

Real input reports from a DualSense (firmware 06.30, hardware 0x0811), captured on 2026-10-23 with
`tools/DualPulse.Probe capture-all` while the controller was connected over USB and Bluetooth at the same time.
Each `.hex` file is one report: uppercase hex bytes separated by spaces. `-usb` files are report `0x01` (64 bytes),
`-bt` files are report `0x31` (78 bytes).

| Fixture | Input held at capture |
|---|---|
| `idle` | nothing pressed, sticks centred |
| `cross` | Cross |
| `dpad-upright` | D-pad up + right |
| `ps-create-options` | PS + Create + Options |
| `l2-full` | L2 fully pressed |
| `left-stick-upleft` | left stick pushed into the up-left corner (circular gate: about 37 per axis) |
| `touch-one` | one finger on the left half of the touchpad |

Battery status byte in every capture: `0x16` (charging over the USB cable, capacity 6 = 60%).
