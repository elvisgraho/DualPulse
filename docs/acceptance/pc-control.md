# Phase M (PC control) acceptance

Hardware: DualSense over USB, Xbox 360 emulation. Date: 2026-10-23.

| Check | Result |
| --- | --- |
| PS + Options switches PC control on and off; the virtual pad pauses while it is on | Pass |
| Touchpad: move, tap to click, two-finger tap and scroll, press to drag | Pass |
| Touchpad press: right half right-clicks, left half left-clicks; Home drawing lights the pressed half | Pass (after fix) |
| Right stick moves the pointer, left stick scrolls | Pass |
| Buttons follow the table (Cross Enter, Circle Escape, D-pad arrows, R2 click) | Pass |
| Hold L1: window switcher opens at once; stick/D-pad/R1/L2 move; let go switches; tap returns to the last window | Pass |
| Hold R1: volume, tracks, play/pause, mute | Pass |

Findings during the check: the first build left-clicked anywhere on the pad, and L1 was a plain Alt key (the
switcher only opened with R1). Both were reworked: press zones like a laptop touchpad, and hold layers modelled on
Steam Input's mode shifts.

Known limit: Windows doesn't let input reach windows of programs running as administrator unless DualPulse runs
elevated. PS also reaches the virtual pad as the Xbox button before Options completes the switch.
