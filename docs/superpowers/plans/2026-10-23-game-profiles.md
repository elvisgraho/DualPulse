# Game Profiles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Per-game Triggers and Gyro that apply while the game has focus, plus trigger deadzones.

**Architecture:** A foreground watcher (Emulation) feeds `ProfileService` (App), which publishes effective settings
(saved settings + active profile). Controller, gyro and emulation services read the effective settings. The
Controller page gains a profile selector; Triggers and Gyro edit the selected target.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, xUnit v3, Win32 (SetWinEventHook, EnumWindows).

**Spec:** `docs/superpowers/specs/2026-10-23-game-profiles-design.md`

## Global Constraints

- Work on main, commit per task, never push. Tests build with `--artifacts-path <scratchpad>/artifacts`.
- TDD: each test is seen failing before its code exists.
- Precedence: mod override > active profile > saved settings. Saved settings are never rewritten by a switch.
- Exe matching is by file name, case-insensitive. DualPulse's own exe never changes the active profile.
- Gyro offsets always come from saved settings. `Deadzone` and `StartAt` are 0–0.3, default 0.
- Match the code style: tabs, one-line comments above types explaining why, `UiContext` for VM updates.

## Review Focus

1. Settings file from before this feature (no `Profiles`, no `Deadzone`/`StartAt`) loads unchanged → Task 1 and 2 tests.
2. Active profile edited or removed while its game has focus → effective settings update at once (Task 2 test).
3. Trigger values at 255 with StartAt > 0 must still reach 255; value 0 always stays 0 (Task 1 test).
4. Focus moves game → DualPulse → desktop: profile stays, then clears (Task 2 test).
5. A profile part set to null while the selector shows it: controls become read-only and show defaults (Task 6 test).

---

### Task 1: Trigger range (Core maths, settings, emulation)

**Files:** Create `src/DualPulse.Core/Mapping/TriggerRange.cs`, `src/DualPulse.Core.Tests/TriggerRangeTests.cs`.
Modify `src/DualPulse.App/Services/TriggerSettings.cs`, `src/DualPulse.App/Services/EmulationService.cs`,
`src/DualPulse.App.Tests/EmulationServiceTests.cs`, `src/DualPulse.App.Tests/TriggerSettingsTests.cs`.

**Produces:** `static byte TriggerRange.Apply(byte value, double deadzone, double startAt)`;
`TriggerEffectSettings` gains `double Deadzone = 0, double StartAt = 0` (appended parameters).

- [ ] Tests (`TriggerRangeTests`): `Identity_WhenBothZero` (0→0, 128→128, 255→255); `Deadzone_SwallowsTheStart`
  (deadzone 0.2: 51→0, 255→255, 153 ≈ 128 ±2); `StartAt_LiftsFirstTouch` (startAt 0.2: 0→0, 1 ≥ 51, 255→255);
  `Both` (deadzone 0.1, startAt 0.2: 25→0, 26 ≥ 51, 255→255); `OutOfRange_Clamped` (deadzone 2 behaves as 0.3).
- [ ] `TriggerSettingsTests.OldSettings_LoadWithNoRange`: JSON without the new fields deserialises with 0/0.
- [ ] `EmulationServiceTests.Emulation_TriggerRange_ShapesL2`: saved Left `Deadzone 0.2`, Linked; feed l2-full and a
  report with L2 = 40 → pad sees 255 and 0.
- [ ] Run → RED. Implement: linear `startAt*255 + (v - dz)/(255 - dz) * (255 - startAt*255)` above the deadzone, 0
  at or below it. EmulationService caches the effective triggers on settings change and applies Left to L2 and
  `EffectiveRight` to R2 before building `PadInput`.
- [ ] Run Core + App → GREEN. Commit "Trigger range: deadzone and start point for L2/R2".

### Task 2: Profiles data and ProfileService

**Files:** Create `src/DualPulse.Emulation/IForegroundWatcher.cs`, `src/DualPulse.App/Services/GameProfile.cs`,
`src/DualPulse.App/Services/ProfileService.cs`, `src/DualPulse.App.Tests/ProfileServiceTests.cs`. Modify
`AppSettings.cs` (+ `Profiles`, `EffectiveProfiles`, JSON context), `ServiceFakes.cs` (+ `FakeForegroundWatcher`).

**Produces:**
- `interface IForegroundWatcher { string? ForegroundExe { get; } event Action<string?>? Changed; }`
- `record GameProfile(string Name, string Exe, TriggerSettings? Triggers = null, GyroSettings? Gyro = null)`
- `interface IProfileService { GameProfile? Active { get; } AppSettings Effective { get; } event Action? Changed; }`
- `ProfileService(ISettingsService, IForegroundWatcher, string ownExe)`; `IHostedService`, `IDisposable`.
- `static AppSettings ProfileService.Merge(AppSettings saved, GameProfile? profile)`.

- [ ] Tests: `Merge_NullParts_FollowSaved`; `Merge_SetParts_Win`; `Merge_KeepsSavedGyroOffsets`;
  `ForegroundGame_ActivatesItsProfile` (exe case differs); `UnknownExe_ClearsProfile`;
  `OwnWindow_KeepsProfile` then desktop clears it; `ActiveProfileEdited_EffectiveUpdates`;
  `ActiveProfileRemoved_Clears`; `Changed_RaisedOnSwitchAndOnSettingsChange`; `DuplicateExe_FirstWins`;
  `OldSettings_HaveNoProfiles`.
- [ ] RED → implement → GREEN. Commit "Game profiles: data and profile service".

### Task 3: Win32 foreground watcher and running windows

**Files:** Create `src/DualPulse.Emulation/ForegroundWatcher.cs`, `src/DualPulse.Emulation/RunningWindows.cs`
(+ `IRunningWindows`, `RunningWindow(string Title, string Exe)`); add `foreground` command to
`tools/DualPulse.Probe` printing each change and `windows` printing the list.

- [ ] `ForegroundWatcher`: dedicated thread, `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, …, WINEVENT_OUTOFCONTEXT)`,
  `GetMessage` loop, exe via `GetWindowThreadProcessId` + `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` +
  `QueryFullProcessImageName`, file name only; failure → null. Dispose posts `WM_QUIT`.
- [ ] `RunningWindows.List(int excludePid)`: `EnumWindows`, visible, non-empty title, no owner, not tool windows;
  one entry per exe, sorted by title.
- [ ] Test `RunningWindows_ExcludesOwnProcess_AndHasTitles` (live, asserts no entry has our pid's exe and all titles
  non-empty). Manual: `DualPulse.Probe foreground` while alt-tabbing. Commit "Foreground watcher and window list".

### Task 4: Services read effective settings; DI

**Files:** Modify `ControllerService.cs`, `GyroService.cs`, `EmulationService.cs`, `Hosting.cs`; tests in
`ControllerServiceTests`, `GyroServiceTests`, `EmulationServiceTests`.

- [ ] Each service takes optional `IProfileService? profiles = null`; reads `profiles?.Effective ?? settings.Current`
  and subscribes to `profiles.Changed` when present instead of `settings.Changed`. Gyro calibration still writes
  saved settings.
- [ ] Tests (fake `IProfileService` with settable Effective): `Controller_ActiveProfileTriggers_Applied`,
  `Gyro_ProfileEnablesGyro`, `Emulation_ProfileRange_Applied`. RED → GREEN.
- [ ] Register `ForegroundWatcher`, `RunningWindows`, `ProfileService` (singleton + hosted + interface, before the
  controller service starts; own exe = `Environment.ProcessPath` file name). Commit "Services follow the active
  game profile".

### Task 5: Trigger range card

- [ ] `TriggersViewModel`: `LeftDeadzone`, `LeftStartAt`, `RightDeadzone`, `RightStartAt` (percent ints 0–30).
  Tests: persist, clamp, linked writes Left only. RED → GREEN.
- [ ] `TriggersView`: **TRIGGER RANGE** card, Deadzone and Starts at sliders per side (right pair hidden when linked),
  caption "Deadzone ignores the first part of the pull; Starts at makes a light touch register." Commit.

### Task 6: Editing a profile (selection + Triggers/Gyro targets)

**Produces:** `ProfileSelection : ObservableObject` with `GameProfile? Selected` (by exe), follows
`IProfileService.Active` when it becomes non-null; `UsesOwn(Part)`, `Customize(Part)`, `UseDefault(Part)` where
`enum ProfilePart { Triggers, Gyro }`.

- [ ] Tests (`ProfileSelectionTests`, extended `TriggersViewModelTests`, `GyroViewModelTests`):
  `Selection_FollowsActive`; `Customize_CopiesDefault`; `UseDefault_ClearsPart`;
  `TriggersVm_EditsProfile_WhenItHasTriggers` (saved unchanged); `TriggersVm_ReadOnly_WhenFollowingDefault`
  (`CanEdit` false, values = default); `GyroVm_EditsProfileGyro`; strip text
  "<Name> uses its own triggers" / "Follows your default triggers" (gyro: "gyro").
- [ ] Both VMs read/write through the target; `CanEdit`, `StripText`, `ShowStrip`, `IsCustom`, `CustomizeCommand`,
  `UseDefaultCommand`. Views: strip on top, content `IsEnabled="{Binding CanEdit}"`. RED → GREEN. Commit.

### Task 7: Controller page selector and Add game dialog

- [ ] `ControllerPageViewModel`: `Profiles` (Default + profiles), `SelectedProfile`, `IsActiveNow`,
  `ShowSameForAllGames`, `RemoveProfileCommand(GameProfile)`, `AddGameCommand` → raises `AddGameRequested`.
  `AddGameViewModel(IRunningWindows, ISettingsService, ProfileSelection)`: `Windows`, `Pick(RunningWindow)`
  creates or selects existing by exe.
  Tests: `AddGame_CreatesProfile`, `AddGame_ExistingExe_SelectsIt`, `Remove_ClearsSelection`, `IsActiveNow`.
- [ ] Views: selector ComboBox right of the chips, "● Active now" caption, "Same for all games" line on Lighting and
  Rumble, `AddGameWindow` (list + Add/Cancel). Build, launch, screenshot. Commit.

### Task 8: Docs

- [ ] `docs/acceptance/game-profiles.md` (unit-verified rows; hardware rows open), TODO Phase P lines updated.
  Commit.
