using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class GyroServiceTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new();

	private readonly FakePcOutput output = new();

	private readonly FakePcControlService pcControl = new();

	private readonly ControllerService controller;

	private readonly GyroService gyro;

	public GyroServiceTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		gyro = new GyroService(controller, settings, output, time, pcControl);
	}

	public void Dispose()
	{
		gyro.Dispose();
		controller.Dispose();
	}

	private static short Yaw(double degreesPerSecondRight) => (short)Math.Round(-degreesPerSecondRight * GyroAim.CountsPerDegree);

	private static InputSnapshot Input(Buttons buttons = Buttons.None, short gyroY = 0) =>
		new() { LeftX = 128, LeftY = 128, RightX = 128, RightY = 128, Buttons = buttons, GyroY = gyroY };

	private void Enable(GyroOutput output = GyroOutput.Stick, GyroActivation activation = GyroActivation.WhileL2)
	{
		settings.Update(s => s with { Gyro = new GyroSettings(Enabled: true, Output: output, Activation: activation) });
	}

	[Fact]
	public void StickMode_OnlyWhileL2IsHeld()
	{
		Enable();

		Assert.Equal(128, gyro.ApplyToPad(Input(gyroY: Yaw(90))).RightX);
		Assert.True(gyro.ApplyToPad(Input(Buttons.L2, Yaw(90))).RightX > 128);
	}

	[Fact]
	public void Disabled_LeavesTheStickAlone()
	{
		Assert.Equal(128, gyro.ApplyToPad(Input(Buttons.L2, Yaw(90))).RightX);
	}

	[Fact]
	public void AlwaysOn_NeedsNoButton()
	{
		Enable(activation: GyroActivation.Always);

		Assert.True(gyro.ApplyToPad(Input(gyroY: Yaw(90))).RightX > 128);
	}

	[Fact]
	public void MouseMode_LeavesThePadAlone()
	{
		Enable(GyroOutput.Mouse);

		Assert.Equal(128, gyro.ApplyToPad(Input(Buttons.L2, Yaw(90))).RightX);
	}

	private async Task<FakeConnection> ConnectAsync()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		device.Latest.OnRead = () => time.Advance(TimeSpan.FromMilliseconds(10));
		return device.Latest;
	}

	// A USB report holding L2 and turning right at the given rate.
	private static byte[] Report(double degreesPerSecondRight, bool l2 = true)
	{
		byte[] report = TestReports.Fixture("idle-usb");
		if (l2)
		{
			report[1 + 8] |= 0x04;
		}
		BitConverter.GetBytes(Yaw(degreesPerSecondRight)).CopyTo(report, 1 + 17);
		return report;
	}

	[Fact]
	public async Task MouseMode_MovesTheMouseWhileL2IsHeld()
	{
		Enable(GyroOutput.Mouse);
		FakeConnection connection = await ConnectAsync();

		for (int i = 0; i < 20; i++)
		{
			connection.Feed(Report(90));
		}

		TestReports.WaitUntil(() => output.Moved.X > 0);
	}

	[Fact]
	public async Task MouseMode_PausesWhilePcControlIsOn()
	{
		Enable(GyroOutput.Mouse);
		pcControl.IsActive = true;
		FakeConnection connection = await ConnectAsync();

		for (int i = 0; i < 20; i++)
		{
			connection.Feed(Report(90));
		}
		TestReports.WaitUntil(() => connection.PendingReports == 0);
		Thread.Sleep(50);

		Assert.Equal(0, output.Moved.X);
	}

	[Fact]
	public async Task Calibration_SavesTheRestingOffset()
	{
		FakeConnection connection = await ConnectAsync();
		short drift = Yaw(2);
		gyro.StartCalibration();
		Assert.Equal(GyroCalibrationState.Calibrating, gyro.CalibrationState);

		for (int i = 0; i < 200; i++)
		{
			connection.Feed(Report(2, l2: false));
		}

		TestReports.WaitUntil(() => gyro.CalibrationState == GyroCalibrationState.Done);
		GyroSettings saved = settings.Current.EffectiveGyro;
		Assert.Equal(drift, saved.OffsetY);
	}

	[Fact]
	public void Profile_TurnsGyroOn_OnlyWhileItsGameHasFocus()
	{
		GameProfile shooter = new("Shooter", "shooter.exe", Gyro: new GyroSettings(Enabled: true, Activation: GyroActivation.Always));
		settings.Update(s => s with { Profiles = [shooter] });
		FakeForegroundWatcher watcher = new();
		using ProfileService profiles = new(settings, watcher, "DualPulse.exe");
		using GyroService withProfiles = new(controller, settings, output, time, pcControl, profiles);

		Assert.Equal(128, withProfiles.ApplyToPad(Input(gyroY: Yaw(90))).RightX);
		watcher.Focus("shooter.exe");
		Assert.True(withProfiles.ApplyToPad(Input(gyroY: Yaw(90))).RightX > 128);
	}
}
