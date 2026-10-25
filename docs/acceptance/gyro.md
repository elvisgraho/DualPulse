# Phase M (gyro aiming) acceptance

Hardware: DualSense over USB, Xbox 360 emulation. Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Gyro chip on the Controller page; settings persist | Pass |
| Mouse mode, Always: tilting up/down moves the pointer the right way | Pass |
| Mouse mode, Always: turning left/right moves the pointer the right way and fully | Pass (after fix) |
| Diagonal moves stay on the intended line | Pass (after fix) |
| Stick mode, While L2: aims in a game, stops when L2 is let go | Open (no game check yet) |
| Calibrate removes resting drift | Open |

Findings during the check: left/right barely moved, because only the controller's own yaw sensor was read and a
tilted grip or wrist-steering lands mostly on roll. Turning is now measured around the real up direction from the
accelerometer (player space, with some roll counted towards turning). Diagonals then felt loose: up was relearned
mid-move from an accelerometer disturbed by hand movement, bleeding pitch into turn; up is now only learned while
the controller is nearly still. 1x sensitivity felt sluggish on small movements, so the default is now 2x.
