using DualPulse.Core.Protocol;

namespace DualPulse.Core.Mods;

public enum ModTrigger
{
	Left = 1,
	Right = 2
}

// One instruction from a game mod. ControllerIndex is kept for logging; DualPulse drives one controller.
public abstract record ModInstruction(int ControllerIndex);

public sealed record ModStatusRequest() : ModInstruction(0);

// Trigger is null for "both" (legacy packets with an invalid trigger reset both triggers).
public sealed record ModTriggerEffect(int ControllerIndex, ModTrigger? Trigger, TriggerEffect Effect, string Description) : ModInstruction(ControllerIndex);

public sealed record ModLightbar(int ControllerIndex, Rgb Colour) : ModInstruction(ControllerIndex);

// Bits follow OutputState.PlayerLeds: bit 0 is the leftmost LED.
public sealed record ModPlayerLeds(int ControllerIndex, byte Bits) : ModInstruction(ControllerIndex);

public sealed record ModMicLed(int ControllerIndex, MicLed MicLed) : ModInstruction(ControllerIndex);

// When the virtual pad should report the trigger as pressed (0-255).
public sealed record ModTriggerThreshold(int ControllerIndex, ModTrigger? Trigger, byte Threshold) : ModInstruction(ControllerIndex);

// Give control back to the user's own settings.
public sealed record ModReset(int ControllerIndex) : ModInstruction(ControllerIndex);

// What the status reply reports about the bound controller.
public sealed record DsxStatus(bool ControllerConnected, int BatteryLevel, string? MacAddress, bool Bluetooth, DateTime TimeReceived);
