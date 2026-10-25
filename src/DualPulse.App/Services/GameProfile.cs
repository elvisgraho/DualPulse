namespace DualPulse.App.Services;

// Per-game settings, applied while the game's window has focus. Exe is the file name (e.g. "game.exe"), matched
// case-insensitively. A part left null follows the saved settings.
public sealed record GameProfile(string Name, string Exe, TriggerSettings? Triggers = null, GyroSettings? Gyro = null);
