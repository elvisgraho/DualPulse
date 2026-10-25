# Portability

DualPulse runs on Windows today. The code is laid out so Linux and macOS can follow without touching the controller
engine or the UI.

## Layout

- `DualPulse.Core` and `DualPulse.App` target plain `net10.0` and build on any OS. Neither calls a Windows API
  (the one P/Invoke left in Core, `TimerResolution`'s `winmm` call, only runs behind `OperatingSystem.IsWindows()`).
- `DualPulse.Emulation` also targets `net10.0`, so the App can reference it everywhere. Its Windows implementations are
  marked `[SupportedOSPlatform("windows")]`; the platform analyzer (CA1416, an error in this build) rejects any
  unguarded call to them.
- `DualPulse.App/Platform/PlatformServices.cs` picks the OS services once at startup: the Windows implementations on
  Windows, the `Unsupported` stand-ins (`DualPulse.App/Platform/Unsupported.cs`) anywhere else. Each stand-in reports
  itself absent or does nothing, the same way the Windows ones behave when a driver or device is missing.
- `tools/DualPulse.Probe` is a Windows development tool and stays `net10.0-windows`.

Building on Windows gives a `win-x64` app as before. For another OS pass a runtime identifier:

```bash
dotnet build src/DualPulse.App -r linux-x64
```

```bash
dotnet build src/DualPulse.App -r osx-arm64
```

Both build today, but neither has been run off Windows yet. There the app would get only the features that talk to
the controller itself over HidSharp: lighting, adaptive triggers, battery and the mod server. Anything that needs the
OS (virtual pad, mouse and keyboard, audio haptics, per-game profiles) reports itself unavailable.

## What each platform still needs

To add a platform, implement the interfaces below (usually in a new `DualPulse.Linux` or `DualPulse.MacOS` project) and
register them in `PlatformServices`.

| Service (interface) | Windows | Linux | macOS |
| --- | --- | --- | --- |
| Controller HID access (HidSharp) | works | works through `hidraw`; needs a udev rule giving the user access to `054c:0ce6` / `054c:0df2` (USB and Bluetooth) | works through IOKit; may ask for Input Monitoring permission |
| Virtual pad (`IVirtualPadBackend`) | ViGEmBus | `uinput` (Xbox-style evdev pad); needs write access to `/dev/uinput` | no simple option; a DriverKit virtual HID device needs a signed system extension |
| Hide the real controller (`IHidHideDriver`) | HidHide | grab the evdev device (`EVIOCGRAB`) or a udev rule; probably a different interface | none known |
| System audio for haptics (`ISystemAudioSource`) | WASAPI loopback | PipeWire / PulseAudio monitor source of the default sink | Core Audio process tap (macOS 14.2+) or a loopback driver |
| Mouse and keyboard (`IPcOutput`) | `SendInput` | `uinput` keyboard/mouse device (works on X11 and Wayland) | `CGEvent`; needs Accessibility permission |
| Focused app for profiles (`IForegroundWatcher`) | WinEvent hook | X11 `_NET_ACTIVE_WINDOW`; Wayland has no general API (compositor-specific) | `NSWorkspace` frontmost application |
| Window list for profiles (`IRunningWindows`) | `EnumWindows` | X11 `_NET_CLIENT_LIST`; same Wayland limit | `CGWindowListCopyWindowInfo` |
| Microphone mute (`IMicrophone`) | Core Audio endpoint volume | PipeWire / PulseAudio default source mute | Core Audio default input device mute |
| Start at sign-in (`IRunRegistry`) | HKCU Run key | XDG autostart `.desktop` file in `~/.config/autostart` | Launch Agent or `SMAppService` |
| Bluetooth power-off (`IBluetoothRadio`) | `IOCTL_BTH_DISCONNECT_DEVICE` | BlueZ `org.bluez.Device1.Disconnect` over D-Bus | `IOBluetoothDevice.closeConnection` |

## Other Windows assumptions left in the app

- `SingleInstance` uses a named `EventWaitHandle` to bring the first instance forward. Named events are not supported
  off Windows (the constructor throws `PlatformNotSupportedException`), so Linux and macOS need another signal, e.g. a
  Unix domain socket in the user's runtime directory.
- `ModServerService` writes the DSX port file to `C:\Temp\DualSenseX\` as well as to local app data; off Windows that
  path should be skipped.
- Game profiles match the focused program by exe file name (`game.exe`). On Linux and macOS the name would come from
  the process (`/proc/<pid>/comm`, the bundle executable); Proton games still report their Windows exe.
- The "Start with Windows" setting and some UI texts name Windows; they would read "Start at sign-in" elsewhere.
- No tray icon test has been done off Windows; Avalonia's tray support on Linux depends on the desktop
  (StatusNotifierItem).
