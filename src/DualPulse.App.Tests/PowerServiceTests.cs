using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class PowerServiceTests
{
	private const long ControllerMac = 0x123456789ABC;

	// The idle Bluetooth capture with PS and Triangle held.
	private static byte[] PsTriangleBluetooth()
	{
		byte[] report = TestReports.Fixture("idle-bt");
		report[2 + 7] |= 0x80;
		report[2 + 9] |= 0x01;
		return report;
	}

	[Fact]
	public async Task Power_ShortcutCallsPowerOffOnce()
	{
		FakeHidTransport transport = new();
		FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
		FakeSettingsService settings = new();
		using ControllerService controller = new(transport, time, settings, NullLogger<ControllerManager>.Instance);
		List<long> poweredOff = [];
		using PowerService power = new(controller, settings, time, NullLogger<PowerService>.Instance, mac =>
		{
			lock (poweredOff)
			{
				poweredOff.Add(mac);
			}
			return true;
		});
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		FakeConnection connection = device.Latest;
		connection.OnRead = () => time.Advance(TimeSpan.FromMilliseconds(20));

		for (int i = 0; i < 10; i++)
		{
			connection.Feed(PsTriangleBluetooth());
		}

		TestReports.WaitUntil(() => connection.PendingReports == 0);
		TestReports.WaitUntil(() => { lock (poweredOff) { return poweredOff.Count == 1; } });
		Thread.Sleep(200);
		lock (poweredOff)
		{
			Assert.Equal([ControllerMac], poweredOff);
		}
	}

	[Fact]
	public async Task Power_PowerOffNow_UsbSession_ReturnsFalse()
	{
		FakeHidTransport transport = new();
		FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
		FakeSettingsService settings = new();
		using ControllerService controller = new(transport, time, settings, NullLogger<ControllerManager>.Instance);
		int calls = 0;
		using PowerService power = new(controller, settings, time, NullLogger<PowerService>.Instance, _ => ++calls > 0);
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		Assert.False(power.PowerOffNow("test"));
		Assert.Equal(0, calls);
	}
}
