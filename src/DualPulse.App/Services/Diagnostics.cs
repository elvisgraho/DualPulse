using System.Text;
using System.Text.RegularExpressions;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Emulation;

namespace DualPulse.App.Services;

// The text behind Settings' "Copy diagnostics", for bug reports: version, Windows, controller, mode, drivers, the
// settings that change behaviour most, and the end of today's log. Bluetooth addresses and the Windows user name (in
// paths) are left out.
public sealed partial class Diagnostics(ISettingsService settings, IControllerService controller, IEmulationService emulation,
	IVirtualPadBackend backend, IHidHideService hidHide, Func<string?> viGEmBusVersion, string logsFolder, string version, string windows)
{
	private const int LogLines = 80;

	public string Build()
	{
		AppSettings current = settings.Current;
		StringBuilder text = new();
		text.AppendLine($"DualPulse {version} on {windows}");
		ControllerSession? session = controller.Session;
		text.AppendLine(session == null
			? $"Controller: not connected{(controller.OpenError != null ? $" ({controller.OpenError})" : "")}"
			: $"Controller: {(session.Transport == Transport.Bluetooth ? "Bluetooth" : "USB")}, {session.Firmware?.Summary ?? "firmware unknown"}");
		string mode = current.Emulation switch
		{
			PadKind.Xbox360 => "Xbox 360",
			PadKind.DualShock4 => "DualShock 4",
			PadKind.DualSense => "DualSense",
			_ => "Native"
		};
		text.AppendLine($"Emulation: {mode} ({emulation.Status})");
		text.AppendLine($"Hide my DualSense: {OnOff(current.HideController)} (HidHide {hidHide.Status})");
		text.AppendLine($"ViGEmBus: {(backend.IsAvailable ? viGEmBusVersion() ?? "installed" : "missing")}");
		text.AppendLine($"usbip-win2: {(backend.IsAvailableFor(PadKind.DualSense) ? "installed" : "missing")}");
		text.AppendLine($"Sound on controller: {OnOff(current.EffectiveAudio.Enabled)}, feel PC sound: {OnOff(current.EffectiveRumble.AudioHaptics)}, "
			+ $"game sound on controller: {OnOff(current.GameSoundOnController)}");
		text.AppendLine();
		text.AppendLine("Log:");
		foreach (string line in LastLogLines())
		{
			text.AppendLine(line);
		}
		return Redact(text.ToString());
	}

	public static string Redact(string text)
	{
		text = MacAddress().Replace(text, "<address>");
		return UserFolder().Replace(text, m => $"{m.Groups[1].Value}Users{m.Groups[1].Value}<user>{m.Groups[1].Value}");
	}

	private IEnumerable<string> LastLogLines()
	{
		try
		{
			string? newest = Directory.Exists(logsFolder)
				? Directory.GetFiles(logsFolder, "dualpulse-*.log").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault()
				: null;
			if (newest == null)
			{
				return ["(no log yet)"];
			}
			// The log is appended to while DualPulse runs, so it is read without locking it.
			using FileStream stream = new(newest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using StreamReader reader = new(stream);
			Queue<string> last = new(LogLines);
			while (reader.ReadLine() is { } line)
			{
				if (last.Count == LogLines)
				{
					last.Dequeue();
				}
				last.Enqueue(line);
			}
			return last;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return [$"(log unreadable: {exception.Message})"];
		}
	}

	private static string OnOff(bool value) => value ? "on" : "off";

	[GeneratedRegex(@"\b[0-9A-Fa-f]{2}([:-])(?:[0-9A-Fa-f]{2}\1){4}[0-9A-Fa-f]{2}\b")]
	private static partial Regex MacAddress();

	[GeneratedRegex(@"([\\/])Users[\\/][^\\/\r\n]+[\\/]", RegexOptions.IgnoreCase)]
	private static partial Regex UserFolder();
}
