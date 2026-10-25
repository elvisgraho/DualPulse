using DualPulse.App.Services;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class DiagnosticsTests : IDisposable
{
	private readonly string logs = Directory.CreateTempSubdirectory("dualpulse-diag-").FullName;

	private readonly FakeSettingsService settings = new();

	private readonly FakeControllerService controller = new();

	private readonly FakeEmulationService emulation = new();

	private readonly FakePadBackend backend = new();

	private readonly FakeHidHideService hidHide = new();

	public void Dispose() => Directory.Delete(logs, recursive: true);

	private Diagnostics Create() => new(settings, controller, emulation, backend, hidHide, () => "1.22.0.0", logs, "0.2.0", "Windows 11 (10.0.26200)");

	[Fact]
	public void Report_SaysWhatMatters()
	{
		settings.Update(s => s with { Emulation = PadKind.DualSense, HideController = true });
		emulation.Status = EmulationStatus.Active;
		backend.DualSenseAvailable = false;

		string report = Create().Build();

		Assert.Contains("DualPulse 0.2.0", report);
		Assert.Contains("Windows 11 (10.0.26200)", report);
		Assert.Contains("Controller: not connected", report);
		Assert.Contains("Emulation: DualSense (Active)", report);
		Assert.Contains("Hide my DualSense: on", report);
		Assert.Contains("ViGEmBus: 1.22.0.0", report);
		Assert.Contains("usbip-win2: missing", report);
	}

	[Fact]
	public void Report_EndsWithTheNewestLogsLastLines()
	{
		File.WriteAllLines(Path.Combine(logs, "dualpulse-20261024.log"), ["old day"]);
		File.WriteAllLines(Path.Combine(logs, "dualpulse-20261025.log"), Enumerable.Range(1, 200).Select(i => $"line {i}"));

		string report = Create().Build();

		Assert.Contains("line 200", report);
		Assert.DoesNotContain("line 100\n", report.ReplaceLineEndings("\n"));
		Assert.DoesNotContain("old day", report);
	}

	[Fact]
	public void Report_LeavesOutBluetoothAddressesAndUserNames()
	{
		File.WriteAllLines(Path.Combine(logs, "dualpulse-20261025.log"),
		[
			"Power off (PS+Triangle) 12:34:56:78:9A:BC: disconnected",
			@"HidHide: allowed \Device\HarddiskVolume3\Users\jane.doe\Apps\DualPulse.exe",
			@"Content root path: C:\Users\jane.doe\Apps\",
		]);

		string report = Create().Build();

		Assert.DoesNotContain("12:34:56:78:9A:BC", report);
		Assert.DoesNotContain("jane.doe", report);
		Assert.Contains(@"\Users\<user>\Apps", report);
	}
}
