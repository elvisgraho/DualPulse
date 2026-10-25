# Phase B1 (Bluetooth rumble → haptics) acceptance

Spec: `docs/superpowers/specs/2026-10-23-bluetooth-haptics-design.md`. Hardware: DualSense over Bluetooth, Windows 11.
Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Feasibility probe `haptics-tone`: tone felt left then right (after the haptics-mode setup and signed samples) | Pass |
| Rumble page Test left: deep, low; Test right: higher frequency; meters move | Pass |
| Haptics vs Classic over Bluetooth: clearly different, haptics finer | Pass |
| USB unchanged (unit test: motors, no haptics mode) | Pass |
| Packet layout, CRC, waveform, stream tail and counter restart (unit tests) | Pass |
| Game rumble over Bluetooth in a real game | Open |
| Sound to haptics: system audio (music, video) felt through the grips over Bluetooth | Pass |
| Loopback capture on Windows (probe `audio-level`: 48 kHz stereo, levels follow playback) | Pass |
| Converter, mixer and service routing (unit tests) | Pass |
| Punchy style on music: better than Natural; soft intro (Rammstein, Deutschland) stays soft after the boost cap | Pass |
| Stereo panning felt left and right (linked automatic level) | Pass |

Found during the probe: nothing is felt unless report 0x31 leaves the rumble bits off and enables power-save control,
the haptics low-pass filter and motor power reduction (all at 0); samples are signed 8-bit.

Spec for the audio source: `docs/superpowers/specs/2026-10-23-audio-haptics-design.md`.

Later work: buffer depth (latency) tuning if games feel late; music-focused processing; speaker and headset audio (B2).
