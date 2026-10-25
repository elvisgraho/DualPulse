# LEDs and mic (Phase L)

Date: 2026-10-23. Status: approved in chat. This spec also carries the build order (small enough not to need a
separate plan).

## Goal

The player LEDs can show the battery level and flash when it's low; the Mute button mutes the Windows microphone
and the mute LED always shows whether the microphone is muted.

## Player-LED battery indicator

- `PlayerLedPattern` gains `Battery`. Bits fill from the left (bit 0 = leftmost): 1 LED up to 20%, 2 up to 40%,
  3 up to 60%, 4 up to 80%, 5 above.
- Charging: the next LED up (if any) blinks, 1 s on / 1 s off.
- Low battery: with `LightingSettings.LowBatteryFlash` (default true), level ≤ 10% and not charging, the player
  LEDs (whatever the pattern; all five when the pattern is Off) flash 0.5 s on / 0.5 s off.
- Mods that control the player LEDs still win.
- `LightingEngine.PlayerLedBits(PlayerLedPattern pattern, int level, bool charging, bool lowFlash, TimeSpan elapsed)`
  is pure; `LightingService` ticks every 500 ms while the pattern is Battery or the flash is enabled (Apply already
  skips unchanged output).

## Mute button and microphone

- `IMicrophone` (Emulation): `bool? IsMuted` (null = no recording device), `bool Toggle()` (false when there is no
  device), `event Action? Changed` (raised when Windows' mute state changes, from any source).
- `WindowsMicrophone`: Core Audio COM. `IMMDeviceEnumerator.GetDefaultAudioEndpoint(eCapture, eConsole)` →
  `IAudioEndpointVolume` `GetMute`/`SetMute` and `RegisterControlChangeNotify`. The default device is resolved again
  on each toggle (re-registering the callback if it changed).
- `MicMuteService` (App): while `LightingSettings.MuteButtonMutesMic` (default true), a Mute press (rising edge)
  calls `Toggle()`. `IsMicMuted` (bool?) and `Changed` feed the mute LED.
- `LightingService`: with the setting on, the mute LED is `On` when the microphone is muted and `Off` otherwise
  (and Off with no device); with it off, the saved `MicLed` choice applies as today. Mods still win.
- PC control: while the setting is on, the Mute button is not mapped to volume mute (volume mute stays on the R1
  media layer).

## Lighting page

- Player LEDs dropdown gains "Battery".
- "Flash player LEDs on low battery" switch.
- "Mute button mutes the microphone" switch; while on, the mute-LED choice is replaced by the caption "Shows whether
  your microphone is muted." and, with no recording device, "No microphone found."

## Error handling

- COM failures (no device, device unplugged, access denied) are caught: `IsMuted` becomes null, Toggle returns false,
  and the LED stays off. Nothing throws into the read thread.

## Known limits

- Switching the Windows default recording device elsewhere is picked up at the next Mute press, not instantly.

## Build order and tests

1. Core: `PlayerLedBits` for Battery (each band, charging blink on/off phase, low flash phases, flash with Off
   pattern, flash disabled, charging suppresses flash). Lighting settings fields; LightingService uses the new
   overload and ticks (test with fake clock: low battery flashes the LEDs).
2. Emulation: `IMicrophone`, `WindowsMicrophone`; probe command `mic` (prints state, `mic toggle` toggles twice).
3. App: `MicMuteService` with a fake microphone (rising edge toggles once; held button doesn't repeat; setting off
   ignores; Changed updates IsMicMuted). LightingService mute LED follows the microphone; PC control skips Mute
   while the setting is on.
4. Lighting page: view model properties and tests, view rows.
5. Hardware check by the user; acceptance doc; TODO.
