# Clean rebuild: design

Date: 2026-10-23 · Status: awaiting review · Roadmap: TODO.md, Phase R

## 1. Intent

**What the owner asked for:** a professional, minimal, dark app ("expensive", prestigious palette, no glass styles)
with a simpler main view, built on current, fast, up-to-date technology, with clean and efficient code. Cool
visualizations and animations can come later. The Info page goes. The project will be published on GitHub later as
free, non-commercial software. The decompiled code was only a starting point.

**What this design adds (assumptions, open to correction):**
- Publishing requires that no decompiled Paliverse code, asset, translation or example remains. A fresh history and
  a credit line alone are not enough, so the rebuild replaces the decompiled parts rather than restyling them.
- Windows only. Everything that matters (HID over Bluetooth, ViGEm/USB/IP, WASAPI, HidHide) is Windows-specific.
- Features move over gradually. The legacy app keeps working until the new one covers what the owner uses.

**Success criteria**
1. The new app replaces the legacy app for daily use: connection, battery, lightbar/LEDs, adaptive triggers, rumble,
   Xbox 360/DS4 emulation, power-off, the UDP mod server and key mapping.
2. It looks deliberate: one consistent dark design system, no stock-theme look.
3. Lower overhead than the legacy app today: idle CPU at most 2% of one core over Bluetooth (legacy: 3-4%), private
   memory at most 150 MB (legacy: ~260 MB), and no added input latency (every report is processed in the read thread).
4. The Core logic is unit-tested, including golden tests built from real reports captured from the controller.
5. When the legacy app is deleted, the repository contains only original code and openly licensed dependencies.

## 2. Technology

| Concern | Choice | Why |
|---|---|---|
| Runtime | .NET 10 (LTS), C# 14, `net10.0-windows` where Windows APIs are needed | Current LTS; the fork already runs on it |
| UI | **Avalonia 12** | Fully custom styling (no stock Windows look), GPU-composited Skia rendering suited to live visualization, compiled XAML bindings, MIT, mature (12.1.x). WinUI 3 was rejected: harder to brand, needs the Windows App SDK runtime. WPF was rejected: its Fluent theme is still experimental, and it is the stack being left behind. |
| MVVM | CommunityToolkit.Mvvm | Source-generated observable properties and commands; no reflection |
| Composition | Microsoft.Extensions.Hosting (DI, configuration, logging) | Standard, testable wiring |
| Logging | Microsoft.Extensions.Logging with a small rolling-file provider | Replaces `connection.log` / `crash.log` with leveled, categorized logs |
| Settings | System.Text.Json with source generation | Fast, trimming-safe; one versioned `settings.json` |
| HID | HidSharp 2.6.x behind `IHidTransport` | Proven on this hardware today; replaceable (HidApi.Net) without touching the rest |
| Emulation | Nefarius.ViGEm.Client behind `IVirtualPadBackend` | Works today; VIIPER/USB-IP backend added in Phase 3 |
| Tests | xUnit v3 on Microsoft.Testing.Platform | Already set up |
| Font | Inter (SIL Open Font License), bundled | Neutral, professional, tabular numerals for live values |

Publishing: framework-dependent build plus an optional self-contained single-file build. Native AOT is kept possible
(no reflection-based serialization or bindings), but it is not required for v1.

## 3. Architecture

```
src/
  DualPulse.Core/            controller engine, no UI          (net10.0-windows)
  DualPulse.Emulation/       virtual pad backends              (net10.0-windows)
  DualPulse.App/             Avalonia 12 app                   (net10.0-windows)
  DualPulse.Core.Tests/      unit + golden tests
  DualSenseX/                legacy app (deleted in R6)
  DualSenseX.Tests/          tests whose classes move to Core.Tests
```

Dependencies point one way: App → Emulation → Core. Core knows nothing about UI or emulation.

### 3.1 Core

| Unit | Purpose | Interface (sketch) | Comes from |
|---|---|---|---|
| `IHidTransport` | Enumerate devices, open read/write streams, feature reports, device-change events | `Devices`, `Changed`, `Open(path)` | new; HidSharp implementation |
| `DualSenseInputParser` | Turn a USB 0x01 or BT 0x31 report into an `InputSnapshot` (sticks, triggers, buttons, gyro/accel, timestamp, touch points, battery byte, BT sequence) | `bool TryParse(ReadOnlySpan<byte>, out InputSnapshot)` | rewritten from the audit layouts |
| `InputSnapshot` | Immutable value type for one input report | `readonly record struct` | new |
| `OutputState` | Immutable full output state: lightbar, player LEDs and brightness, mic LED, both trigger effects, motors, rumble mode, LED fade setup | `record` with `with` updates | new |
| `OutputReportBuilder` | `OutputState` → USB 0x02 or BT 0x31 bytes (**sequenced** form: `seq<<4`, tag 0x10, common block at 3), CRC-32 for BT | `int Build(OutputState, Transport, byte seq, Span<byte>)` | rewritten from the audit layouts |
| `TriggerEffects` | Adaptive trigger effect encoding (Off, Feedback, Weapon, Vibration, multi-position, plus legacy-compatible modes) | static methods writing 11 bytes | reimplemented from Nielk1's documented encodings (MIT) |
| `ControllerManager` | Discovery, USB-over-BT preference, rebind on device change, dead-reader watchdog, reconnect backoff | events `Connected`/`Disconnected`; `Controllers` | ported: `ControllerDiscovery` and the rebind logic |
| `ControllerSession` | One connected controller: read thread, coalescing write thread, latest snapshot, output state, firmware, battery, link quality, activity | `Latest`, `InputReceived`, `Output`, `Apply(Func<OutputState,OutputState>)`, `Stop()` | ported: read/write threads, `Stop()` ownership rules |
| Feature helpers | `BatteryStatus`, `FirmwareInfo`, `LinkQualityMeter`, `InputActivityTracker`, `RumbleMode`, `BluetoothPower`, `BatteryNotifier` | as today | ported with their tests |

**Threading.** One read thread per controller processes every report: parse, then map, then submit to the virtual pad,
with no UI work. It publishes the latest `InputSnapshot` through a single reference swap. The UI samples that value
once per rendered frame instead of receiving 700 events per second. One write thread per controller sends the newest
`OutputState`, as today. Rumble feedback from the virtual pad updates `OutputState` and signals the writer.

### 3.2 Emulation

`IVirtualPadBackend` creates `IVirtualPad` instances (Xbox 360, DualShock 4). `IVirtualPad.Submit(in PadReport)`
sends input; a `Feedback` event reports rumble (always large/left motor first, small/right second) and DS4 lightbar.
`InputMapper` (pure functions in Core: deadzones, axis invert, button remap) turns an `InputSnapshot` into a
`PadReport`. ViGEm is the v1 backend. The driver check is non-blocking: without ViGEmBus the app still reads the
controller and runs in native mode (this fixes the legacy limitation noted in Phase 0).

### 3.3 App

- **Shell:** a single window with a slim left navigation rail (icons with tooltips), the content area and a status
  bar. Tray icon with battery, connection and quit. Pages: **Home, Lighting, Triggers, Rumble, Emulation, Mapping,
  Settings**. No Info page; version and licenses go at the bottom of Settings.
- **Home (simple main view):**
  - Left, about 60%: a live controller view, an original stylized outline (not Sony artwork). Pressed buttons light
    up in the accent colour, sticks and triggers show position, touch points appear on the touchpad, plus a small
    motion indicator.
  - Right: a status column with connection (USB/Bluetooth, rate, loss), battery (level, charging), firmware, the
    active emulation mode, and three quick actions (lightbar colour, trigger preset, power off over Bluetooth).
  - Animation is limited to 150-200 ms ease-out state transitions. Richer visualizations come later.
- **Pattern:** one view model per page, bound with compiled bindings. View models talk to Core and Emulation through
  injected services (`IControllerService`, `IEmulationService`, `ISettingsService`). No code-behind logic.

### 3.4 Design system

| Token | Value | Use |
|---|---|---|
| `Bg` | `#0D0E11` | window background |
| `Surface` | `#15171B` | cards, rail |
| `SurfaceRaised` | `#1C1F24` | hover, inputs |
| `Border` | `#262A31` | 1 px dividers and card borders |
| `TextPrimary` | `#ECEDEF` | primary text |
| `TextSecondary` | `#9097A1` | labels, units |
| `TextMuted` | `#5D646E` | disabled, hints |
| `Accent` | `#C9A86A` (champagne gold) | the one accent: active state, focus, live highlights |
| `AccentMuted` | `#C9A86A` at 16% | selected backgrounds |
| `Positive` / `Warning` / `Critical` | `#5FB98E` / `#D9A441` / `#D9645A` | status only, used sparingly |

Rules:
- Inter type scale 12/14/16/20/28; 600 weight for headings only; tabular numerals for live values.
- 8 px spacing grid; 10 px radius for controls, 14 px for cards; 1 px borders instead of shadows.
- No gradients, blur, acrylic or glass. Accent at most once per region. Icons in a single line style.
- Light theme is a non-goal for v1. The tokens make it possible later.

## 4. Data flow

```
HID read thread ──► DualSenseInputParser ──► InputSnapshot ──► InputMapper ──► IVirtualPad.Submit
        │                                         │
        │                                         └─► Latest (reference swap) ──► UI, sampled per frame
        ▼
LinkQualityMeter, BatteryStatus, InputActivityTracker

UI action / settings / virtual-pad feedback ──► OutputState (immutable) ──► write thread ──► OutputReportBuilder ──► HID
```

## 5. Errors, logging, resilience

- HID errors end the session's read thread. `ControllerManager` rebinds after a 1 s backoff, as verified today
  (power cycle, USB↔BT, sleep).
- A missing emulation driver is a status shown on the Emulation page, not a blocking dialog.
- Logging categories: `Controller`, `Emulation`, `Settings`, `App`. The file log lives in
  `%LOCALAPPDATA%\DualPulse\logs`. Unhandled exceptions are logged with full stack traces.
- Settings are versioned. Unknown or corrupt files fall back to defaults and keep a `.bak` copy.

## 6. Testing

- Core: port the 66 existing tests. Add golden tests for `DualSenseInputParser` and `OutputReportBuilder`, using
  reports captured from the controller (USB and BT, with known stick, button and battery values), plus
  `ControllerManager` tests against a fake `IHidTransport` (device appears, vanishes, switches, reader dies).
- App: view-model tests with fake services.
- Hardware checklist per release: USB and BT connect, LEDs, triggers, rumble on both motors, emulation in a game,
  power-off, reconnect scenarios.

## 7. Migration and publishing

Scope of planning: the first implementation plan covers R1-R4 (Core, Emulation, app shell, Home view). R5 pages get
one plan each; R6-R7 get a final plan.

1. R1-R4 build the new app beside the legacy one. Only one of them should run at a time, because both would write to
   the controller.
2. R5 moves the remaining features page by page. Each page is done when the new app matches or beats legacy on
   hardware.
3. R6 deletes `src/DualSenseX`, its tests and the Paliverse-derived resources (language files, games list,
   backgrounds, fonts, BAML).
4. R7 publishing:
   - **Repository:** final name and trademark check; own icon; MIT LICENSE; third-party notices; fresh git history.
   - **README credit:** "Originally based on DualSenseX by Paliverse; rewritten from scratch."
   - **Final check:** a scan confirming no decompiled code remains.

## 8. Non-goals (v1)

Light theme · localization (English first; strings in resources so translation can come later) · macOS/Linux ·
features scheduled in later phases (BT audio/haptics, virtual DualSense, profiles, gyro mapping, Edge).

## 9. Open questions

- **Name:** "DualPulse" is a working name. It should be checked for conflicts before R7, and changing it before R1
  starts costs nothing.
- **Key mapping (Sense2Key):** migrate it as is, or redesign it within the Mapping page? The proposal is to redesign
  it within Mapping in R5.
