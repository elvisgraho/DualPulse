namespace DualPulse.Core.Steam;

// What DualPulse can see of Steam (per platform).
public interface ISteamClient
{
	bool IsRunning();

	// Steam's virtual gamepad exists: Steam Input is translating a controller for a running game.
	bool InputActive();

	// The Steam game whose window has focus; null when the focused program isn't one.
	SteamApp? ForegroundGame();
}
