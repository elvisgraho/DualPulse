# R5 Rumble page acceptance

Hardware: DualSense, firmware 06.30, over USB, Xbox 360 emulation. Steam running (automated tests). Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Test left: heavy left grip, Left meter moves | Pass |
| Test right: lighter right grip, Right meter moves | Pass |
| Test both: both motors | Pass |
| Strength 30%: test noticeably weaker; 200%: stronger than 100% | Pass (after making tests follow Strength; see note) |
| Mute on: game rumble via XInput (`DualPulse.Probe xinput-rumble`) does nothing | Pass |
| Mute off: game rumble left first, then right | Pass |
| Rumble mode shows "Automatic uses Improved on firmware 06.30" | Pass (screenshot) |

Note: the first build's Test buttons used a fixed level and ignored Strength, which was confusing when trying the
slider. They now follow Strength (not Mute, so testing works while game rumble is muted). At 200% the test pulse is
capped at the motors' maximum, about 25% above the 100% pulse.
