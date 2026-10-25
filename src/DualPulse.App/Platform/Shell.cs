using System.Diagnostics;

namespace DualPulse.App.Platform;

// Opens a web page, file or folder the way the user expects. Folders go to Explorer directly: opening a folder path
// through file associations can fail or do nothing.
public static class Shell
{
	public static void Open(string target)
	{
		if (OperatingSystem.IsWindows() && Directory.Exists(target))
		{
			Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { target }, UseShellExecute = false })?.Dispose();
			return;
		}
		Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
	}
}
