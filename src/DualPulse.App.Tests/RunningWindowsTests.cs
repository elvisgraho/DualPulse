using System.Runtime.Versioning;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

[SupportedOSPlatform("windows")]
public sealed class RunningWindowsTests
{
	[Fact]
	public void List_HasTitledWindows_OnePerExe_WithoutTheExcludedProcess()
	{
		Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
		string ownExe = Path.GetFileName(Environment.ProcessPath!);

		IReadOnlyList<RunningWindow> windows = new RunningWindows().List(Environment.ProcessId);

		Assert.All(windows, w => Assert.False(string.IsNullOrWhiteSpace(w.Title)));
		Assert.All(windows, w => Assert.EndsWith(".exe", w.Exe, StringComparison.OrdinalIgnoreCase));
		Assert.Equal(windows.Count, windows.Select(w => w.Exe.ToLowerInvariant()).Distinct().Count());
		Assert.DoesNotContain(windows, w => string.Equals(w.Exe, ownExe, StringComparison.OrdinalIgnoreCase));
	}
}
