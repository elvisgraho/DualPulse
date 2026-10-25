# Third-party notices

DualPulse uses the following open-source components. Each is used under its own license; the full license text is
at the linked address.

| Component | License | Source |
| --- | --- | --- |
| Avalonia (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter) | MIT | https://github.com/AvaloniaUI/Avalonia |
| Inter typeface (bundled by Avalonia.Fonts.Inter) | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| HidSharp | Apache License 2.0 | https://www.zer7.com/software/hidsharp |
| Nefarius.ViGEm.Client | MIT | https://github.com/nefarius/ViGEm.NET |
| Microsoft.Extensions.Hosting, Microsoft.Extensions.Logging.Abstractions, System.IO.Hashing | MIT | https://github.com/dotnet/runtime |
| NAudio.Wasapi (Windows system-audio capture for haptics) | MIT | https://github.com/naudio/NAudio |
| Concentus (Opus encoder for controller speaker and headset audio) | BSD 3-Clause | https://github.com/lostromb/concentus |

Not bundled, installed separately by the user:

| Component | License | Source |
| --- | --- | --- |
| ViGEmBus driver (needed for Xbox 360 / DualShock 4 emulation) | BSD 3-Clause | https://github.com/nefarius/ViGEmBus |
| usbip-win2 driver (needed for the virtual DualSense) | BSD 2-Clause | https://github.com/vadimgrn/usbip-win2 |

Protocol references (facts only, no code copied): the Linux `hid-playstation` driver documentation of the DualSense
report layouts, and Nielk1's DualSense adaptive trigger effect documentation
(https://gist.github.com/Nielk1/6d54cc2c00d2201ccb8c2720ad7538db).

Bluetooth haptics (report 0x32 layout, sample format, pacing and the haptics-mode setup values), facts only, no code
copied: SAxense by Egor Vorontsov (MPL-2.0, https://github.com/egormanga/SAxense), the dualsense-bridge README
(https://github.com/vingt-2/dualsense-bridge) and AnyPS5 issue #2056
(https://github.com/boykopovar/AnyPS5/issues/2056).

Bluetooth speaker and headset audio (report 0x39 layout, flag mask, buffer length, Opus settings, audio control
values), facts only, no code copied: the ds5bridge protocol notes (https://github.com/snooptheone/ds5bridge),
DS5Dongle (MPL-2.0, https://github.com/awalol/DS5Dongle) and KytyPS5 pull request #148
(https://github.com/chenxiao07/KytyPS5/pull/148).

Virtual DualSense over USB/IP, facts only, no code copied: the Linux kernel's USB/IP protocol documentation
(Documentation/usb/usbip_protocol.rst), usbip-win2's handling of full-speed intervals and isochronous start frames
(BSD-2-Clause, https://github.com/vadimgrn/usbip-win2), and VIIPER's observed device behaviour (frame-paced
isochronous completions, full-speed endpoint intervals; GPL-3.0, https://github.com/Alia5/VIIPER). The USB
descriptors and feature reports were read from a real DualSense.
