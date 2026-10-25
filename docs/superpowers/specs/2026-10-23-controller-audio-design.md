# Controller speaker and headset audio over Bluetooth (Phase B2)

Date: 2026-10-23. Status: approved in chat. Feasibility proven by the `speaker-tone` probe: a steady tone on the
speaker and through wired earbuds. This spec also carries the build order.

## Goal

Over Bluetooth, the sound of one chosen Windows output device plays on the controller: on the speaker, or on
earbuds in the jack. Game rumble and system-audio haptics keep working. Over USB the controller is already a Windows
sound device, so nothing is needed there.

## Protocol facts (probe-verified)

- Report 0x39, 547 bytes, one per 21.33 ms.
  - Byte 1: sequence nibble.
  - Bytes 2-9: `0x91 0x06`, then the flag mask `0x7E`, four buffer-length bytes (100), and a counter that rises
    by 2 per report.
  - Bytes 10-139: `0xD2 0x40`, then 2 × 64 bytes of haptics (two 32-frame stereo int8 blocks).
  - Bytes 140-541: `0xD3` (speaker) or `0xD6` (headset), `200`, then 2 × 200-byte Opus frames.
  - Byte 542: 0. Bytes 543-546: CRC-32 over `0xA2` + bytes 0-542.
- Opus:
  - 48 kHz stereo, 10 ms frames (480 samples), CBR 160 kbit/s (exactly 200 bytes), restricted low-delay.
  - The controller consumes 960 samples per 21.33 ms (45 kHz), so captured audio is resampled to 45 kHz and
    labelled 48 kHz.
  - Three silent reports prime it.
- Report 0x31:
  - valid_flag0 gains 0x80 (audio control) plus 0x20 (speaker volume, common byte 5) or 0x10 (headphone volume,
    common byte 4).
  - Audio control (common byte 7): 0x30 for the speaker, 0x00 for the headset.
  - With flag mask 0xFE the speaker played in bursts.
- Input: the status byte after the battery byte (common byte 53), bit 0 = headphones plugged.
- Sources (facts only): ds5bridge protocol notes, DS5Dongle (MPL-2.0), KytyPS5 PR #148, Linux hid-playstation.

## Components

- Core:
  - `InputSnapshot.HeadphonesPlugged`, parsed from the status byte.
  - `OutputState.AudioOutput` (`Off`, `Speaker`, `Headset`) and `AudioVolume` (0-100); the 0x31 builder writes the
    audio bits.
    - Speaker volume = volume × 0x64 / 100.
    - Headphone volume = volume × 0x7F / 100.
  - `AudioReport.Build(...)`: the 0x39 layout above.
  - `SpeakerStream` (thread-safe):
    - `Push(ReadOnlySpan<float> interleaved, int sampleRate, int channels)` resamples to 45 kHz stereo into a
      buffer capped at 6 reports (oldest dropped).
    - `bool Next(Span<byte> opus400)` encodes two 480-sample frames once a two-report head start is buffered,
      and silence on underrun.
    - `IsActive` turns false after 1 s without sound.
    - Opus through Concentus (BSD).
  - `ControllerSession`:
    - `Speaker` (a `SpeakerStream`) and `WakeAudio()`.
    - The haptics loop sends 0x39 every 21.33 ms while the speaker is active, with two haptic blocks from
      `HapticMixer`, and primes with three silent reports at each start.
    - Otherwise it sends 0x32 as before.
- Capture:
  - `ISystemAudioSourceFactory` with `IReadOnlyList<AudioDevice> OutputDevices()` and
    `ISystemAudioSource Create(string? deviceId)`; null means the default output.
  - Windows implementation: `WasapiLoopbackSourceFactory` (NAudio MMDeviceEnumerator).
- App:
  - `ControllerAudioSettings(bool Enabled = false, string? DeviceId = null, ControllerAudioOutput Output = Auto,
    int Volume = 70)`.
  - `ControllerAudioService`:
    - On Bluetooth with Enabled, it captures the chosen device into the session's speaker stream.
    - It sets `AudioOutput` from Output (Auto follows `HeadphonesPlugged`) and `AudioVolume`.
    - Otherwise it stops and sets `AudioOutput` to Off.
- UI: an Audio chip on the Controller page with:
  - an enable switch;
  - a source dropdown (Default output first, then devices, with a refresh button);
  - Output (Auto / Speaker / Earbuds) and Volume;
  - a hint about per-app output in Windows' Volume mixer, and a USB note.

## Build order and tests

1. Parser jack bit; 0x31 audio bits (speaker, headset, off, volume scaling).
2. `AudioReport` layout, counter and CRC.
3. `SpeakerStream`:
   - resampled frame count per second;
   - head start before the first frame, silence on underrun, buffer cap;
   - inactive after 1 s of silence;
   - Opus frames exactly 200 bytes.
4. Session: 0x39 while the speaker is active (two haptic blocks inside, three silent primes), back to 0x32 when it
   stops.
5. Capture factory and device list (Windows), with probe `audio-devices`.
6. Service with fakes (Bluetooth + enabled starts; Auto follows the jack; off or USB stops); settings; Audio chip
   view model and view.
7. Notices (Concentus), hardware check, acceptance doc, TODO.
