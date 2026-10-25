using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DualPulse.Emulation;

// Reports the exe of the focused window through a WinEvent hook on its own thread with a message loop; nothing runs
// while focus doesn't change. An exe that can't be read (elevated or protected process) is reported as null.
[SupportedOSPlatform("windows")]
public sealed unsafe class ForegroundWatcher : IForegroundWatcher, IDisposable
{
	// The hook callback runs on the watcher thread, which owns exactly one watcher.
	[ThreadStatic]
	private static ForegroundWatcher? current;

	private readonly Thread thread;

	private readonly ManualResetEventSlim started = new();

	private uint threadId;

	public ForegroundWatcher()
	{
		ForegroundExe = WindowNative.ExeOf(WindowNative.GetForegroundWindow());
		thread = new Thread(Run) { IsBackground = true, Name = "DualPulse foreground watcher" };
		thread.Start();
		started.Wait(TimeSpan.FromSeconds(5));
	}

	public string? ForegroundExe { get; private set; }

	// False when the hook couldn't be installed; profiles then never activate.
	public bool IsWatching { get; private set; }

	public event Action<string?>? Changed;

	public void Dispose()
	{
		if (threadId != 0)
		{
			WindowNative.PostThreadMessageW(threadId, WindowNative.WmQuit, 0, 0);
		}
		thread.Join(TimeSpan.FromSeconds(2));
	}

	private void Run()
	{
		current = this;
		threadId = WindowNative.GetCurrentThreadId();
		nint hook = WindowNative.SetWinEventHook(WindowNative.EventSystemForeground, WindowNative.EventSystemForeground, 0, &OnEvent, 0, 0, WindowNative.WinEventOutOfContext);
		IsWatching = hook != 0;
		// A switch between the constructor's read and the hook going in would otherwise be missed.
		Report(WindowNative.GetForegroundWindow());
		started.Set();
		if (hook == 0)
		{
			return;
		}
		try
		{
			while (WindowNative.GetMessageW(out WindowNative.Msg message, 0, 0, 0) > 0)
			{
				WindowNative.DispatchMessageW(message);
			}
		}
		finally
		{
			WindowNative.UnhookWinEvent(hook);
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static void OnEvent(nint hook, uint eventType, nint hwnd, int objectId, int childId, uint eventThread, uint time)
	{
		current?.Report(hwnd);
	}

	private void Report(nint hwnd)
	{
		// This runs inside a native callback: an exception escaping here would end the process.
		try
		{
			string? exe = WindowNative.ExeOf(hwnd);
			if (!string.Equals(exe, ForegroundExe, StringComparison.OrdinalIgnoreCase))
			{
				ForegroundExe = exe;
				Changed?.Invoke(exe);
			}
		}
		catch (Exception)
		{
		}
	}
}
