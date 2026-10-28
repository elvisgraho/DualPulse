using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using DualPulse.Core.Steam;

namespace DualPulse.Emulation;

// Steam on Windows: running is the steam process; Steam Input is active while Steam's virtual gamepad (USB
// 28DE:11FF) is a present device; the focused game comes from the focused window's exe path and Steam's library
// files (found through HKCU\Software\Valve\Steam\SteamPath; the installed games are read again every 30 s).
[SupportedOSPlatform("windows")]
public sealed partial class WindowsSteamClient : ISteamClient
{
	private const string VirtualGamepad = "VID_28DE&PID_11FF";

	private const uint PresentOnly = 0x100;

	private static readonly TimeSpan LibraryLifetime = TimeSpan.FromSeconds(30);

	private readonly object gate = new();

	private List<(string Library, SteamApp App)> installed = [];

	private long? readAt;

	public bool IsRunning()
	{
		Process[] steam = Process.GetProcessesByName("steam");
		foreach (Process process in steam)
		{
			process.Dispose();
		}
		return steam.Length > 0;
	}

	public bool InputActive()
	{
		if (CM_Get_Device_ID_List_SizeW(out uint length, null, PresentOnly) != 0 || length == 0)
		{
			return false;
		}
		char[] ids = new char[length];
		return CM_Get_Device_ID_ListW(null, ids, length, PresentOnly) == 0
			&& new string(ids).Contains(VirtualGamepad, StringComparison.OrdinalIgnoreCase);
	}

	public SteamApp? ForegroundGame()
	{
		uint pid = WindowNative.ProcessOf(WindowNative.GetForegroundWindow());
		string? path = pid == 0 ? null : WindowNative.FullPathOfProcess(pid);
		return path == null ? null : GameAt(path);
	}

	// The installed Steam game an exe belongs to; null for anything else.
	public SteamApp? GameAt(string exePath) => SteamLibrary.GameFor(exePath, Installed());

	private List<(string, SteamApp)> Installed()
	{
		lock (gate)
		{
			if (readAt is { } at && Stopwatch.GetElapsedTime(at) < LibraryLifetime)
			{
				return installed;
			}
			readAt = Stopwatch.GetTimestamp();
			installed = ReadLibrary();
			return installed;
		}
	}

	private static List<(string, SteamApp)> ReadLibrary()
	{
		List<(string, SteamApp)> games = [];
		try
		{
			if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is not string steamPath)
			{
				return games;
			}
			string folders = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
			IEnumerable<string> libraries = File.Exists(folders) ? SteamLibrary.LibraryPaths(File.ReadAllText(folders)) : [steamPath];
			foreach (string library in libraries.Append(steamPath).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				string apps = Path.Combine(library, "steamapps");
				if (!Directory.Exists(apps))
				{
					continue;
				}
				foreach (string manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
				{
					if (SteamLibrary.ParseManifest(File.ReadAllText(manifest)) is { } app)
					{
						games.Add((library, app));
					}
				}
			}
		}
		catch (Exception)
		{
		}
		return games;
	}

	[LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int CM_Get_Device_ID_List_SizeW(out uint length, string? filter, uint flags);

	[LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int CM_Get_Device_ID_ListW(string? filter, [Out] char[] buffer, uint length, uint flags);
}
