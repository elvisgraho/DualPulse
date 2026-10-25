using System.Diagnostics;

namespace DualPulse.Emulation;

// Plugs USB/IP devices in through usbip-win2's command-line tool (usbip.exe, installed with its driver):
// "usbip -t PORT attach -r 127.0.0.1 -b BUS --once --terse" prints the virtual hub port, "usbip detach -p PORT" unplugs.
// --once: if DualPulse stops serving, Windows lets the device go instead of reconnecting to it.
// While usbip.exe isn't found, every availability check looks again, so installing usbip-win2 needs no restart.
public sealed class UsbIpWinAttacher(Func<string?> locate) : IUsbIpAttacher
{
	private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

	private string? tool;

	public UsbIpWinAttacher() : this(() => Find(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetEnvironmentVariable("PATH")))
	{
	}

	public UsbIpWinAttacher(string? tool) : this(() => tool)
	{
	}

	public bool IsAvailable => Tool != null;

	public string? UnavailableReason => Tool == null ? "usbip-win2 isn't installed (github.com/vadimgrn/usbip-win2)." : null;

	private string? Tool => tool ??= locate();

	public int Attach(int port, string busId)
	{
		(string output, string errors, int exitCode) = Run($"-t {port} attach -r 127.0.0.1 -b {busId} --once --terse");
		return HubPort(output, exitCode, errors);
	}

	public void Detach(int handle)
	{
		(string output, string errors, int exitCode) = Run($"detach -p {handle}");
		if (exitCode != 0)
		{
			throw new UsbIpException($"usbip detach failed: {(errors + output).Trim()}");
		}
	}

	// usbip.exe in the install folder, otherwise the first one on the search path.
	public static string? Find(string? programFiles, string? path)
	{
		IEnumerable<string> folders = [
			.. string.IsNullOrEmpty(programFiles) ? [] : new[] { Path.Combine(programFiles, "USBip") },
			.. (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)];
		return folders.Select(folder => Path.Combine(folder.Trim('"'), "usbip.exe")).FirstOrDefault(File.Exists);
	}

	// What --terse attach prints on standard output: the hub port alone. Warnings on standard error don't count;
	// on failure they (or the output) are the tool's complaint.
	public static int HubPort(string output, int exitCode, string errors = "")
	{
		if (exitCode == 0 && int.TryParse(output.Trim(), out int port))
		{
			return port;
		}
		string said = (errors + output).Trim();
		throw new UsbIpException($"usbip attach failed: {(said.Length > 0 ? said : "no port reported")}");
	}

	// Every failure (the tool missing or refusing to start, a hang) is a UsbIpException.
	private (string Output, string Errors, int ExitCode) Run(string arguments)
	{
		if (Tool is not { } path)
		{
			throw new UsbIpException(UnavailableReason!);
		}
		Process process;
		try
		{
			process = Process.Start(new ProcessStartInfo(path, arguments)
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			}) ?? throw new UsbIpException("usbip didn't start.");
		}
		catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
		{
			throw new UsbIpException($"usbip didn't start: {error.Message}", error);
		}
		using (process)
		{
			Task<string> output = process.StandardOutput.ReadToEndAsync();
			Task<string> errors = process.StandardError.ReadToEndAsync();
			if (!process.WaitForExit(CommandTimeout))
			{
				try
				{
					process.Kill();
				}
				catch (InvalidOperationException)
				{
					// It exited just now.
				}
				throw new UsbIpException($"usbip {arguments.Split(' ')[0]} took longer than {CommandTimeout.TotalSeconds:0} s.");
			}
			return (output.Result, errors.Result, process.ExitCode);
		}
	}
}
