# Building DualPulse

## Requirements

- Windows 10/11 x64
- [.NET SDK 10](https://dotnet.microsoft.com/download) (`winget install Microsoft.DotNet.SDK.10`)
- For Xbox 360 / DualShock 4 emulation: the ViGEmBus driver; for the virtual DualSense: usbip-win2
  (`docs/virtual-dualsense.md`); optionally HidHide to hide the real controller

## Build, test, run

```bash
dotnet build src/DualPulse.slnx -c Release
```

```bash
dotnet test --project src/DualPulse.Core.Tests -c Release
```

```bash
dotnet test --project src/DualPulse.App.Tests -c Release
```

```bash
dotnet run --project src/DualPulse.App -c Release
```

Output: `src/DualPulse.App/bin/Release/net10.0/win-x64/DualPulse.exe`. Settings and logs live in
`%LOCALAPPDATA%\DualPulse` (`settings.json`, `logs\dualpulse-yyyyMMdd.log`, kept 7 days). Closing the window keeps
DualPulse in the tray; use the tray menu's Quit to exit.

Core and App target plain `net10.0`; on Windows they build for `win-x64` as before. For another OS pass a runtime
identifier, e.g. `dotnet build src/DualPulse.App -r linux-x64`. What each platform still lacks is in
`docs/portability.md`.

Hardware probe for development (`tools/DualPulse.Probe`): `capture-all` records test fixtures, `lightbar` sends an
output report, `xinput-rumble` rumbles the first XInput pad left then right.

## Release

```powershell
./tools/publish.ps1 -Version 0.2.0
```

Builds `artifacts/DualPulse-0.2.0-win-x64.zip`: a self-contained single-file `DualPulse.exe` (no .NET install needed)
with the license and notices. Pushing a tag such as `v0.2.0` does the same on GitHub Actions
(`.github/workflows/release.yml`) and attaches the zip to a new release.
