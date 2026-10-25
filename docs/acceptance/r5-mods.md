# R5 Mods page (DSX-compatible mod server) acceptance

Hardware: DualSense, firmware 06.30, over USB, Xbox 360 emulation. Mod simulated with `DualPulse.Probe mod-demo`
(sends DSX packets to 127.0.0.1:6969 and prints the replies). Date: 2026-10-23.

| Check | Result |
| --- | --- |
| Server on: status "Listening on 127.0.0.1:6969" | Pass |
| Step 1: R2 weapon click, L2 bow, lightbar green, all player LEDs, mic LED pulse | Pass |
| Step 2: R2 machine-gun vibration (legacy mode 17), lightbar red | Pass |
| Step 3: reset instruction returns everything to the user's own settings | Pass |
| Every packet answered with the DSX status reply (connected, USB, battery 70%) | Pass |
| Activity list shows the instructions in plain words | Pass |

Covered by tests rather than by hand: all 27 trigger modes and 8 instruction types, numbers/strings/booleans as
parameters, malformed packets counted and still answered, the override surviving user setting changes and a
reconnect, "Give control back", turning the server off, port files, and a port already in use.

Known gap: the reply's MacAddress is empty (mods only display it). Trigger threshold instructions are accepted but
have no effect, because Xbox 360 triggers are analog.
