using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DualPulse.Emulation;

public sealed record RunningWindow(string Title, string Exe);

public interface IRunningWindows
{
	// Programs with a window right now, one per exe, sorted by title; excludePid is left out (DualPulse itself).
	IReadOnlyList<RunningWindow> List(int excludePid);
}

[SupportedOSPlatform("windows")]
public sealed unsafe class RunningWindows : IRunningWindows
{
	public IReadOnlyList<RunningWindow> List(int excludePid)
	{
		List<nint> handles = [];
		GCHandle list = GCHandle.Alloc(handles);
		try
		{
			WindowNative.EnumWindows(&Collect, GCHandle.ToIntPtr(list));
		}
		finally
		{
			list.Free();
		}
		Dictionary<string, RunningWindow> byExe = new(StringComparer.OrdinalIgnoreCase);
		foreach (nint hwnd in handles)
		{
			if (WindowNative.ProcessOf(hwnd) == (uint)excludePid || !WindowNative.IsAppWindow(hwnd, out string title))
			{
				continue;
			}
			if (WindowNative.ExeOf(hwnd) is { } exe && !byExe.ContainsKey(exe))
			{
				byExe[exe] = new RunningWindow(title, exe);
			}
		}
		return [.. byExe.Values.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)];
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int Collect(nint hwnd, nint state)
	{
		((List<nint>)GCHandle.FromIntPtr(state).Target!).Add(hwnd);
		return 1;
	}
}
