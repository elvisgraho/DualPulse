using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class ControllerManagerTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly ListLogger<ControllerManager> logger = new();

	private readonly ControllerManager manager;

	private readonly List<string> ended = [];

	private int started;

	public ControllerManagerTests()
	{
		manager = new ControllerManager(transport, time, logger);
		manager.SessionStarted += _ => Interlocked.Increment(ref started);
		manager.SessionEnded += (_, reason) => { lock (ended) { ended.Add(reason); } };
	}

	public void Dispose() => manager.Dispose();

	[Fact]
	public void Manager_DeviceAppears_StartsSession()
	{
		manager.Start();
		Assert.Null(manager.Current);

		transport.Add(FakeHidTransport.BluetoothPath);

		Assert.NotNull(manager.Current);
		Assert.Equal(FakeHidTransport.BluetoothPath, manager.Current!.Device.Path);
		Assert.Equal(1, started);
	}

	[Theory]
	[InlineData("DPLS00DUALPULSE-VIRTUAL00", true)]
	[InlineData("DPLS01DUALPULSE-VIRTUAL00", true)]
	[InlineData("DPLS01DUALPULSE-VIRTUAL03", true)]
	[InlineData("E55701GTD1190A500", false)]
	[InlineData(null, false)]
	public void OwnVirtual_MatchesTheSerial_EvenAfterViiperRewritesIt(string? serial, bool own)
	{
		// VIIPER writes the shell-colour code into characters 5-6 and a counter into the last two to keep serials unique.
		Assert.Equal(own, ControllerDiscovery.IsOwnVirtual(new HidDeviceInfo("path", 0x054C, 0x0CE6, 64, 64, 64, serial)));
	}

	[Fact]
	public void Manager_IgnoresDualPulsesOwnVirtualDualSense()
	{
		// The virtual DualSense (USB, same ids) must never be taken for the real controller, even though USB is preferred.
		transport.Add(FakeHidTransport.BluetoothPath);
		transport.Add(@"\\?\hid#vid_054c&pid_0ce6&mi_03#4&virtual#{4d1e55b2-f16f-11cf-88cb-001111000030}", serialNumber: ControllerDiscovery.VirtualSerialNumber);

		manager.Start();

		Assert.Equal(FakeHidTransport.BluetoothPath, manager.Current!.Device.Path);
		Assert.Contains(logger.Entries, e => e.Message.Contains("Controllers present: Bluetooth", StringComparison.Ordinal));
	}

	[Fact]
	public void Manager_PrefersUsbOverBluetooth_WhenBothListed()
	{
		transport.Add(FakeHidTransport.BluetoothPath);
		transport.Add(FakeHidTransport.UsbPath);

		manager.Start();

		Assert.Equal(Transport.Usb, manager.Current!.Transport);
	}

	[Fact]
	public void Manager_UsbAppears_RebindsFromBluetooth()
	{
		transport.Add(FakeHidTransport.BluetoothPath);
		manager.Start();
		ControllerSession bluetooth = manager.Current!;

		transport.Add(FakeHidTransport.UsbPath);

		// USB opens first, then Bluetooth is released: no gap without a controller.
		Assert.Equal(Transport.Usb, manager.Current!.Transport);
		Assert.Contains(ended, r => r.Contains("preferred", StringComparison.OrdinalIgnoreCase));
		Assert.Equal(2, started);
		TestReports.WaitUntil(() => !bluetooth.IsReading);
	}

	[Fact]
	public void Manager_ReaderDies_RebindsAfterOneSecondBackoff()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		manager.Start();
		ControllerSession first = manager.Current!;

		device.Latest.Fail();
		TestReports.WaitUntil(() => !first.IsReading);
		manager.Poll();

		Assert.Null(manager.Current);
		time.Advance(TimeSpan.FromMilliseconds(900));
		Assert.Null(manager.Current);
		time.Advance(TimeSpan.FromMilliseconds(100));
		Assert.NotNull(manager.Current);
		Assert.NotSame(first, manager.Current);
	}

	[Fact]
	public void Manager_DeviceVanishes_EndsSession()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		manager.Start();
		ControllerSession session = manager.Current!;

		transport.Remove(device);

		Assert.Null(manager.Current);
		Assert.Single(ended);
		TestReports.WaitUntil(() => !session.IsReading);
	}

	[Fact]
	public void Manager_TwoControllers_BindsOneStably()
	{
		transport.Add(FakeHidTransport.UsbPath);
		transport.Add(FakeHidTransport.UsbPath.Replace("a&1", "a&2"));
		manager.Start();
		ControllerSession bound = manager.Current!;

		for (int i = 0; i < 5; i++)
		{
			manager.Poll();
			time.Advance(TimeSpan.FromMilliseconds(300));
		}

		Assert.Same(bound, manager.Current);
		Assert.Equal(1, started);
	}

	[Fact]
	public void Manager_OpenFailure_IsReported_And_Retried_After_Backoff()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		device.FailOpen = true;

		manager.Start();

		Assert.Null(manager.Current);
		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning);
		device.FailOpen = false;
		time.Advance(TimeSpan.FromMilliseconds(500));
		Assert.Null(manager.Current);
		time.Advance(TimeSpan.FromMilliseconds(500));
		Assert.NotNull(manager.Current);
	}

	[Fact]
	public void Manager_SessionEvents_DeliveredInOrder_WhenPollsOverlap()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		List<string> order = [];
		manager.SessionStarted += _ =>
		{
			lock (order) { order.Add("started-begin"); }
			// A device-change poll on another thread ends the session while this handler is still running.
			Thread other = new(() => transport.Remove(device));
			other.Start();
			other.Join(300);
			lock (order) { order.Add("started-end"); }
		};
		manager.SessionEnded += (_, _) => { lock (order) { order.Add("ended"); } };

		manager.Start();

		TestReports.WaitUntil(() => { lock (order) { return order.Count == 3; } });
		Assert.Equal(["started-begin", "started-end", "ended"], order);
	}

	[Fact]
	public void Manager_PreferredDeviceUnopenable_KeepsWorkingSession()
	{
		transport.Add(FakeHidTransport.BluetoothPath);
		manager.Start();
		ControllerSession bluetooth = manager.Current!;

		transport.Add(FakeHidTransport.UsbPath, failOpen: true);
		for (int i = 0; i < 8; i++)
		{
			time.Advance(TimeSpan.FromMilliseconds(250));
		}

		Assert.Same(bluetooth, manager.Current);
		Assert.True(bluetooth.IsReading);
		Assert.Empty(ended);
	}

	[Fact]
	public void Manager_RepeatedOpenFailure_LoggedOnce_AndExposed()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath, failOpen: true);

		manager.Start();
		for (int i = 0; i < 20; i++)
		{
			time.Advance(TimeSpan.FromMilliseconds(250));
		}

		Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
		Assert.NotNull(manager.OpenError);
		device.FailOpen = false;
		time.Advance(TimeSpan.FromSeconds(1));
		Assert.NotNull(manager.Current);
		Assert.Null(manager.OpenError);
	}

	[Fact]
	public void Manager_HandlerThrows_IsLoggedAndManagerKeepsWorking()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		manager.SessionStarted += _ => throw new InvalidOperationException("handler bug");

		manager.Start();
		transport.Remove(device);

		Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
		Assert.Single(ended);
		Assert.Null(manager.Current);
	}

	[Fact]
	public void Manager_ControllerListChange_IsLoggedOncePerChange()
	{
		manager.Start();
		FakeDevice usb = transport.Add(FakeHidTransport.UsbPath);
		transport.Add(FakeHidTransport.BluetoothPath);
		manager.Poll();
		transport.Remove(usb);

		List<string> lines = logger.Entries.Where(e => e.Message.StartsWith("Controllers present", StringComparison.Ordinal)).Select(e => e.Message).ToList();
		Assert.Equal(["Controllers present: USB", "Controllers present: USB, Bluetooth", "Controllers present: Bluetooth"], lines);
	}
}
