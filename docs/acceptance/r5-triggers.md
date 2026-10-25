# R5 Triggers page acceptance

Hardware: DualSense, firmware 06.30, over USB. Steam running (automated tests). Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Travel bars follow L2 and R2 | Pass |
| Feedback preset: steady resistance from about a third of the travel | Pass |
| Weapon preset: a click that gives way | Pass |
| Vibration preset: buzz past the start zone | Pass |
| Separate triggers: two editors, each starting from the shared effect | Pass |
| L2 Resistance, strength 8 from zone 0: stiff from the top | Pass |
| R2 Vibration: frequency slider changes the buzz speed | Pass |
| Home shows "Custom" after editing; choosing Off there frees both triggers | Pass |

The travel bars sample about 30 times a second only while the page is visible, and redraw only when a trigger moves
by 3 or more steps.
