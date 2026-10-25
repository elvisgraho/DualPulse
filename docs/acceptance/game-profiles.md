# Phase P (game profiles) acceptance

Spec: `docs/superpowers/specs/2026-10-23-game-profiles-design.md`. Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Foreground watcher reports each focus change (probe `foreground`: DualPulse, Explorer, Claude) | Pass |
| Window list shows real programs with readable exe names (probe `windows`) | Pass |
| Add game: picker lists running programs; picking one creates and selects its profile | Pass (UI Automation) |
| Selected profile: strip "Follows your default…", controls dimmed and read-only | Pass (UI Automation) |
| Remove deletes the profile and returns to Default | Pass (UI Automation) |
| Profile activation, own-window rule, merge, edits while active (unit tests) | Pass |
| Trigger range: deadzone and start point shape L2/R2 sent to the virtual pad (unit tests) | Pass |
| Real game: triggers/gyro switch on focus, back on alt-tab; DualPulse's window keeps the profile | Pass (user) |
| Trigger range felt in a racing or shooter game | Open (hardware) |

Findings during the check: the first selector replaced its item list on every change, which made the dropdown
re-select its old item and undo the new selection; the list is now updated in place and entries carry only name
and exe. The profile row overlapped the section chips at the default window width and moved to its own line.

Review fixes: the deadzone now also holds back the digital L2/R2 buttons a DS4 pad sends; the foreground watcher
can't crash the app if a listener throws, and re-reads focus right after its hook goes in.

Known limits: Store (UWP) apps all appear as `ApplicationFrameHost.exe`, so they share one profile. Choosing a preset
links the triggers again, so a separate R2 range stops applying until they're unlinked. A rare race between a
settings edit and a focus change can apply the previous effective settings until the next change.
