# DualSense report audit (Phase 1)

Compared against Linux `drivers/hid/hid-playstation.c` (kernel.org, fetched 2026-10-23) and SDL's
`SDL_hidapi_ps5.c` (current main and release-2.0.20 / 2.26.0 / 2.30.0). Both are used as references only;
no code was copied. Hardware check: DualSense (PID 0x0CE6) over Bluetooth on Windows 11, read with HidSharp.

## Input reports: correct

| Field | USB (0x01, 64 B) | Bluetooth (0x31, 78 B) | App |
|---|---|---|---|
| Sticks LX/LY/RX/RY | 1-4 | 2-5 | ✅ |
| L2/R2 analog | 5-6 | 6-7 | ✅ |
| Buttons (3 bytes) | 8-10 | 9-11 | ✅ |
| Gyro / accel (6 x le16) | 16 / 22 | 17 / 23 | ✅ |
| Sensor timestamp (le32) | 28 | 29 | ✅ |
| Touch points (2 x 4 B) | 33 | 34 | ✅ |
| Status[0] battery/charging | 53 | 54 | ✅ offset; ❌ decoding (fixed) |
| CRC-32 (seed 0xA1) | - | 74-77 | not checked by the app |

Hardware: the Bluetooth reports arrive as 78-byte 0x31 reports with a valid 0xA1-seeded CRC. Byte 1 carries a
sequence counter in its high nibble.

## Output report 0x31 (Bluetooth): valid, older convention

The app writes `[0]=0x31, [1]=0x02` and starts the common block at byte **2**. That is the layout SDL 2.x used up to
and including 2.30 (2024). Linux and SDL3 use `[1]=seq<<4 | tag 0`, `[2]=0x10` with the common block at byte **3**.
Both are accepted by the controller. The app's block offsets are internally consistent with its convention:

| Common field | Offset in common | App BT index |
|---|---|---|
| valid_flag0 / valid_flag1 | 0 / 1 | 2 / 3 (0x0F / 0x55) |
| motor right / left | 2 / 3 | 4 / 5 |
| mute LED | 8 | 10 |
| right / left trigger effect (11 B each) | 10 / 21 | 12 / 23 |
| motor power reduction | 36 | 38 |
| valid_flag2 | 38 | 40 |
| lightbar setup (2 = fade out boot light) | 41 | 43 |
| player LED brightness / mask | 42 / 43 | 44 / 45 |
| lightbar RGB | 44-46 | 46-48 |
| CRC-32 (seed 0xA2) | - | 74-77 ✅ (unit-tested) |

Windows reports a 547-byte max output length for the Bluetooth device; the app allocates that, as `WriteFile` requires.

**Verified 2026-10-23 (DualPulse probe):** this controller (firmware 06.30) accepts both forms over Bluetooth, sequenced (red) and legacy (green), and the USB report (blue). DualPulse uses the sequenced form by default.

**Planned:** move to the sequenced form (`seq<<4`, `0x10`, common at 3) together with Phase 2, because the
haptics/audio reports (0x32, 0x34-0x39) use sequence tags too.

## Bugs found

1. **Charging state latched** over Bluetooth: set on state 1, never cleared. Fixed.
2. **Full (state 2) and error states ignored.** Fixed.
3. **USB hard-coded `isCharging = true`.** Fixed (USB carries the same status byte).
4. **DS4 emulation battery byte** was the raw DualSense status byte divided by 11. Now converted to DS4 format
   (level 0-10, bit 4 = on power).
5. **Read loop opens a new HID handle per report** (`HidDevice.Open()` every iteration, plus a second 200 ms
   touchpad thread doing the same, and every write opens a handle). Adds latency and can drop reports.
   → Phase 1 "low-latency input loop" item.
6. **Dead code:** `RefreshButtonStatus` in the USB/BT classes discards every value and uses wrong motion offsets
   (12/18); harmless, remove when splitting the classes.
7. **DS4 Bluetooth output** (`SendDS4HapticsBT`, added by the remix author) writes a 78-byte buffer. If Windows
   reports a larger max output length for a DS4 over Bluetooth, as it does for the DualSense, the write fails
   silently. Untested: no DS4 available.

Battery level stays in steps of 10 (`capacity x 10`, capped at 100) because the UI selects icons by exact values;
Linux reports `capacity x 10 + 5`.
