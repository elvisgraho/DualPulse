# System audio → haptics over Bluetooth (Phase B1, second source)

Date: 2026-10-23. Status: approved in chat. Builds on `2026-10-23-bluetooth-haptics-design.md`. This spec also
carries the build order.

## Goal

Over Bluetooth, what Windows plays is felt through the grips (left channel → left actuator, right → right), mixed
with game rumble. Off by default; intensity slider. USB is out of scope (there the controller's USB audio device
carries haptics).

## Portability (user requirement)

- Core holds the interface and all processing, with no OS APIs: `ISystemAudioSource`, `HapticsAudioConverter`,
  `HapticsMixer`.
- The capture implementation is per platform. Windows: `WasapiLoopbackSource` in DualPulse.Emulation, using NAudio
  (MIT). A Linux (PipeWire/PulseAudio) or macOS (Core Audio) source would be another implementation of the same
  interface.
- `TimerResolution` (Core) becomes a no-op off Windows.

## Components

- `ISystemAudioSource` (Core):
  - `int SampleRate`, `int Channels`, `void Start()`, `void Stop()`.
  - `event AudioBlock? Blocks`, where the delegate is `AudioBlock(ReadOnlySpan<float> interleaved)`. Raised on
    the capture thread.
- `HapticsAudioConverter` (Core): converts float audio at the source rate to 3 kHz signed 8-bit stereo.
  - Per channel: two-pole low-pass at 400 Hz, then decimation by averaging to 3 kHz (handles any source rate
    through a fractional step).
  - Applies the intensity gain (0–2) and a noise gate (|sample| below 1/127 after gain becomes 0).
  - Mono input feeds both channels.
  - `void Process(ReadOnlySpan<float> interleaved, HapticsMixer mixer)`.
- `HapticsMixer` (Core, thread-safe):
  - Holds rumble levels and a short frame buffer (capped at 6 reports = 192 frames; oldest dropped).
  - `Push(sbyte left, sbyte right)`, `SetRumble(byte large, byte small)`.
  - `bool Fill(Span<sbyte> left, Span<sbyte> right)` mixes 32 frames: audio frames (silence on underrun) plus the
    rumble waveform, clamped. It returns whether anything was non-silent.
- `HapticsStream` takes its frames from the mixer:
  - `Next(HapticsMixer mixer, TimeSpan now, Span<byte> report)`.
  - The tail rule is unchanged: active while Fill reports sound, then 250 ms of silence.
  - The session keeps `SetHapticRumble` (it sets the mixer's rumble) and adds `PushHapticAudio` through the mixer.
- `AudioHapticsService` (App):
  - Starts the source only while the feature is on and the controller is on Bluetooth with haptics allowed.
  - Feeds the converter into the session's mixer.
  - Puts the session in haptics mode while active.
- Settings: `RumbleSettings.AudioHaptics` (default false) and `AudioHapticsIntensity` (default 1.0, 0–2).
- Rumble page: a "SOUND TO HAPTICS" card with the switch and an Intensity slider.
- Probe `audio-level`: prints the loopback level for 5 s (manual check of capture).

## Build order and tests

1. Converter: a 100 Hz tone keeps most of its amplitude, a 1 kHz tone is cut below 25%; output count matches 3 kHz;
   gain; noise gate; mono to both channels.
2. Mixer: sum and clamp; underrun gives silence; buffer cap drops the oldest; Fill's activity result. The stream
   takes frames from the mixer (existing stream tests move to the mixer form).
3. Session: pushed audio streams 0x32 over Bluetooth and stops after the tail.
4. `TimerResolution` no-op off Windows; Windows `WasapiLoopbackSource` (NAudio) and the probe `audio-level`.
5. `AudioHapticsService` (fake source: on + Bluetooth starts and feeds; off, USB or disconnected stops), settings,
   Rumble page card (view model tests).
6. Notices (NAudio), hardware check by the user, acceptance doc, TODO.

## Addendum: Punchy style (approved in chat, same day)

- `HapticsAudioStyle` has two values; the setting `RumbleSettings.AudioHapticsStyle` defaults to Punchy and the
  Rumble page offers a Style choice.
  - **Natural** is the filtered audio as above.
  - **Punchy**, per channel at 3 kHz:
    - a 150 Hz band feeds a fast envelope (5 ms attack, 60 ms release) and a slow envelope that smooths the fast
      one (300 ms);
    - level = 0.5 × fast + 2 × max(0, fast − slow), for beat emphasis;
    - automatic level, linked across both sides (one peak, the louder side's) so panning survives:
      level × 0.9 ÷ max(peak, 0.3) (at most about 3× boost), where the peak decays over about
      20 s, so soft intros and calm passages stay softer; capped at 1;
    - a gate: below a fast envelope of 0.01 the output is silence;
    - the result is played on a 100 Hz carrier.
- Tests:
  - kick pattern: distinct pulses;
  - steady bass: settles below its onset;
  - quiet tracks come up moderately; a soft intro stays at most half the heavy part; a quiet passage after a loud
    one stays quiet;
  - 35 Hz input: comes out on the 100 Hz carrier;
  - near silence: silent.

