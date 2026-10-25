using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class BatteryAlertServiceTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new();

	private readonly FakeNotifier notifier = new();

	private readonly ControllerService controller;

	private readonly BatteryAlertService alerts;

	public BatteryAlertServiceTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		alerts = new BatteryAlertService(controller, notifier, time);
	}

	public void Dispose()
	{
		alerts.Dispose();
		controller.Dispose();
	}

	// Feeds a USB report with the given battery status byte (low nibble: level in tens, high nibble: charging state).
	private async Task<FakeConnection> ConnectWithBatteryAsync(byte status)
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		Feed(device.Latest, status);
		return device.Latest;
	}

	private void Feed(FakeConnection connection, byte status)
	{
		byte[] report = TestReports.Fixture("idle-usb");
		report[1 + 52] = status;
		connection.Feed(report);
		int level = Math.Min(status & 0x0F, 10) * 10;
		TestReports.WaitUntil(() => controller.Session!.Battery.Level == level);
	}

	[Fact]
	public async Task LowBattery_WarnsOnce()
	{
		await ConnectWithBatteryAsync(0x02);

		time.Advance(BatteryAlertService.CheckInterval);
		TestReports.WaitUntil(() => notifier.Count == 1);
		time.Advance(BatteryAlertService.CheckInterval);
		Thread.Sleep(100);

		(string title, string message) = Assert.Single(notifier.Shown);
		Assert.Equal("Controller battery", title);
		Assert.Contains("20%", message);
	}

	[Fact]
	public async Task CriticalAfterLow_WarnsAgain()
	{
		FakeConnection connection = await ConnectWithBatteryAsync(0x02);
		time.Advance(BatteryAlertService.CheckInterval);
		TestReports.WaitUntil(() => notifier.Count == 1);

		Feed(connection, 0x01);
		time.Advance(BatteryAlertService.CheckInterval);

		// The fake clock's timer callback can finish on another thread just after Advance returns.
		TestReports.WaitUntil(() => notifier.Count == 2);
		Assert.Contains("critical", notifier.Shown[1].Message);
	}

	[Fact]
	public async Task Charging_NeverWarns()
	{
		await ConnectWithBatteryAsync(0x11);

		time.Advance(BatteryAlertService.CheckInterval);

		Assert.Empty(notifier.Shown);
	}

	[Fact]
	public void NoController_NeverWarns()
	{
		time.Advance(BatteryAlertService.CheckInterval);

		Assert.Empty(notifier.Shown);
	}
}
