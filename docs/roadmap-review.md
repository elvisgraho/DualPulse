# Roadmap review: keep, later or drop

Test applied to each open item in `TODO.md`: can we name a concrete situation where someone uses it, and would they
notice if it were missing? Date: 2026-10-23. Nothing in `TODO.md` changes until the owner decides.

## Already done in DualPulse (TODO wording is out of date)

| Item | Where |
| --- | --- |
| DSX v2 + v3 mod compatibility (Phase 9) | Mods page |
| Sony's official trigger effect set (Phase 5) | Core; Triggers page shows the four everyday ones, mods can use all |
| Lightbar static / breathing / rainbow / battery (Phase 8) | Lighting page |
| HidHide integration (Phase 3) | Emulation page |

## Keep: clear use cases

| Item | Phase | Use case |
| --- | --- | --- |
| Start with Windows, toast notifications (low battery) | 11 | Daily driver: it has to run without opening it, and wireless needs a battery warning |
| Tray menu: battery, emulation switch, turn controller off | 11 | Quick actions without opening the window |
| Touchpad → mouse (tap click, two-finger scroll/right-click) | 6 | Controlling the PC from the couch |
| Stick → mouse / scroll | 6 | Same, plus menus in games without controller support |
| Button → keyboard/mouse (with hold) | 6 / R5 Mapping | Games without controller support; media keys |
| Gyro → mouse / right stick | 6 | Gyro aiming in shooters |
| Game profiles that switch automatically (foreground exe) | 7 | Different triggers/lighting/emulation per game, no manual switching |
| Trigger deadzones / anti-deadzone | 5 | Racing and shooters: the game reacts at the right point of travel |
| Player-LED battery indicator, low-battery flash | 8 | See the battery on the controller itself when wireless |
| Mute button toggles the Windows microphone | 8 | Headset users get a real mute button |
| Bluetooth haptics stream (0x32) | 2 | The DSX+ headline: haptics without a cable |
| Haptics from system audio | 2 | Feel games and music through the haptic motors |
| Rumble → haptics | 2 | Every game with rumble gets haptic feel over Bluetooth |
| Bluetooth speaker / headset audio (0x39, Opus) | 2 | Wireless headset jack and controller speaker |
| Virtual DualSense (needs VIIPER) | 3 | Games with native DualSense support get adaptive triggers and haptics wirelessly |
| Re-test reconnect after sleep; the lightbar "Watch" item | R | Reliability |
| Delete the legacy app; publish | R6/R7 | Required |

## Later: real but not yet asked for, or depends on something bigger

| Item | Phase | Why later |
| --- | --- | --- |
| Multiple controllers at once | 1 | Local multiplayer; big change, single controller today |
| DSU/CemuHook motion server | 6 | Useful for emulator players only |
| JSON profile import/export, profile hotkeys | 7 | After game profiles exist |
| Audio-to-haptics EQ presets (Games/Music/Movies) | 2 | Tune after the basic stream is in real use |
| Microphone over Bluetooth | 2 | Needs a virtual audio driver |
| Replace ViGEmBus with VIIPER | 3 | Only needed for the virtual DualSense; ViGEm works |
| DS4 lightbar passthrough from games | 8 | Few games set it |
| Dynamic rumble sync for vibration triggers; audio-to-rumble | 4/5 | Unclear benefit until haptics land |
| Fill the MAC address in the mod status reply | R5 | Mods only display it |

## Drop: novelty, duplicate or untestable

| Item | Phase | Why |
| --- | --- | --- |
| Split the legacy `Main.cs` | 1 | The legacy app is being deleted |
| Bluetooth polling-rate increase | 1 | No reliable method for the DualSense; we already measure about 730 Hz |
| Sound-wave generator, file playback as haptics sources | 2 | Test toys; a developer probe covers testing |
| Multi-haptics, button/trigger/stick "click" haptics | 4 | Novelty; nobody keeps them on |
| 20 haptic presets, 20 trigger presets per profile | 4/5 | Profiles cover this with far less UI |
| L2/R2 turbo, random turbo, triple-press, chords | 5/6 | Niche; conflicts with games' own input |
| Lightbar strobe, multi-colour cycle | 8 | Decoration; four modes cover real use |
| Settings backup/restore | 7 | Settings are one small JSON file |
| Command-line interface | 9 | No user need named |
| OSC output | 6 | No user need named |
| DualSense Edge features | 10 | No Edge controller to test with |

## Suggested order for what's kept

1. **Daily-driver basics** (small): start with Windows, low-battery toast, tray quick actions.
2. **Mapping page** (last R5 page): touchpad/stick → mouse, buttons → keys.
3. **Game profiles** that switch automatically.
4. **Bluetooth haptics and audio** (the big one), then the virtual DualSense.
