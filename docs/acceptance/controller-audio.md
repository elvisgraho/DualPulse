# Phase B2 (controller speaker and headset audio over Bluetooth) acceptance

Spec: `docs/superpowers/specs/2026-10-23-controller-audio-design.md`. Hardware: DualSense over Bluetooth, wired
earbuds, Windows 11. Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Probe `speaker-tone`: steady tone on the speaker (after fixing the flag mask to 0x7E) | Pass |
| Probe `speaker-tone --headset`: steady tone in wired earbuds | Pass |
| Audio chip: PC sound (default output) plays on the controller speaker | Pass |
| Volume slider audible across its range (after mapping onto 0x3D-0x64) | Pass |
| Auto: plugging earbuds moves the sound to them; unplugging moves it back | Pass |
| Report layout, speaker stream, audio lane switching, service routing (unit tests) | Pass |
| Per-game routing through an unused output (Volume mixer → e.g. Playback 7/8) | Open |
| Earbud volume curve across the slider | Open (linear 0-0x7F, not yet judged) |

Found during the check: with flag mask 0xFE the speaker played in bursts (the mask announced a field that wasn't
there); speaker volume bytes below about 0x3D are silent, so the slider's lower half did nothing until remapped.
