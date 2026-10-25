namespace DualPulse.Emulation;

// The exe file name (e.g. "game.exe") of the program whose window has focus; null when it can't be read.
public interface IForegroundWatcher
{
	string? ForegroundExe { get; }

	// Raised on the watcher's own thread with the new exe.
	event Action<string?>? Changed;
}
