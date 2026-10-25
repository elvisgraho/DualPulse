using System.Runtime.InteropServices;

namespace DualPulse.Core.Devices;

// 1 ms Windows timer resolution while something needs even pacing (the haptics stream); the default 15.6 ms makes a
// 10.67 ms cadence impossible. Begin and End must be paired. Elsewhere the default timers are already fine-grained,
// so this does nothing.
internal static partial class TimerResolution
{
	public static void Begin()
	{
		if (OperatingSystem.IsWindows())
		{
			TimeBeginPeriod(1);
		}
	}

	public static void End()
	{
		if (OperatingSystem.IsWindows())
		{
			TimeEndPeriod(1);
		}
	}

	[LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
	private static partial uint TimeBeginPeriod(uint period);

	[LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
	private static partial uint TimeEndPeriod(uint period);
}
