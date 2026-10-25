# Clean Rebuild R1–R4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the new DualPulse app (Core engine, Emulation layer, Avalonia 12 shell with a live Home view and a
Settings page) alongside the legacy app. The result is good enough for daily use: connection, battery, lightbar,
trigger presets, rumble, Xbox 360/DS4 emulation and Bluetooth power-off.

**Architecture:**
- `DualPulse.Core` is a UI-free engine. Per connected controller it runs a read thread and a coalescing write thread,
  parses reports into immutable `InputSnapshot`s and builds output reports from an immutable `OutputState`.
- `DualPulse.Emulation` wraps ViGEm behind `IVirtualPadBackend`.
- `DualPulse.App` (Avalonia 12, MVVM, Microsoft.Extensions.Hosting) samples the latest snapshot once per rendered
  frame.

**Tech Stack:** .NET 10, C# 14, Avalonia 12.1.4 (+ Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter
12.1.4), CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.Hosting 10.0.13, System.IO.Hashing 10.0.13, HidSharp
2.6.4, Nefarius.ViGEm.Client 1.21.256, xUnit v3 4.0.2 on Microsoft.Testing.Platform,
Microsoft.Extensions.TimeProvider.Testing 10.0.13.

**Spec:** `docs/superpowers/specs/2026-10-23-clean-rebuild-design.md`. Report layouts:
`docs/bluetooth-report-audit.md`.

## Global Constraints

- **No decompiled code:** never copy code from `src/DualSenseX/` except the fork's own files, which may be ported:
  `ControllerDiscovery`, `DualSenseBatteryStatus`, `BatteryNotifier`, `ControllerFirmwareInfo`, `LinkQualityMeter`,
  `InputActivityTracker`, `RumbleMode`, `BluetoothPower`, `RemixSettings`, `ConnectionLog`. No code from Linux
  `hid-playstation.c` (GPL) or DualSenseClient (GPL); facts and layouts only.
- **No legacy changes:** `src/DualSenseX/` and `src/DualSenseX.Tests/` stay untouched by this plan.
- **Shared build settings:** new projects import `src/DualPulse.props` with `TargetFramework net10.0-windows`,
  `LangVersion 14`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`, `PlatformTarget x64`.
  There is **no** `Directory.Build.props` (it would affect the legacy project).
- **Solution:** new projects go in `src/DualPulse.slnx`, not in `src/DualSenseX.sln`.
- **Data folder:** `%LOCALAPPDATA%\DualPulse\` (`settings.json`, `logs\`).
- **Supported devices:** Sony VID `0x054C`. DualSense PID `0x0CE6`, DualSense Edge PID `0x0DF2` (basic support only).
  DualShock 4 hardware is out of scope for v1.
- **Report facts** (from the audit):
  - Input: USB `0x01`, 64 bytes, data base offset 1. Bluetooth `0x31`, 78 bytes, data base offset 2.
  - Output: USB `0x02`, 48 bytes, common block at 1. Bluetooth `0x31`, 78 bytes, with CRC-32 over `0xA2` + bytes
    0..73 stored little-endian at 74..77.
  - Bluetooth sequenced form: `[1] = seq << 4`, `[2] = 0x10`, common block at 3.
  - Bluetooth legacy (SDL2) form: `[1] = 0x02`, common block at 2.
- **Common block offsets:** flag0 0, flag1 1, motorRight 2, motorLeft 3, micLed 8, rightTrigger 10–20,
  leftTrigger 21–31, flag2 38, lightbarSetup 41, ledBrightness 42, playerLeds 43, red 44, green 45, blue 46.
- **Motor order:** "left/large" always comes first in every API (`Rumble(left, right)`, `RumbleFeedback(Large, Small)`).
- **Performance targets:** idle CPU ≤ 2% of one core over Bluetooth; private memory ≤ 150 MB.
- **Design tokens** (exact): Bg `#0D0E11`, Surface `#15171B`, SurfaceRaised `#1C1F24`, Border `#262A31`,
  TextPrimary `#ECEDEF`, TextSecondary `#9097A1`, TextMuted `#5D646E`, Accent `#C9A86A`, AccentMuted `#29C9A86A`,
  Positive `#5FB98E`, Warning `#D9A441`, Critical `#D9645A`.
  - Type scale 12/14/16/20/28; Inter; 8 px grid; radius 10 (controls) / 14 (cards); 1 px borders.
  - No gradients, blur, acrylic or glass.

## Review Focus

1. **Controller held by another app:** the legacy app or Steam may have it open (shared HID access). Expect the
   session to start or report a clear status; never crash. Test: Task 5
   `Manager_OpenFailure_IsReported_And_Retried_After_Backoff`.
2. **ViGEmBus not installed:** the controller still connects and the Home view works in native mode. Emulation shows
   "Driver missing". Test: Task 9 `Emulation_BackendUnavailable_KeepsSessionAndReportsDriverMissing`.
3. **Corrupt or empty `settings.json`:** defaults load and the bad file is kept as `settings.json.bak`. Test: Task 8
   `Settings_CorruptFile_FallsBackAndKeepsBackup`.
4. **Two DualSense controllers connected:** v1 binds one (USB preferred, then the first path), logs the other, and
   doesn't flap between them. Test: Task 5 `Manager_TwoControllers_BindsOneStably`.
5. **No controller at startup, or disconnect while the UI renders:** the Home view shows the disconnected state, and
   the live view draws an idle controller on a null snapshot. Test: Task 10
   `Home_NoSession_ShowsDisconnectedState`.

---

## File Structure

```
src/DualPulse.props                       shared MSBuild settings
src/DualPulse.slnx                        solution for the new projects
src/DualPulse.Core/
  Protocol/  Transport.cs, InputSnapshot.cs, Buttons.cs, TouchPoint.cs, DualSenseInputParser.cs,
             OutputState.cs, TriggerEffect.cs, TriggerEffects.cs, OutputReportBuilder.cs, BluetoothLayout.cs
  Devices/   IHidTransport.cs (HidDeviceInfo, IHidConnection), HidSharpTransport.cs, ControllerDiscovery.cs,
             ControllerSession.cs, ControllerManager.cs
  Features/  DualSenseBattery.cs, FirmwareInfo.cs, LinkQualityMeter.cs, InputActivityTracker.cs, BatteryNotifier.cs
  Mapping/   PadStates.cs (XboxPadState, Ds4PadState), InputMapper.cs
  Power/     BluetoothPower.cs, PowerPolicy.cs, PowerSettings.cs
src/DualPulse.Emulation/  IVirtualPad.cs, ViGEmBackend.cs, ViGEmXboxPad.cs, ViGEmDs4Pad.cs
src/DualPulse.App/
  App.axaml(.cs), Program.cs, Hosting.cs
  Theme/     Tokens.axaml, Controls.axaml
  Logging/   FileLoggerProvider.cs
  Services/  AppSettings.cs, SettingsService.cs, ControllerService.cs, EmulationService.cs, PowerService.cs
  ViewModels/ ShellViewModel.cs, HomeViewModel.cs, SettingsViewModel.cs
  Views/     ShellWindow.axaml, HomeView.axaml, SettingsView.axaml
  Controls/  ControllerView.cs, ControllerLayout.cs
src/DualPulse.Core.Tests/   (+ Fixtures/*.hex, FakeHidTransport.cs)
src/DualPulse.App.Tests/
tools/DualPulse.Probe/      console tool: capture input reports, send output reports (hardware checks)
```

---

### Task 1: Solution scaffold and ported feature helpers

**Files:**
- Create: `src/DualPulse.props`, `src/DualPulse.slnx`, `src/DualPulse.Core/DualPulse.Core.csproj`,
  `src/DualPulse.Core.Tests/DualPulse.Core.Tests.csproj`.
- Create, ported from the fork's own legacy files: `Features/DualSenseBattery.cs`, `Features/FirmwareInfo.cs`,
  `Features/LinkQualityMeter.cs`, `Features/InputActivityTracker.cs`, `Features/BatteryNotifier.cs`,
  `Devices/ControllerDiscovery.cs`.
- Test: port `DualSenseBatteryStatusTests`, `LinkInfoTests`, `BatteryNotifierTests` and `ControllerDiscoveryTests`,
  plus the `InputActivityTrackerTests` part of `BluetoothPowerTests`, into `src/DualPulse.Core.Tests/`.
- Modify: `.github/workflows/build.yml` to also build and test `src/DualPulse.slnx`.

**Interfaces:**
- Produces:
  - `namespace DualPulse.Core.Features`. Port the types as-is, with three changes:
    - Rename `ControllerFirmwareInfo` to `FirmwareInfo`. Drop its `Read(HidDevice)`; Task 5 reads it through
      `IHidConnection`.
    - Drop `DualSenseBatteryStatus.StatusOffset`; the parser owns offsets.
    - `ControllerDiscovery` keeps only `IsBluetooth(string path)` and `ChoosePreferredPath(IEnumerable<string>)`.
    - `BatteryNotifier.TooltipText` says "DualPulse" instead of "DualSenseX Remix".
    - `FirmwareInfo` gains `bool SupportsImprovedRumble` (update version ≥ `0x0224`), replacing `RumbleMode`.
  - Nullable annotations are added (`FirmwareInfo? Parse(ReadOnlySpan<byte>)`).

- [ ] **Step 1:** Create `src/DualPulse.props` with the Global Constraints values. Create both csproj files importing
  it: Core references `System.IO.Hashing`; Tests reference xUnit v3 4.0.2, Microsoft.NET.Test.Sdk 18.11.0,
  xunit.runner.visualstudio 4.0.1, Microsoft.Extensions.TimeProvider.Testing 10.0.13 and the Core project, with
  `OutputType Exe`. Create `src/DualPulse.slnx` with both projects.
- [ ] **Step 2:** Port the tests above into `src/DualPulse.Core.Tests/`, with namespaces changed to
  `DualPulse.Core.Tests` and types to `DualPulse.Core.Features` / `DualPulse.Core.Devices`.
- [ ] **Step 3:** Run `dotnet test --project src/DualPulse.Core.Tests -c Release`. Expected: compile errors for the
  missing types.
- [ ] **Step 4:** Port the six helper files with the changes listed under Interfaces. Remove any `ConnectionLog`
  calls; logging comes through `ILogger` in Task 5.
- [ ] **Step 5:** Run `dotnet test --project src/DualPulse.Core.Tests -c Release`. Expected: all ported tests pass
  (about 35).
- [ ] **Step 6:** Add `dotnet build src/DualPulse.slnx -c Release` and
  `dotnet test --project src/DualPulse.Core.Tests -c Release` steps to `build.yml`.
- [ ] **Step 7:** Commit: `feat(core): scaffold DualPulse solution and port feature helpers`.

### Task 2: Probe tool and captured input fixtures

**Files:**
- Create: `tools/DualPulse.Probe/DualPulse.Probe.csproj` (console, HidSharp 2.6.4, references Core),
  `tools/DualPulse.Probe/Program.cs`.
- Create: `src/DualPulse.Core.Tests/Fixtures/*.hex`, copied to output.

**Interfaces:**
- Produces:
  - CLI: `DualPulse.Probe capture <name>` waits for Enter, then writes the next input report from every connected
    DualSense node to `Fixtures/<name>-<usb|bt>.hex`, as uppercase hex bytes separated by spaces, all on one line.
  - Task 4 adds: `DualPulse.Probe lightbar <r> <g> <b> [--legacy-bt]`.

- [ ] **Step 1:** Implement `capture` in `Program.cs` using HidSharp directly. It's a tool, not product code.
- [ ] **Step 2 (hardware, owner present):** Capture each fixture on **both** USB and Bluetooth, with the owner
  holding the stated input when Enter is pressed:

  | Fixture | Input held |
  |---|---|
  | `idle` | nothing pressed, sticks centred |
  | `cross` | Cross |
  | `dpad-upright` | D-pad up+right |
  | `ps-create-options` | PS + Create + Options |
  | `l2-full` | L2 fully pressed |
  | `left-stick-upleft` | left stick fully up-left |
  | `touch-one` | one finger on the touchpad's left half |

  Note the battery percentage the legacy app shows at capture time in `Fixtures/README.md`.
- [ ] **Step 3:** Commit: `test(core): add probe tool and captured input fixtures`.

### Task 3: Input parsing

**Files:**
- Create: `Protocol/Transport.cs`, `Protocol/Buttons.cs`, `Protocol/TouchPoint.cs`, `Protocol/InputSnapshot.cs`,
  `Protocol/DualSenseInputParser.cs`.
- Test: `src/DualPulse.Core.Tests/InputParserTests.cs` (loads the Task 2 fixtures).

**Interfaces:**
- Produces (`namespace DualPulse.Core.Protocol`):
  - `enum Transport { Usb, Bluetooth }`
  - `[Flags] enum Buttons : uint { None = 0, Square, Cross, Circle, Triangle, L1, R1, L2, R2, Create, Options, L3, R3,
    Ps, Touchpad, Mute, DpadUp, DpadDown, DpadLeft, DpadRight, Fn1, Fn2, LeftPaddle, RightPaddle }`, each one bit.
  - `readonly record struct TouchPoint(bool Active, byte Id, ushort X, ushort Y)`. X is 0–1919, Y 0–1079.
  - `readonly record struct InputSnapshot(Transport Transport, byte LeftX, byte LeftY, byte RightX, byte RightY,
    byte L2, byte R2, Buttons Buttons, short GyroX, short GyroY, short GyroZ, short AccelX, short AccelY,
    short AccelZ, uint SensorTimestamp, TouchPoint Touch0, TouchPoint Touch1, byte BatteryStatus,
    int? BluetoothSequence)`
  - `static class DualSenseInputParser { static bool TryParse(ReadOnlySpan<byte> report, out InputSnapshot snapshot); }`

- [ ] **Step 1:** Write the tests, each run for both `-usb` and `-bt` fixtures (`[Theory]` with transport):
  - `Idle_SticksNearCentre_NoButtons`: each stick axis is within 128 ± 20; `Buttons == None`; L2 and R2 are < 10;
    the transport matches the suffix.
  - `Cross_SetsCrossOnly`: `Buttons == Buttons.Cross`.
  - `DpadUpRight_DecodesHatToTwoFlags`: `Buttons == (DpadUp | DpadRight)`.
  - `PsCreateOptions_SetsThreeFlags`: `Buttons == (Ps | Create | Options)`.
  - `L2Full_ReportsAnalogAndDigital`: `L2 >= 250`; `Buttons.HasFlag(L2)`.
  - `LeftStickUpLeft_ReportsMinimumAxes`: `LeftX < 40`, `LeftY < 40`.
  - `TouchOne_ReportsActivePointOnLeftHalf`: `Touch0.Active`; `Touch0.X < 960`; `!Touch1.Active`.
  - `Bluetooth_HasSequence_Usb_DoesNot`: BT gives `BluetoothSequence` in 0..15; USB gives null.
  - `Rejects_WrongIdOrShortReport`: `TryParse` is false for `{0x05, ...64}`, for a 10-byte `0x31`, and for an empty
    span.
- [ ] **Step 2:** Run `dotnet test --project src/DualPulse.Core.Tests -c Release`. Expected: FAIL (types missing).
- [ ] **Step 3:** Implement `TryParse`. Parsing rules:
  - **Base offset:** 1 for `0x01` with length ≥ 64; 2 for `0x31` with length ≥ 78.
  - **Field offsets from the base:** sticks 0–3, L2/R2 4–5, buttons 7–9, gyro (3 × int16 LE) 15, accel 21,
    timestamp (uint32 LE) 27, touch 32 and 36, status 52.
  - **D-pad:** the low nibble of `buttons[0]` is a hat value, 0 = up, going clockwise to 7 = up-left, and 8 = none.
  - **Face buttons:** in the high nibble of `buttons[0]`, bits 4–7 are Square, Cross, Circle, Triangle.
  - **`buttons[1]`:** bits 0–7 are L1, R1, L2, R2, Create, Options, L3, R3.
  - **`buttons[2]`:** bits 0–2 are Ps, Touchpad, Mute; bits 4–7 are Fn1, Fn2, LeftPaddle, RightPaddle.
  - **Touch point:** byte 0 bit 7 = inactive, bits 0–6 = id; `X = (b2 & 0x0F) << 8 | b1`; `Y = b3 << 4 | b2 >> 4`.
  - **BT sequence:** `report[1] >> 4`.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Commit: `feat(core): parse DualSense input reports`.

### Task 4: Output state, trigger effects, report builder (hardware-verified)

**Files:**
- Create: `Protocol/OutputState.cs`, `Protocol/TriggerEffect.cs`, `Protocol/TriggerEffects.cs`,
  `Protocol/BluetoothLayout.cs`, `Protocol/OutputReportBuilder.cs`.
- Modify: `tools/DualPulse.Probe/Program.cs` (add the `lightbar` command).
- Test: `OutputReportBuilderTests.cs`, and `TriggerEffectsTests.cs` ported from the legacy test cases (same expected
  bytes).

**Interfaces:**
- Produces (`namespace DualPulse.Core.Protocol`):
  - `readonly record struct Rgb(byte R, byte G, byte B)`
  - `enum MicLed : byte { Off = 0, On = 1, Pulse = 2 }`
  - `enum LedBrightness : byte { High = 0, Medium = 1, Low = 2 }`
  - `readonly struct TriggerEffect`: holds 11 bytes; `static TriggerEffect Off`; `ReadOnlySpan<byte> Bytes`.
  - `static class TriggerEffects`: `Off()`; `Feedback(byte startZone0to9, byte strength0to8)` (code `0x21`);
    `Weapon(byte start2to7, byte end, byte strength0to8)` (`0x25`); `Vibration(byte startZone, byte amplitude0to8,
    byte frequencyHz)` (`0x26`). Each returns a `TriggerEffect`; invalid arguments throw
    `ArgumentOutOfRangeException`.
  - `sealed record OutputState(Rgb Lightbar, byte PlayerLeds, LedBrightness PlayerLedBrightness, MicLed MicLed,
    TriggerEffect LeftTrigger, TriggerEffect RightTrigger, byte LeftMotor, byte RightMotor, bool ImprovedRumble,
    bool FadeOutBootLight)`, with `static OutputState Default` = Lightbar (0,0,0), PlayerLeds `0b00100`, High, Off,
    triggers Off, motors 0, ImprovedRumble false, FadeOutBootLight true.
  - `enum BluetoothLayout { Sequenced, Legacy }`
  - `static class OutputReportBuilder`:
    - `const int UsbLength = 48`, `const int BluetoothLength = 78`
    - `static int Build(OutputState state, Transport transport, byte sequence, BluetoothLayout layout,
      Span<byte> destination)` returns the number of bytes written.

- [ ] **Step 1:** Write the tests:
  - `Usb_Default_HasIdAndFlags`: `[0]=0x02`, `[1]=0x0F`, `[2]=0x15`, `[39]=0x03`, `[42]=0x02`, `[44]=0x24`;
    returns 48.
  - `Usb_LightbarAndMotors_AtCommonOffsets`: Lightbar (255,0,128), Left 200, Right 50 gives `[45..47] = FF 00 80`,
    `[3]=50` (right/small), `[4]=200` (left/large).
  - `Usb_ImprovedRumble_UsesFlag2Bit2`: `[1]=0x0E`, `[39]=0x07`.
  - `Bt_Sequenced_HeaderAndOffsets`: seq 5 gives `[0]=0x31`, `[1]=0x50`, `[2]=0x10`, `[3]=0x0F`, lightbar at `[47..49]`,
    right trigger bytes at `[13..23]`, left at `[24..34]`; returns 78.
  - `Bt_Legacy_HeaderAndOffsets`: `[1]=0x02`, `[2]=0x0F`, lightbar at `[46..48]`.
  - `Bt_Crc_MatchesStandardCrc32WithA2Prefix`: `BitConverter.ToUInt32(r, 74) == Crc32.HashToUInt32([0xA2, ..r[0..74]])`.
  - `Triggers_WrittenAtBothSides`: Right `Feedback(0,8)` gives USB `[11..21]` starting `21 FF 03`; Left `Weapon(2,8,8)`
    gives USB `[22..32]` starting `25 04 01 07`.
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement `TriggerEffects` from Nielk1's documented encodings, then `Build`:
  - flag0 = `0x02 | 0x04 | 0x08 | (ImprovedRumble ? 0 : 0x01)`
  - flag1 = `0x01 | 0x04 | 0x10`
  - flag2 = `0x01 | 0x02 | (ImprovedRumble ? 0x04 : 0)`
  - lightbarSetup = `FadeOutBootLight ? 0x02 : 0`
  - playerLeds = `PlayerLeds & 0x1F | 0x20`
  - CRC via `System.IO.Hashing.Crc32`.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Add `lightbar <r> <g> <b> [--legacy-bt]` to the probe. It builds `OutputState.Default with { Lightbar }`
  and writes it to every connected DualSense node.
- [ ] **Step 6 (hardware, owner present):** Over Bluetooth, run `lightbar 255 0 0` (sequenced) and then
  `lightbar 0 255 0 --legacy-bt`. Over USB, run `lightbar 0 0 255`. The owner confirms each colour. If the sequenced
  form doesn't change the colour, make `BluetoothLayout.Legacy` the default in Task 5 and record that in
  `docs/bluetooth-report-audit.md`.
- [ ] **Step 7:** Commit: `feat(core): build DualSense output reports`.

### Task 5: HID transport, controller session and manager

**Files:**
- Create: `Devices/IHidTransport.cs`, `Devices/HidSharpTransport.cs`, `Devices/ControllerSession.cs`,
  `Devices/ControllerManager.cs`.
- Test: `src/DualPulse.Core.Tests/FakeHidTransport.cs`, `ControllerManagerTests.cs`, `ControllerSessionTests.cs`.

**Interfaces:**
- Consumes: `DualSenseInputParser.TryParse`, `OutputReportBuilder.Build`, `FirmwareInfo.Parse`,
  `DualSenseBatteryStatus.Parse`, `LinkQualityMeter`, `InputActivityTracker`, `ControllerDiscovery`.
- Produces (`namespace DualPulse.Core.Devices`):
  - `sealed record HidDeviceInfo(string Path, int VendorId, int ProductId, int MaxInputLength, int MaxOutputLength,
    int MaxFeatureLength)`
  - `interface IHidConnection : IDisposable { int Read(Span<byte> buffer, TimeSpan timeout); void Write(ReadOnlySpan<byte> report); void GetFeature(Span<byte> buffer); }`.
    Read throws `TimeoutException` on timeout and `IOException` when the device is gone.
  - `interface IHidTransport { IReadOnlyList<HidDeviceInfo> Enumerate(); event EventHandler? DevicesChanged; IHidConnection Open(HidDeviceInfo device); }`
  - `sealed class ControllerSession : IDisposable`:
    - ctor: `(HidDeviceInfo device, IHidTransport transport, BluetoothLayout layout, ILogger logger)`
    - properties: `Transport Transport`, `HidDeviceInfo Device`, `InputSnapshot? Latest`, `DualSenseBattery Battery`,
      `FirmwareInfo? Firmware`, `LinkQualitySnapshot? LinkQuality`, `InputActivityTracker Activity`,
      `bool IsReading`, `OutputState Output`
    - members: `event Action<InputSnapshot>? InputReceived` (raised on the read thread), `void Start()`,
      `void Apply(Func<OutputState, OutputState> change)`, `void Stop(string reason)`
  - `sealed class ControllerManager : IDisposable`:
    - ctor: `(IHidTransport transport, TimeProvider time, ILogger<ControllerManager> logger,
      BluetoothLayout layout = BluetoothLayout.Sequenced)`
    - members: `ControllerSession? Current`, `event Action<ControllerSession>? SessionStarted`,
      `event Action<ControllerSession, string>? SessionEnded`, `void Start()`, `void Poll()`

- [ ] **Step 1:** Write `FakeHidTransport`. It holds a mutable device list and raises `DevicesChanged`. Its
  connections serve queued reports, can be told to throw `IOException`, record writes, and answer `GetFeature(0x20)`
  with the real firmware bytes from `LinkInfoTests`.
- [ ] **Step 2:** Write the tests:
  - `Manager_DeviceAppears_StartsSession`
  - `Manager_PrefersUsbOverBluetooth_WhenBothListed`
  - `Manager_UsbAppears_RebindsFromBluetooth`
  - `Manager_ReaderDies_RebindsAfterOneSecondBackoff`: no new session at +0.9 s, a new one at +1.0 s
    (FakeTimeProvider).
  - `Manager_DeviceVanishes_EndsSession`
  - `Manager_TwoControllers_BindsOneStably`: repeated `Poll` keeps the same `Current`.
  - `Manager_OpenFailure_IsReported_And_Retried_After_Backoff`
  - `Session_ParsesReports_UpdatesLatestBatteryAndLinkQuality`
  - `Session_ReadsFirmware_EnablesImprovedRumbleFrom0x0224`
  - `Session_Apply_CoalescesToNewestReport`: after 50 rapid `Apply` calls, the last written report carries the final
    lightbar.
  - `Session_Stop_EndsBothThreads`
- [ ] **Step 3:** Run the tests. Expected: FAIL.
- [ ] **Step 4:** Implement:
  - **`ControllerManager`:** port the legacy rebind rules: USB preferred; rebind on a path change or when
    `!IsReading`; 1 s backoff after any end. `DevicesChanged` calls `Poll`, and `TimeProvider` drives a 250 ms
    fallback poll.
  - **`ControllerSession` threads:** port the legacy ownership rules. The read thread is the only one that ends the
    session's external resources. The write thread coalesces to the newest `OutputState`, with a sequence counter
    `0..15` for Bluetooth. `Stop` disposes the connections to unblock reads.
  - **`ControllerSession` firmware:** read feature `0x20` once on start (DualSense PIDs only), then
    `Apply(s => s with { ImprovedRumble = firmware.SupportsImprovedRumble })`.
  - **`HidSharpTransport`:** wraps `DeviceList.Local` filtered to VID `0x054C`, PIDs `0x0CE6`/`0x0DF2`.
- [ ] **Step 5:** Run the tests. Expected: PASS.
- [ ] **Step 6:** Commit: `feat(core): controller session and manager with rebind rules`.

### Task 6: Input mapping and power policy

**Files:**
- Create: `Mapping/PadStates.cs`, `Mapping/InputMapper.cs`, `Power/PowerSettings.cs`, `Power/PowerPolicy.cs`,
  `Power/BluetoothPower.cs` (ported).
- Test: `InputMapperTests.cs`, `PowerPolicyTests.cs`, `BluetoothPowerTests.cs` (the MAC parsing part, ported).

**Interfaces:**
- Produces:
  - `readonly record struct XboxPadState(short LeftX, short LeftY, short RightX, short RightY, byte LeftTrigger,
    byte RightTrigger, XboxButtons Buttons)`, with `[Flags] enum XboxButtons : ushort { A, B, X, Y, LeftShoulder,
    RightShoulder, Back, Start, Guide, LeftThumb, RightThumb, Up, Down, Left, Right }`.
  - `readonly record struct Ds4PadState(byte LeftX, byte LeftY, byte RightX, byte RightY, byte L2, byte R2,
    Buttons Buttons)`. It reuses the DualSense `Buttons`; Create maps to Share.
  - `static class InputMapper { static XboxPadState ToXbox(in InputSnapshot s); static Ds4PadState ToDs4(in InputSnapshot s); }`
  - `sealed record PowerSettings(bool ShortcutEnabled = true, bool PowerOffOnExit = false, int IdleTimeoutMinutes = 15)`
  - `enum PowerAction { None, ShortcutPowerOff, IdlePowerOff }`
  - `sealed class PowerPolicy { PowerAction Evaluate(in InputSnapshot snapshot, DualSenseBattery battery,
    InputActivityTracker activity, DateTimeOffset now, PowerSettings settings); }`
  - `static class BluetoothPower { static bool TryReadMac(IHidConnection c, out long mac); static bool PowerOff(long mac); }`

- [ ] **Step 1:** Write the tests:
  - `Xbox_StickExtremes`: LeftX 0 gives −32768; 255 gives 32767; LeftY 0 gives 32767 (up); 255 gives −32768.
  - `Xbox_ButtonsMap`: Cross→A, Circle→B, Square→X, Triangle→Y, L1→LeftShoulder, Create→Back, Options→Start,
    Ps→Guide, DpadUp|DpadRight→Up|Right.
  - `Xbox_Triggers_PassThrough`
  - `Ds4_CopiesAxesAndButtons`
  - `Policy_ShortcutFiresOncePerPress`: PS+Triangle held across three evaluations gives one `ShortcutPowerOff`;
    after release and press again, a second one.
  - `Policy_Shortcut_IgnoredOverUsb`
  - `Policy_Idle_FiresAfterTimeout_NotWhileCharging_AtMostOncePerMinute`
  - `Policy_ShortcutDisabled_DoesNothing`
  - The ported MAC tests (`0x123456789ABC` from the real pairing report).
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement:
  - **Xbox axes:** `X = ((x << 8) | x) - 32768`; `Y = 32767 - ((y << 8) | y)`.
  - **`PowerPolicy`:** state is the previous combo state and the time of the last idle attempt.
  - **`BluetoothPower`:** ported, reading the MAC through `IHidConnection.GetFeature` (report `0x09`).
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Commit: `feat(core): input mapping and power policy`.

### Task 7: Emulation layer

**Files:**
- Create: `src/DualPulse.Emulation/DualPulse.Emulation.csproj` (references Core and Nefarius.ViGEm.Client 1.21.256),
  `IVirtualPad.cs`, `ViGEmBackend.cs`, `ViGEmXboxPad.cs`, `ViGEmDs4Pad.cs`. Add the project to `src/DualPulse.slnx`.

**Interfaces:**
- Produces (`namespace DualPulse.Emulation`):
  - `enum PadKind { Xbox360, DualShock4 }`
  - `readonly record struct RumbleFeedback(byte Large, byte Small, Rgb? Lightbar)`
  - `interface IVirtualPad : IDisposable { PadKind Kind { get; } void Submit(in InputSnapshot input); event Action<RumbleFeedback>? Feedback; }`
  - `interface IVirtualPadBackend { bool IsAvailable { get; } string? UnavailableReason { get; } IVirtualPad Create(PadKind kind); }`
  - `sealed class ViGEmBackend : IVirtualPadBackend, IDisposable`. It creates the `ViGEmClient` lazily; on
    `VigemBusNotFoundException` it sets `IsAvailable = false` and `UnavailableReason = "ViGEmBus driver not installed"`.

- [ ] **Step 1:** Implement the pads:
  - **`ViGEmXboxPad`:** `Submit` calls `InputMapper.ToXbox`, then `SetAxisValue`, `SetSliderValue` and
    `SetButtonState` (including `Xbox360Button.Guide`), then a single `SubmitReport()` with `AutoSubmitReport = false`.
    `FeedbackReceived` raises `Feedback(new(e.LargeMotor, e.SmallMotor, null))`.
  - **`ViGEmDs4Pad`:** the same via `ToDs4` and `DualShock4DPadDirection`.
  - **Disposal:** `Dispose` disconnects the pad.
- [ ] **Step 2:** Run `dotnet build src/DualPulse.slnx -c Release`. Expected: 0 errors.
- [ ] **Step 3 (hardware):** No unit tests (thin wrapper over the driver); it's verified in Task 12.
- [ ] **Step 4:** Commit: `feat(emulation): ViGEm Xbox 360 and DS4 backends`.

### Task 8: App shell, theme, hosting, settings, logging

**Files:**
- Create: `src/DualPulse.App/DualPulse.App.csproj` (WinExe, Avalonia packages 12.1.4, CommunityToolkit.Mvvm 8.4.2,
  Microsoft.Extensions.Hosting 10.0.13; references Core and Emulation; `AvaloniaUseCompiledBindingsByDefault true`).
- Create: `Program.cs`, `App.axaml(.cs)`, `Hosting.cs`, `Theme/Tokens.axaml`, `Theme/Controls.axaml`,
  `Logging/FileLoggerProvider.cs`, `Services/AppSettings.cs`, `Services/SettingsService.cs`,
  `ViewModels/ShellViewModel.cs`, `Views/ShellWindow.axaml(.cs)`.
- Create: `src/DualPulse.App.Tests/DualPulse.App.Tests.csproj` (same test packages, references App).
- Test: `SettingsServiceTests.cs`, `ShellViewModelTests.cs`.

**Interfaces:**
- Produces:
  - `sealed record AppSettings(int Version = 1, PadKind? Emulation = PadKind.Xbox360, Rgb? Lightbar = null,
    TriggerPreset TriggerPreset = TriggerPreset.Off, PowerSettings? Power = null, bool StartMinimized = false)`.
    `Power` defaults to `new PowerSettings()`; `null` Lightbar means Accent `#C9A86A`; `null` Emulation means
    native mode.
  - `enum TriggerPreset { Off, Feedback, Weapon, Vibration }`
  - `interface ISettingsService { AppSettings Current { get; } void Update(Func<AppSettings, AppSettings> change); event Action<AppSettings>? Changed; }`
    `SettingsService(string folder)` stores JSON at `<folder>/settings.json` via a source-generated
    `JsonSerializerContext`.
  - `enum Page { Home, Settings }`; `ShellViewModel` with `Page CurrentPage`, `NavigateCommand(Page)`,
    `string StatusText`.

- [ ] **Step 1:** Write the tests:
  - `Settings_MissingFile_UsesDefaults`
  - `Settings_Update_PersistsAndRaisesChanged`
  - `Settings_CorruptFile_FallsBackAndKeepsBackup`: write `{not json`; defaults load; `settings.json.bak` exists
    with the bad content.
  - `Settings_NewerUnknownFields_Ignored`
  - `Shell_StartsOnHome_NavigatesToSettings`
- [ ] **Step 2:** Run `dotnet test --project src/DualPulse.App.Tests -c Release`. Expected: FAIL.
- [ ] **Step 3:** Implement the services, then the shell:
  - **`Hosting.cs`:** a `Microsoft.Extensions.Hosting` host registering every service as a singleton, plus
    `TimeProvider.System`, `HidSharpTransport` and `ViGEmBackend`.
  - **`FileLoggerProvider`:** writes `logs/dualpulse-yyyyMMdd.log` and deletes files older than 7 days.
  - **Theme:** `Tokens.axaml` holds the exact Global Constraints colours as `SolidColorBrush` resources.
    `Controls.axaml` styles Button, ToggleSwitch, ComboBox, Slider and TextBlock classes on top of
    `FluentTheme` (Dark variant) using only those tokens, with 1 px borders, radius 10/14, and no shadows or
    transparency effects.
  - **Font:** `.WithInterFont()` in `Program.cs`.
  - **`ShellWindow`:** a 64 px left rail with Home and Settings icon buttons, a content host and a 28 px status bar.
  - **Tray:** an `App.axaml` `TrayIcon` with Open and Quit; its tooltip is bound later (Task 10).
- [ ] **Step 4:** Run the tests. Expected: PASS. Launch the app with `dotnet run --project src/DualPulse.App`; an empty
  dark shell appears.
- [ ] **Step 5:** Commit: `feat(app): Avalonia shell, design tokens, settings and logging`.

### Task 9: Controller, emulation and power services

**Files:**
- Create: `Services/ControllerService.cs`, `Services/EmulationService.cs`, `Services/PowerService.cs`.
- Test: `EmulationServiceTests.cs`, `ControllerServiceTests.cs`, `PowerServiceTests.cs` (fakes for
  `IVirtualPadBackend` and `IHidTransport`, reusing `FakeHidTransport` via a linked file).

**Interfaces:**
- Consumes: `ControllerManager`, `ControllerSession`, `IVirtualPadBackend`, `PowerPolicy`, `BluetoothPower`,
  `ISettingsService`.
- Produces:
  - `interface IControllerService { ControllerSession? Session { get; } event Action? SessionChanged; }`. It applies
    `Lightbar` and `TriggerPreset` (Feedback(3,6) / Weapon(2,6,8) / Vibration(2,6,30) on both triggers) on session
    start and on settings changes.
  - `enum EmulationStatus { Off, Active, DriverMissing }`
  - `interface IEmulationService { EmulationStatus Status { get; } event Action? StatusChanged; }`. It creates a pad
    per session for `Settings.Emulation`, submits on `InputReceived` (read thread), maps `Feedback` to
    `session.Apply(s => s with { LeftMotor = f.Large, RightMotor = f.Small })`, and disposes the pad when the session
    ends.
  - `PowerService`: evaluates `PowerPolicy` from `InputReceived` at most every 16 ms (shortcut) and every 10 s (idle),
    calls `BluetoothPower.PowerOff` off the read thread, and powers off on app exit when `PowerOffOnExit` is set.

- [ ] **Step 1:** Write the tests:
  - `Emulation_SessionStart_CreatesPadAndSubmitsInput`
  - `Emulation_Feedback_SetsMotorsLargeFirst`: `Feedback(200, 50)` gives `Output.LeftMotor == 200`,
    `RightMotor == 50`.
  - `Emulation_BackendUnavailable_KeepsSessionAndReportsDriverMissing`
  - `Emulation_ModeNull_NoPad_StatusOff`
  - `Emulation_SessionEnds_DisposesPad`
  - `Controller_AppliesLightbarAndPresetOnStartAndOnChange`
  - `Power_ShortcutCallsPowerOffOnce` (`BluetoothPower` behind an `Func<long,bool>` seam injected into
    `PowerService`)
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement the three services, registered in `Hosting.cs`.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Commit: `feat(app): controller, emulation and power services`.

### Task 10: Home view and live controller view

**Files:**
- Create: `ViewModels/HomeViewModel.cs`, `Views/HomeView.axaml(.cs)`, `Controls/ControllerLayout.cs`,
  `Controls/ControllerView.cs`.
- Test: `HomeViewModelTests.cs`, `ControllerLayoutTests.cs`.

**Interfaces:**
- Consumes: `IControllerService`, `IEmulationService`, `ISettingsService`, `PowerService`.
- Produces:
  - `HomeViewModel`:
    - display: `bool IsConnected`, `string ConnectionText` ("Bluetooth · 731 Hz · 0% loss" / "USB · 250 Hz" /
      "Not connected"), `int BatteryPercent`, `bool IsCharging`, `string FirmwareText` ("Firmware 06.30"),
      `string EmulationText` ("Xbox 360" / "DualShock 4" / "Native" / "Driver missing")
    - live input: `InputSnapshot? Snapshot`, set by the view once per frame
    - lightbar: `IReadOnlyList<Rgb> LightbarSwatches`, exactly `#C9A86A`, `#FFFFFF`, `#3D7BFF`, `#FF3B3B`, `#3DDC84`,
      `#000000`; `SetLightbarCommand(Rgb)`
    - triggers: `IReadOnlyList<TriggerPreset> TriggerPresets`, `TriggerPreset SelectedTriggerPreset`
    - `PowerOffCommand`, enabled only when connected over Bluetooth
  - `static class ControllerLayout { static Point StickDot(byte x, byte y, Point centre, double radius); static double TriggerFill(byte value); static Point TouchPoint(TouchPoint p, Rect pad); }`
  - `ControllerView : Control` with `StyledProperty<InputSnapshot?> SnapshotProperty`. `Render` draws an original
    stylized outline: body, two grips, touchpad, face buttons, d-pad, sticks, L1/R1/L2/R2. Pressed elements fill
    with Accent and idle ones are stroked in Border/TextMuted; stick dots move; trigger bars fill; touch points show.
    A `null` snapshot draws the idle outline.

- [ ] **Step 1:** Write the tests:
  - `Home_NoSession_ShowsDisconnectedState`: `IsConnected` false; `ConnectionText == "Not connected"`;
    `PowerOffCommand.CanExecute` false; `Snapshot` null.
  - `Home_BluetoothSession_ShowsLinkBatteryFirmware`
  - `Home_UsbSession_PowerOffDisabled`
  - `Home_SetLightbar_UpdatesSettings`
  - `Home_SelectPreset_UpdatesSettings`
  - `Layout_StickDot_CentreAndExtremes`: (128,128) gives the centre ± 1; (0,0) gives centre − radius on both axes
    within 1.
  - `Layout_TriggerFill_0To1`
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement:
  - **`HomeView` layout:** the controller view on the left (about 60%); on the right, a status column of four cards
    and a quick-actions card.
  - **Frame sampling:** `TopLevel.RequestAnimationFrame` loop pushes `session.Latest` into the view model only when it
    changed. Status text refreshes every 500 ms via `DispatcherTimer`.
  - **Motion:** 150–200 ms ease-out transitions on card state changes only.
  - **Tray tooltip:** `BatteryNotifier.TooltipText(level, state)` ("DualPulse - 70% (charging)"), or "DualPulse - not
    connected".
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Commit: `feat(app): home view with live controller visualization`.

### Task 11: Settings page

**Files:**
- Create: `ViewModels/SettingsViewModel.cs`, `Views/SettingsView.axaml(.cs)`, `THIRD-PARTY-NOTICES.md` (root).
- Test: `SettingsViewModelTests.cs`.

**Interfaces:**
- Produces: `SettingsViewModel` with `PadKind? Emulation` (choices: Xbox 360, DualShock 4, Native),
  `bool ShortcutEnabled`, `bool PowerOffOnExit`, `int IdleTimeoutMinutes` (choices 0, 5, 10, 15, 30, 60; 0 = never),
  `bool StartMinimized`, `string Version` (informational version), `OpenLogsFolderCommand`, and
  `OpenThirdPartyNoticesCommand`.

- [ ] **Step 1:** Write the tests:
  - `Settings_ChangingEmulation_PersistsAndRestartsPad`: through a fake `IEmulationService`, the pad kind changes.
  - `Settings_PowerOptions_Persist`
  - `Settings_IdleChoices_ExactValues`
- [ ] **Step 2:** Run the tests. Expected: FAIL.
- [ ] **Step 3:** Implement the view: sections **Emulation**, **Power**, **App** and **About** (version, logs folder,
  third-party notices listing Avalonia, CommunityToolkit.Mvvm, HidSharp, Nefarius.ViGEm.Client, Inter with their
  licenses). `EmulationService` reacts to `ISettingsService.Changed`.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Commit: `feat(app): settings page`.

### Task 12: Hardware acceptance, performance, docs

**Files:**
- Modify: `README.md` (Status table: rebuild R1–R4 done), `TODO.md` (tick R1–R4), `BUILDING.md` (how to build and run
  the new app).
- Create: `docs/acceptance/r1-r4.md` (results).

- [ ] **Step 1 (hardware, owner present; legacy app closed):** Run the new app. Check and record each item in
  `docs/acceptance/r1-r4.md`:
  - USB and BT connect, and the live view reacts to every button, both sticks, L2/R2 and touch.
  - Battery and charging are correct.
  - Lightbar swatches and trigger presets work over both transports.
  - Xbox 360 emulation works in a game or with XInput: rumble left first, then right, using the probe-style XInput
    test from the rumble work.
  - DS4 mode is seen by Windows.
  - Native mode without ViGEm works: test by disabling emulation.
  - PS+Triangle powers off over BT.
  - Reconnect works: power cycle, USB↔BT, sleep.
- [ ] **Step 2:** Measure over BT for 60 s idle with the window open, using the per-thread CPU and private-memory
  method from Phase 1 item 7. Expected: CPU ≤ 2% of one core and private memory ≤ 150 MB. Record the numbers. If
  either misses, profile with `dotnet-counters` / `dotnet-trace` and fix before ticking.
- [ ] **Step 3:** Update the docs listed above.
- [ ] **Step 4:** Commit: `docs: R1-R4 acceptance results`.
