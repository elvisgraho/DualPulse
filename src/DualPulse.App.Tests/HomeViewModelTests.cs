using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class HomeViewModelTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	// Starts in Xbox 360 mode, which these tests use (new installs start in Native).
	private readonly FakeSettingsService settings = new(new AppSettings(Emulation: DualPulse.Emulation.PadKind.Xbox360));

	private readonly FakeEmulationService emulation = new();

	private readonly FakePowerService power = new();

	private readonly ControllerService controller;

	private readonly HomeViewModel home;

	public HomeViewModelTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		home = new HomeViewModel(controller, emulation, settings, power, time: time, deviceInfo: () => new DeviceInfoViewModel(controller, time));
	}

	public void Dispose()
	{
		controller.Dispose();
	}

	private async Task<FakeConnection> ConnectAsync(string path)
	{
		FakeDevice device = transport.Add(path);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		return device.Latest;
	}

	[Fact]
	public void Home_NoSession_ShowsDisconnectedState()
	{
		home.Refresh();

		Assert.False(home.IsConnected);
		Assert.Equal("Not connected", home.ConnectionText);
		Assert.False(home.PowerOffCommand.CanExecute(null));
		Assert.Null(home.Snapshot);
		Assert.Equal("DualPulse - not connected", home.TrayToolTip);
	}

	[Fact]
	public async Task Home_BluetoothSession_ShowsLinkBatteryFirmware()
	{
		FakeConnection connection = await ConnectAsync(FakeHidTransport.BluetoothPath);
		connection.OnRead = () => time.Advance(TimeSpan.FromMilliseconds(100));
		for (int i = 0; i < 15; i++)
		{
			connection.Feed(TestReports.BluetoothWithSequence("idle", i));
		}
		TestReports.WaitUntil(() => controller.Session!.LinkQuality != null && connection.PendingReports == 0);

		home.Refresh();
		home.SampleInput();

		Assert.True(home.IsConnected);
		Assert.StartsWith("Bluetooth · ", home.ConnectionText);
		Assert.EndsWith("% loss", home.ConnectionText);
		Assert.Equal(60, home.BatteryPercent);
		Assert.True(home.IsCharging);
		Assert.Equal("Firmware 06.30", home.FirmwareText);
		Assert.Equal("Xbox 360", home.EmulationText);
		Assert.Equal("DualPulse - 60% (charging)", home.TrayToolTip);
		Assert.True(home.PowerOffCommand.CanExecute(null));
		Assert.NotNull(home.Snapshot);
	}

	[Fact]
	public async Task Home_Summary_ShowsFirmwareAndConnectedTime()
	{
		home.Refresh();
		Assert.Equal("", home.DeviceSummary);

		await ConnectAsync(FakeHidTransport.UsbPath);
		time.Advance(TimeSpan.FromMinutes(72));
		home.Refresh();

		Assert.Equal("Firmware 06.30 · Connected for 1 h 12 min", home.DeviceSummary);
	}

	[Fact]
	public async Task Home_Details_OpensAndClosesInPlace()
	{
		await ConnectAsync(FakeHidTransport.UsbPath);
		Assert.Null(home.DeviceInfo);

		home.ShowDeviceInfoCommand.Execute(null);
		TestReports.WaitUntil(() => home.DeviceInfo?.IsConnected == true);

		home.CloseDeviceInfoCommand.Execute(null);
		Assert.Null(home.DeviceInfo);
	}

	[Fact]
	public void Home_ProfilePicker_FollowsAProfileChosenElsewhere()
	{
		// A controller shortcut (or the tray) changes the chosen profile; the picker on Home shows it.
		GameProfile racing = new("Racing", GameProfile.NamedPrefix + "Racing");
		settings.Update(s => s with { Profiles = [racing] });
		List<string?> changed = [];
		home.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		settings.Update(s => s with { ChosenProfile = racing.Exe });

		Assert.Contains(nameof(HomeViewModel.ChosenProfile), changed);
		Assert.Equal("Racing", home.ChosenProfile?.Label);
	}

	[Fact]
	public async Task Home_UsbSession_ShowsTheCable()
	{
		home.Refresh();
		Assert.False(home.IsCabled);

		await ConnectAsync(FakeHidTransport.UsbPath);
		home.Refresh();

		Assert.True(home.IsCabled);
	}

	[Fact]
	public async Task Home_BluetoothSession_ShowsNoCable()
	{
		await ConnectAsync(FakeHidTransport.BluetoothPath);
		home.Refresh();

		Assert.True(home.IsConnected);
		Assert.False(home.IsCabled);
	}

	[Fact]
	public async Task Home_SampleInput_IgnoresSensorOnlyChanges()
	{
		FakeConnection connection = await ConnectAsync(FakeHidTransport.UsbPath);
		int snapshotChanges = 0;
		home.PropertyChanged += (_, e) => snapshotChanges += e.PropertyName == nameof(HomeViewModel.Snapshot) ? 1 : 0;
		byte[] idle = TestReports.Fixture("idle-usb");
		byte[] moved = (byte[])idle.Clone();
		moved[1 + 15] ^= 0x55;
		moved[1 + 27] ^= 0x55;

		connection.Feed(idle);
		TestReports.WaitUntil(() => controller.Session!.Latest != null);
		home.SampleInput();
		connection.Feed(moved);
		TestReports.WaitUntil(() => controller.Session!.Latest!.Value.GyroX != home.Snapshot!.Value.GyroX);
		home.SampleInput();
		Assert.Equal(1, snapshotChanges);

		connection.Feed(TestReports.Fixture("cross-usb"));
		TestReports.WaitUntil(() => controller.Session!.Latest!.Value.Buttons != 0);
		home.SampleInput();
		Assert.Equal(2, snapshotChanges);
	}

	[Fact]
	public async Task Home_SampleInput_IgnoresStickNoiseAtRest()
	{
		FakeConnection connection = await ConnectAsync(FakeHidTransport.UsbPath);
		int snapshotChanges = 0;
		home.PropertyChanged += (_, e) => snapshotChanges += e.PropertyName == nameof(HomeViewModel.Snapshot) ? 1 : 0;

		foreach (byte value in new byte[] { 127, 128, 129, 128, 127 })
		{
			byte[] report = TestReports.Fixture("idle-usb");
			report[1] = value;
			report[1 + 27] = value;
			connection.Feed(report);
			TestReports.WaitUntil(() => controller.Session!.Latest?.SensorTimestamp == BitConverter.ToUInt32(report, 1 + 27));
			home.SampleInput();
		}

		Assert.Equal(1, snapshotChanges);
	}

	[Fact]
	public async Task Home_VisibleInputChange_UpdatesSnapshotWithoutPolling()
	{
		FakeConnection connection = await ConnectAsync(FakeHidTransport.UsbPath);

		connection.Feed(TestReports.Fixture("cross-usb"));

		TestReports.WaitUntil(() => home.Snapshot?.Buttons.HasFlag(Buttons.Cross) == true);
	}

	[Fact]
	public async Task Home_ControllerCantBeOpened_SaysWhy()
	{
		transport.Add(FakeHidTransport.UsbPath, failOpen: true);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		home.Refresh();

		Assert.False(home.IsConnected);
		Assert.Equal("Controller in use by another app", home.ConnectionText);
	}

	[Fact]
	public async Task Home_UsbSession_PowerOffDisabled()
	{
		await ConnectAsync(FakeHidTransport.UsbPath);

		home.Refresh();

		Assert.True(home.IsConnected);
		Assert.StartsWith("USB", home.ConnectionText);
		Assert.False(home.PowerOffCommand.CanExecute(null));
	}

	[Fact]
	public void Home_EmulationText_FollowsStatusAndSettings()
	{
		emulation.Status = EmulationStatus.DriverMissing;
		home.Refresh();
		Assert.Equal("Driver missing", home.EmulationText);

		emulation.Status = EmulationStatus.Off;
		settings.Update(s => s with { Emulation = null });
		home.Refresh();
		Assert.Equal("Native", home.EmulationText);

		settings.Update(s => s with { Emulation = DualPulse.Emulation.PadKind.DualShock4 });
		home.Refresh();
		Assert.Equal("DualShock 4", home.EmulationText);

		settings.Update(s => s with { Emulation = DualPulse.Emulation.PadKind.DualSense });
		home.Refresh();
		Assert.Equal("DualSense", home.EmulationText);
	}

	[Fact]
	public void Home_ProfilePicker_ShowsOnceThereAreNamedProfiles_AndPicksOne()
	{
		Assert.False(home.ShowProfilePicker);

		GameProfile racing = GameProfile.Named("Racing", []);
		settings.Update(s => s with { Profiles = [new GameProfile("Forza", "forza.exe"), racing] });

		Assert.True(home.ShowProfilePicker);
		Assert.Equal(["Default", "Racing"], home.NamedProfiles.Select(p => p.Label));
		Assert.Equal("Default", home.ChosenProfile!.Label);

		home.ChosenProfile = home.NamedProfiles[1];

		Assert.Equal(racing.Exe, settings.Current.ChosenProfile);
	}

	[Fact]
	public void Home_Swatches_MarkTheCurrentColour()
	{
		Assert.Equal([AppSettings.AccentLightbar], home.LightbarSwatches.Where(s => s.Selected).Select(s => s.Colour));

		home.SetLightbarCommand.Execute(new Rgb(0xFF, 0x3B, 0x3B));
		Assert.Equal([new Rgb(0xFF, 0x3B, 0x3B)], home.LightbarSwatches.Where(s => s.Selected).Select(s => s.Colour));

		// A colour set elsewhere (hue sliders, hex) shows as an extra swatch.
		settings.Update(s => s with { Lightbar = new Rgb(0x12, 0x34, 0x56) });
		Assert.Equal(7, home.LightbarSwatches.Count);
		Assert.Equal(new SwatchChoice(new Rgb(0x12, 0x34, 0x56), true), home.LightbarSwatches[^1]);
		Assert.Single(home.LightbarSwatches, s => s.Selected);
	}

	[Fact]
	public void Home_Lightbar_FollowsTheChosenColour_ForTheDrawing()
	{
		Assert.Equal(AppSettings.AccentLightbar, home.Lightbar);
		List<string?> changed = [];
		home.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		home.SetLightbarCommand.Execute(new Rgb(0x3D, 0x7B, 0xFF));

		Assert.Equal(new Rgb(0x3D, 0x7B, 0xFF), home.Lightbar);
		Assert.Contains(nameof(HomeViewModel.Lightbar), changed);
	}

	[Fact]
	public void Home_SetLightbar_UpdatesSettings()
	{
		Assert.Equal(
			[new Rgb(0xC9, 0xA8, 0x6A), new Rgb(0xFF, 0xFF, 0xFF), new Rgb(0x3D, 0x7B, 0xFF), new Rgb(0xFF, 0x3B, 0x3B), new Rgb(0x3D, 0xDC, 0x84), new Rgb(0, 0, 0)],
			home.LightbarSwatches.Select(s => s.Colour));

		home.SetLightbarCommand.Execute(new Rgb(0x3D, 0x7B, 0xFF));

		Assert.Equal(new Rgb(0x3D, 0x7B, 0xFF), settings.Current.Lightbar);
	}

	[Fact]
	public void Home_SelectPreset_UpdatesSettings()
	{
		Assert.Equal([TriggerPreset.Off, TriggerPreset.Feedback, TriggerPreset.Weapon, TriggerPreset.Vibration], home.TriggerPresets);

		home.SelectedTriggerPreset = TriggerPreset.Weapon;

		Assert.Equal(TriggerPreset.Weapon, settings.Current.TriggerPreset);
		Assert.Equal(TriggerSettings.FromPreset(TriggerPreset.Weapon), settings.Current.Triggers);
	}

	[Fact]
	public void Home_CustomTriggers_ShowNoPreset()
	{
		settings.Update(s => s with { TriggerPreset = TriggerPreset.Custom, Triggers = new TriggerSettings(Linked: false) });

		Assert.Null(home.SelectedTriggerPreset);
	}

	[Fact]
	public void Tray_NoController_SaysSo()
	{
		home.Refresh();

		Assert.Equal("Not connected", home.TrayStatus);
	}

	[Fact]
	public async Task Tray_ShowsConnectionAndBattery()
	{
		FakeConnection connection = await ConnectAsync(FakeHidTransport.UsbPath);
		connection.Feed(TestReports.Fixture("idle-usb"));
		TestReports.WaitUntil(() => controller.Session!.Battery.Level == 60);

		home.Refresh();

		Assert.Equal("USB · 60% (charging)", home.TrayStatus);
	}

	[Fact]
	public void Tray_EmulationToggle_OffThenBackToTheSameKind()
	{
		settings.Update(s => s with { Emulation = DualPulse.Emulation.PadKind.DualShock4 });
		Assert.True(home.EmulationOn);

		home.ToggleEmulationCommand.Execute(null);
		Assert.Null(settings.Current.Emulation);
		Assert.False(home.EmulationOn);

		home.ToggleEmulationCommand.Execute(null);
		Assert.Equal(DualPulse.Emulation.PadKind.DualShock4, settings.Current.Emulation);
	}

	[Fact]
	public void Tray_EmulationToggle_FromNativeTurnsOnXbox()
	{
		settings.Update(s => s with { Emulation = null });

		home.ToggleEmulationCommand.Execute(null);

		Assert.Equal(DualPulse.Emulation.PadKind.Xbox360, settings.Current.Emulation);
	}

	[Fact]
	public void Tray_PcControlToggle()
	{
		FakePcControlService pc = new();
		HomeViewModel withPc = new(controller, emulation, settings, power, pc);

		withPc.TogglePcControlCommand.Execute(null);
		Assert.True(pc.IsActive);
		Assert.True(withPc.PcControlOn);

		withPc.TogglePcControlCommand.Execute(null);
		Assert.False(pc.IsActive);
	}
}
