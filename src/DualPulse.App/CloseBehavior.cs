using Avalonia.Controls;

namespace DualPulse.App;

internal static class CloseBehavior
{
	// Only the user closing the window hides it to the tray. Windows signing out or shutting down, an app shutdown
	// or Quit close it for real, so the exit cleanup (power-off on exit, neutral controller output) runs.
	public static bool HideToTray(WindowCloseReason reason, bool isProgrammatic, bool quitting)
	{
		return reason == WindowCloseReason.WindowClosing && !isProgrammatic && !quitting;
	}
}
