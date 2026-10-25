using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class HidHideServiceTests : IDisposable
{
	private const string App = @"\Device\HarddiskVolume3\Tools\DualPulse.exe";

	private const string OtherApp = @"\Device\HarddiskVolume3\Games\Other.exe";

	private const string OtherDevice = @"HID\VID_1234&PID_5678\1&2&3";

	// DualPulse's own virtual DualSense: same ids as a real one, told apart by its serial.
	private const string VirtualPath = @"\\?\hid#vid_054c&pid_0ce6&mi_03#4&virtual#{4d1e55b2-f16f-11cf-88cb-001111000030}";

	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new();

	private readonly FakeHidHideDriver driver = new();

	private readonly ControllerService controller;

	private readonly HidHideService service;

	public HidHideServiceTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		service = new HidHideService(driver, transport, controller, settings, InstanceIdOf, App, NullLogger<HidHideService>.Instance);
	}

	public void Dispose()
	{
		service.Dispose();
		controller.Dispose();
	}

	// Stand-in for the device-instance lookup: USB and Bluetooth paths map to fixed instance ids.
	private static string? InstanceIdOf(string path) => ControllerDiscovery.IsBluetooth(path) ? "ID-BT" : "ID-USB";

	[Fact]
	public void Hide_AllowsDualPulseFirst_ThenHidesControllers_KeepingOtherEntries()
	{
		driver.AllowedApps = [OtherApp];
		driver.HiddenDevices = [OtherDevice];
		transport.Add(FakeHidTransport.UsbPath);
		transport.Add(FakeHidTransport.BluetoothPath);

		settings.Update(s => s with { HideController = true });

		Assert.Equal([OtherApp, App], driver.AllowedApps);
		Assert.Equal([OtherDevice, "ID-USB", "ID-BT"], driver.HiddenDevices);
		Assert.True(driver.Active);
		Assert.Equal(["SetAllowedApps", "SetHiddenDevices", "SetActive"], driver.Writes);
		Assert.Equal(["ID-USB", "ID-BT"], settings.Current.HiddenDeviceIds);
		Assert.Equal(HidHideStatus.Hidden, service.Status);
	}

	[Fact]
	public void Hide_NeverHidesDualPulsesOwnVirtualDualSense()
	{
		// Hiding it too left games with no controller at all.
		transport.Add(FakeHidTransport.BluetoothPath);
		transport.Add(VirtualPath, serialNumber: ControllerDiscovery.VirtualSerialNumber);

		settings.Update(s => s with { HideController = true });

		Assert.Equal(["ID-BT"], driver.HiddenDevices);
		Assert.Equal(["ID-BT"], settings.Current.HiddenDeviceIds);
	}

	[Fact]
	public void Hide_UnhidesDualPulsesOwnVirtualDualSense_IfAnEarlierVersionHidIt()
	{
		driver.HiddenDevices = [OtherDevice, "ID-USB"];
		transport.Add(FakeHidTransport.BluetoothPath);
		transport.Add(VirtualPath, serialNumber: ControllerDiscovery.VirtualSerialNumber);

		// Hiding still on, with the virtual pad recorded as hidden by DualPulse.
		settings.Update(s => s with { HideController = true, HiddenDeviceIds = ["ID-USB"] });

		Assert.Equal([OtherDevice, "ID-BT"], driver.HiddenDevices);
		Assert.Equal(["ID-BT"], settings.Current.HiddenDeviceIds);
	}

	[Fact]
	public void VirtualDualSensePluggedInAfterHiding_IsTakenOffTheListThen()
	{
		// At startup hiding runs when the controller connects; the virtual pad is plugged in moments later.
		driver.HiddenDevices = [OtherDevice, "ID-USB"];
		transport.Add(FakeHidTransport.BluetoothPath);
		settings.Update(s => s with { HideController = true, HiddenDeviceIds = ["ID-USB"] });

		transport.Add(VirtualPath, serialNumber: ControllerDiscovery.VirtualSerialNumber);

		TestReports.WaitUntil(() => driver.HiddenDevices.SequenceEqual([OtherDevice, "ID-BT"]));
	}

	[Fact]
	public void Unhide_RemovesOnlyTheDevicesDualPulseHid()
	{
		driver.AllowedApps = [OtherApp, App];
		driver.HiddenDevices = [OtherDevice, "ID-USB"];
		settings.Update(s => s with { HideController = true, HiddenDeviceIds = ["ID-USB"] });
		driver.Writes.Clear();

		settings.Update(s => s with { HideController = false });

		Assert.Equal([OtherDevice], driver.HiddenDevices);
		Assert.Equal([OtherApp, App], driver.AllowedApps);
		Assert.Empty(settings.Current.HiddenDeviceIds ?? []);
		Assert.Equal(HidHideStatus.Off, service.Status);
	}

	[Fact]
	public async Task Startup_RestoresDevicesLeftHiddenByACrash()
	{
		driver.HiddenDevices = [OtherDevice, "ID-USB"];
		FakeSettingsService crashed = new(new AppSettings(HideController: false, HiddenDeviceIds: ["ID-USB"]));
		using HidHideService fresh = new(driver, transport, controller, crashed, InstanceIdOf, App, NullLogger<HidHideService>.Instance);

		await fresh.StartAsync(TestContext.Current.CancellationToken);

		Assert.Equal([OtherDevice], driver.HiddenDevices);
	}

	[Fact]
	public void NotInstalled_ReportsStatus_AndTouchesNothing()
	{
		driver.Installed = false;
		transport.Add(FakeHidTransport.UsbPath);

		settings.Update(s => s with { HideController = true });

		Assert.Equal(HidHideStatus.NotInstalled, service.Status);
		Assert.Empty(driver.Writes);
	}

	[Fact]
	public void DriverError_ReportsStatus_WithoutThrowing()
	{
		driver.Fail = true;
		transport.Add(FakeHidTransport.UsbPath);

		settings.Update(s => s with { HideController = true });

		Assert.Equal(HidHideStatus.Error, service.Status);
	}

	[Fact]
	public async Task NewControllerConnection_IsHiddenToo()
	{
		transport.Add(FakeHidTransport.UsbPath);
		settings.Update(s => s with { HideController = true });

		transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		Assert.Contains("ID-BT", driver.HiddenDevices);
	}

	[Fact]
	public void AlreadyHidden_NoRedundantWrites()
	{
		transport.Add(FakeHidTransport.UsbPath);
		settings.Update(s => s with { HideController = true });
		driver.Writes.Clear();

		service.Apply();

		Assert.Empty(driver.Writes);
	}

	[Fact]
	public void NotAttached_ReportsStatus_AndHidesNothing()
	{
		driver.Attached = false;
		transport.Add(FakeHidTransport.UsbPath);

		settings.Update(s => s with { HideController = true });

		Assert.Equal(HidHideStatus.NotAttached, service.Status);
		Assert.Empty(driver.Writes);
	}

	[Fact]
	public void NotAttached_StillRestoresDevicesWhenTurnedOff()
	{
		driver.HiddenDevices = [OtherDevice, "ID-USB"];
		settings.Update(s => s with { HiddenDeviceIds = ["ID-USB"] });
		driver.Attached = false;

		settings.Update(s => s with { HideController = false });

		Assert.Equal([OtherDevice], driver.HiddenDevices);
		Assert.Equal(HidHideStatus.NotAttached, service.Status);
	}
}
