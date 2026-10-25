# Bluetooth haptics: rumble → haptics (Phase B1)

Date: 2026-10-23. Status: approved in chat. Feasibility proven by the `haptics-tone` probe (felt on the user's
controller). This spec also carries the build order.

## Goal

Over Bluetooth, game rumble plays as DualSense haptics: the heavy (left) motor becomes a low-frequency vibration on
the left actuator, the light (right) motor a higher one on the right. Strength and mute apply as today. USB is
unchanged. Speaker/headset audio (B2) and system-audio haptics are separate later work.

## Protocol facts (from the probe and the sources below)

- Report 0x32, 142 bytes, written at the device's full output length (547 on Windows Bluetooth, zero padded).
  - Byte 0: 0x32. Byte 1: sequence in the high nibble.
  - Bytes 2-10: block 0x11: `0x91 0x07`, then `0xFE 00 00 00 00`, the buffer depth, then a counter.
  - Bytes 11-76: block 0x12: `0x92 0x40`, then 32 frames of signed 8-bit stereo (left, right) at 3 kHz.
  - Bytes 138-141: CRC-32 over `0xA2` + bytes 0-137, little-endian.
- One report per 10.67 ms. The counter restarts at 0 for each stream. About 250 ms of silence after an effect,
  otherwise short effects are cut off or keep buzzing.
- Buffer depth: about 50 ms at `0x20`, 25 ms at `0x08`; `0x04` plays nothing. Start at `0x20`.
- Haptics mode in report 0x31:
  - valid_flag0 has no rumble bits (0x01 compatible vibration, 0x02 haptics select); the trigger bits stay.
  - valid_flag2 has no compatible-vibration bit (0x04).
  - valid_flag1 adds 0x02 (power-save control), 0x20 (haptics low-pass filter) and 0x40 (motor power reduction).
  - Power-save byte (common 9) = 0 and motor power reduction (common 36) = 0.
  - Without this nothing is felt.
- Sources (facts only, no code copied): SAxense (Egor Vorontsov, MPL-2.0), dualsense-bridge README, AnyPS5 issue
  #2056. Credited in THIRD-PARTY-NOTICES.

## Components

- Core `OutputState.HapticsMode` (default false); `OutputReportBuilder` writes the haptics-mode bits above when set,
  motors 0.
- Core `HapticsReport.Build(Span<byte> destination, byte sequence, byte counter, byte bufferDepth,
  ReadOnlySpan<sbyte> left, ReadOnlySpan<sbyte> right) -> int` (142).
- Core `RumbleWaveform`: `Fill(byte large, byte small, Span<sbyte> left, Span<sbyte> right)` writes 32 frames: a
  70 Hz sine on the left with amplitude large/255 × 127, and a 160 Hz sine on the right with amplitude
  small/255 × 127. The phase carries over between calls (no clicks).
- Core `HapticsStream`: decides per tick whether to send and builds the report.
  - `bool Next(byte large, byte small, TimeSpan now, Span<byte> report)`.
  - Sends while rumble is non-zero and for 250 ms after it stops (silence).
  - A new stream after a pause starts with counter 0.
- `ControllerSession`:
  - `SetHapticRumble(byte large, byte small)` (Bluetooth only).
  - A haptics thread wakes on a signal and ticks every 10.67 ms with 1 ms timer resolution while `HapticsStream`
    is active, then sleeps.
  - Writes from the writer and the haptics thread go through one lock.
- App `RumbleSettings.BluetoothHaptics` (default true). `RumbleService`: on Bluetooth with the setting on, game rumble
  and the test buttons go to `SetHapticRumble` and the state gets `HapticsMode = true` (motors 0); otherwise motors
  as today and `HapticsMode = false`.
- Rumble page: "Over Bluetooth" row, Haptics / Classic.

## Build order and tests

1. `HapticsMode` bits in the 0x31 builder (flags, power bytes, motors, trigger bits kept).
2. `HapticsReport` (layout, sequence, counter, depth, samples, CRC position and value).
3. `RumbleWaveform` (silence for 0/0, amplitude scaling, channel separation, phase continuity across calls).
4. `HapticsStream` (no report while idle, reports while rumbling, 250 ms tail, counter restart) and session
   streaming (fake device on Bluetooth: 0x32 writes appear while rumbling and stop after the tail).
5. `RumbleService` routing (Bluetooth + setting → haptics, USB or Classic → motors) and the setting.
6. Rumble page row (view model test, view).
7. Notices, hardware check by the user, acceptance doc, TODO.
