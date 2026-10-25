# DualPulse: free DualSense app for PC

DualPulse gets everything out of a PS5 DualSense controller on Windows, **including over Bluetooth**: adaptive
triggers, haptic feedback, the controller's speaker and headset jack, lightbar and LEDs, and controller emulation so
every game works. Free and open source (MIT), a free alternative to DSX.

## Features

- **Wireless haptics and adaptive triggers:** games with native DualSense support get adaptive triggers, haptic
  feedback, lights and speaker sound over Bluetooth, through a virtual USB DualSense (no extra hardware).
- **Haptic feedback over Bluetooth:** game rumble plays as DualSense haptics, and PC sound (games, music, video) can be
  felt in the grips.
- **Controller speaker and headset jack over Bluetooth:** play PC sound on the controller's speaker or on earbuds
  plugged into it.
- **Adaptive triggers everywhere:** resistance, weapon and vibration effects, deadzones and start points, per game.
- **Controller emulation:** Xbox 360, DualShock 4 or a virtual DualSense, with the real controller hidden from games
  (HidHide) so there's no double input.
- **Game profiles:** trigger and gyro settings switch automatically when a game is in front.
- **PC control:** touchpad as a mouse, sticks for the pointer and scrolling, buttons as keys and media keys, an Alt+Tab
  window switcher.
- **Gyro aiming:** move the controller to aim, as mouse or right stick.
- **Lightbar and LEDs:** static, breathing, rainbow or battery colour; player LEDs show the battery; the mute button
  mutes your microphone.
- **DSX mods:** games with DSX v2 and v3.1 mods work.
- **Daily use:** starts with Windows, lives in the tray, low-battery pop-ups, turns the controller off on request.

## Requirements

- Windows 10 or 11, 64-bit
- A DualSense (PS5) controller, by cable or Bluetooth
- Optional drivers, depending on what you use:
  - [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases/latest) for Xbox 360 or DualShock 4 emulation
  - [usbip-win2](https://github.com/vadimgrn/usbip-win2/releases) for the virtual DualSense
    ([setup and limits](docs/virtual-dualsense.md))
  - [HidHide](https://github.com/nefarius/HidHide/releases) to hide the real controller from games

## Build from source

Needs the [.NET SDK 10](https://dotnet.microsoft.com/download).

```bash
dotnet build src/DualPulse.slnx -c Release
```

```bash
dotnet run --project src/DualPulse.App -c Release
```

Tests and the hardware probe: [BUILDING.md](BUILDING.md). Settings and logs are in `%LOCALAPPDATA%\DualPulse`.

## Good to know

- After the controller has been on Bluetooth, it ignores game haptics over the cable until you turn it off and on
  again. DualPulse tells you when this happens. To charge mid-game and keep haptics, charge from a USB charger or
  power bank: the controller then stays on Bluetooth.

## Credits

Originally based on DualSenseX by Paliverse; rewritten from scratch. Components and protocol references:
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

"DualSense", "PlayStation" and "PS5" are trademarks of Sony Interactive Entertainment. DualPulse is not affiliated
with Sony or Paliverse.

## License

[MIT](LICENSE)
