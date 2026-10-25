using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DualPulse.Emulation;

// Win32 calls shared by the foreground watcher and the window list.
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowNative
{
	public const uint EventSystemForeground = 0x0003;

	public const uint WinEventOutOfContext = 0x0000;

	public const uint WmQuit = 0x0012;

	private const uint ProcessQueryLimitedInformation = 0x1000;

	private const int GwOwner = 4;

	private const int GwlExStyle = -20;

	private const long WsExToolWindow = 0x80;

	private const uint DwmCloaked = 14;

	[StructLayout(LayoutKind.Sequential)]
	public struct Msg
	{
		public nint Hwnd;

		public uint Message;

		public nint WParam;

		public nint LParam;

		public uint Time;

		public int X;

		public int Y;
	}

	public static uint ProcessOf(nint hwnd)
	{
		GetWindowThreadProcessId(hwnd, out uint pid);
		return pid;
	}

	// The exe file name of the window's process, or null when the process can't be opened (elevated, protected).
	public static string? ExeOf(nint hwnd)
	{
		uint pid = ProcessOf(hwnd);
		return pid == 0 ? null : ExeOfProcess(pid);
	}

	public static string? ExeOfProcess(uint pid)
	{
		nint process = OpenProcess(ProcessQueryLimitedInformation, 0, pid);
		if (process == 0)
		{
			return null;
		}
		try
		{
			char* buffer = stackalloc char[1024];
			int size = 1024;
			return QueryFullProcessImageNameW(process, 0, buffer, ref size) != 0 ? Path.GetFileName(new string(buffer, 0, size)) : null;
		}
		finally
		{
			CloseHandle(process);
		}
	}

	// A window a person would call "a program": visible, titled, not owned, not a tool window, not cloaked.
	public static bool IsAppWindow(nint hwnd, out string title)
	{
		title = "";
		if (IsWindowVisible(hwnd) == 0 || GetWindow(hwnd, GwOwner) != 0 || (GetWindowLongPtrW(hwnd, GwlExStyle) & WsExToolWindow) != 0)
		{
			return false;
		}
		int cloaked = 0;
		if (DwmGetWindowAttribute(hwnd, DwmCloaked, &cloaked, sizeof(int)) == 0 && cloaked != 0)
		{
			return false;
		}
		char* buffer = stackalloc char[256];
		int length = GetWindowTextW(hwnd, buffer, 256);
		title = new string(buffer, 0, length).Trim();
		return title.Length > 0;
	}

	[LibraryImport("user32.dll")]
	public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
		delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void> callback, uint processId, uint threadId, uint flags);

	[LibraryImport("user32.dll")]
	public static partial int UnhookWinEvent(nint hook);

	[LibraryImport("user32.dll")]
	public static partial int GetMessageW(out Msg message, nint hwnd, uint min, uint max);

	[LibraryImport("user32.dll")]
	public static partial nint DispatchMessageW(in Msg message);

	[LibraryImport("user32.dll")]
	public static partial int PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);

	[LibraryImport("kernel32.dll")]
	public static partial uint GetCurrentThreadId();

	[LibraryImport("user32.dll")]
	public static partial nint GetForegroundWindow();

	[LibraryImport("user32.dll")]
	public static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint lParam);

	[LibraryImport("user32.dll")]
	private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

	[LibraryImport("kernel32.dll")]
	private static partial nint OpenProcess(uint access, int inherit, uint processId);

	[LibraryImport("kernel32.dll")]
	private static partial int CloseHandle(nint handle);

	[LibraryImport("kernel32.dll")]
	private static partial int QueryFullProcessImageNameW(nint process, uint flags, char* name, ref int size);

	[LibraryImport("user32.dll")]
	private static partial int IsWindowVisible(nint hwnd);

	[LibraryImport("user32.dll")]
	private static partial nint GetWindow(nint hwnd, int command);

	[LibraryImport("user32.dll")]
	private static partial long GetWindowLongPtrW(nint hwnd, int index);

	[LibraryImport("user32.dll")]
	private static partial int GetWindowTextW(nint hwnd, char* text, int max);

	[LibraryImport("dwmapi.dll")]
	private static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, int size);
}
