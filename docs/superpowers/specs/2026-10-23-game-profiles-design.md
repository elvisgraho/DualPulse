# Game profiles (Phase P)

Date: 2026-10-23. Status: approved in chat, awaiting spec review.

## Goal

Per-game trigger feel and gyro that switch on by themselves while the game has focus and switch back the moment it
doesn't, without the user touching DualPulse. Plus trigger deadzones, usable in the default settings and in profiles.

## Decisions (from the design conversation)

- A profile can change **Triggers** and **Gyro** only. Lighting, rumble, emulation and mapping stay global.
- A game is added by **picking a running program**; matching is by exe file name, case-insensitive.
- A profile applies **while the game's window has focus**. Any other window brings the normal settings back, except
  **DualPulse's own window**, which keeps whatever was applied (so the user can tweak while playing).
- Profiles are applied through a **live effective-settings layer**. The saved settings are never rewritten on a
  switch, so a crash can't leave a game's values as the defaults.
- Precedence: **mod override > active profile > saved settings** (mods already override triggers per side).

## Data

```csharp
public sealed record GameProfile(string Name, string Exe, TriggerSettings? Triggers = null, GyroSettings? Gyro = null);
```

- `AppSettings` gains `IReadOnlyList<GameProfile>? Profiles` and `EffectiveProfiles` (empty when null).
- A part set to null follows the saved settings.
- Gyro calibration offsets always come from the saved settings: the merge keeps the saved `OffsetX/Y/Z`, so a profile
  never carries a stale calibration.
- `TriggerEffectSettings` gains `Deadzone` and `StartAt` (0–0.3 each, default 0). Old settings files load with 0.

## Components

**Core (`DualPulse.Core.Mapping`)**
- `TriggerRange.Apply(byte value, double deadzone, double startAt) -> byte`: values at or below the deadzone give 0;
  above it the output runs linearly from `startAt` to 255. Deadzone 0 and startAt 0 is the identity.

**Emulation (`DualPulse.Emulation`)**
- `IForegroundWatcher { string? ForegroundExe { get; } event Action<string?> Changed; }`
- `ForegroundWatcher`: `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` on its own thread with a message loop; resolves the
  exe with `GetWindowThreadProcessId` + `QueryFullProcessImageName`. No polling.
- `IRunningWindows { IReadOnlyList<RunningWindow> List(); }` with `RunningWindow(string Title, string Exe)`: visible,
  titled top-level windows via `EnumWindows`, excluding DualPulse itself, one entry per exe.

**App (`DualPulse.App.Services`)**
- `IProfileService`:
  - `GameProfile? Active` (the profile applied now) and `AppSettings Effective` (saved settings with the active
    profile's parts laid on top).
  - `event Action? Changed` (raised when Active changes or the saved settings change).
- `ProfileService` listens to the watcher and settings.
  - A foreground exe matching a profile makes it active; any other exe clears it; DualPulse's own exe changes nothing.
  - Removing or editing the active profile updates `Effective` at once.
- `ControllerService` (triggers) and `GyroService` read `IProfileService.Effective` instead of the saved settings.
  `EmulationService` applies `TriggerRange` to L2/R2 from the effective triggers before submitting to the pad.

**App (view models and views)**
- `ProfileSelection`: shared object holding the selected profile (null = Default). It follows `IProfileService.Active`
  whenever a profile becomes active.
- Controller page header: profile selector (Default, profiles, "Add game profile…"), remove per profile, and
  "● Active now" when the selected profile is the applied one.
- `TriggersViewModel` and `GyroViewModel` edit the selection's target:
  - Default: the saved settings, as today.
  - Profile with that part set: the profile. The strip says "<Name> uses its own triggers" with **Use default**
    (sets the part back to null).
  - Profile without that part: controls show the default values, read-only. The strip says "Follows your default
    triggers" with **Customize for this game**, which copies the current default into the profile.
- Lighting and Rumble show "Same for all games" while a profile is selected.
- Add game dialog: lists `IRunningWindows`. Picking one creates `GameProfile(title, exe)` and selects it. If that
  exe already has a profile, it selects the existing one instead.
- Triggers page: new **TRIGGER RANGE** card with Deadzone and Starts at sliders (per side; one pair while linked).

## Error handling

- Foreground exe can't be resolved (protected or elevated process): treated as "no game", so normal settings apply.
- Watcher thread fails to start: logged; profiles never activate, and everything else works.
- Duplicate exe in settings (hand-edited file): the first matching profile wins.

## Testing

- Core: `TriggerRange` (identity, deadzone, startAt, both, clamping).
- App, with a fake watcher, settings and running-windows list:
  - which profile is active for each foreground exe, including DualPulse's own window and an unknown exe;
  - the merge (parts null vs set; gyro offsets kept from saved);
  - `Changed` firing; the active profile being removed;
  - the emulation pad receiving range-shaped L2/R2;
  - view models: selection follows Active; customize, use default and the read-only state; adding a duplicate exe.
- Hardware, by the user later: a real game switches on focus and back on alt-tab; DualPulse's window keeps it.

## Out of scope

- Profiles for lighting, rumble, emulation or mapping; browsing for an exe; Steam library detection; per-profile
  hotkeys; importing or exporting profiles.
