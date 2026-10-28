# DualPulse: free DualSense app for PC

Get everything out of a PS5 DualSense controller on Windows, **including over Bluetooth**: adaptive triggers, haptic
feedback, the controller's speaker and microphone, lights and controller emulation. Free and open source, a free
alternative to DSX.

![DualPulse home screen](docs/images/home.png)

## Features

- **Wireless haptics, adaptive triggers and speaker** for games with DualSense support, through a virtual DualSense.
- **Haptic feedback over Bluetooth** from game rumble, and PC sound you can feel in the grips (also on the cable).
- **Emulation:** Xbox 360, DualShock 4 or DualSense, with your real controller hidden from games.
- **Profiles:** triggers, gyro and sticks per game (switching automatically) or picked by hand.
- **For games:** stick deadzones, button remapping, gyro as the right stick, all trigger effects.
- **Microphone in games:** the controller's mic through the virtual DualSense, with a click-proof noise gate, low cut
  and boost.
- **PC control:** touchpad as mouse, buttons as keys and media keys.
- **Lightbar, battery LEDs, mic mute button**, controller details and DSX mods.

## Get started

1. Download the zip from [Releases](https://github.com/elvisgraho/DualPulse/releases/latest), unzip it and run
   `DualPulse.exe`. If Windows says it protected your PC, click **More info**, then **Run anyway**.
2. Connect your DualSense by cable or Bluetooth.
3. On the Emulation page, pick what games see. DualPulse links the driver a mode needs:
   [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) for Xbox 360 and DualShock 4,
   [usbip-win2](https://github.com/vadimgrn/usbip-win2/releases) for DualSense, and
   [HidHide](https://github.com/nefarius/HidHide/releases) to hide the real controller.

After Bluetooth, the controller's sound over the cable (game haptics and its microphone) stays off until you turn it
off and on again. More: [virtual DualSense](docs/virtual-dualsense.md).

## Build from source

`dotnet build src/DualPulse.slnx -c Release` with the .NET 10 SDK. Details: [BUILDING.md](BUILDING.md).

## Credits

Originally based on DualSenseX by Paliverse; rewritten from scratch. [Third-party notices](THIRD-PARTY-NOTICES.md).
"DualSense" and "PS5" are trademarks of Sony Interactive Entertainment; DualPulse is not affiliated with Sony or
Paliverse. [MIT license](LICENSE) · [Privacy policy](PRIVACY.md).

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).
[Code signing policy](https://elvisgraho.github.io/DualPulse/#code-signing-policy). Applied, pending approval: releases
are not signed yet.
