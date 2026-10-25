using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class RumbleServiceTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new();

	private readonly ControllerService controller;

	private readonly RumbleService rumble;

	public RumbleServiceTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		rumble = new RumbleService(controller, settings, time);
	}

	public void Dispose()
	{
		rumble.Dispose();
		controller.Dispose();
	}

	private async Task ConnectAsync()
	{
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
	}

	private (byte Left, byte Right) Motors => (controller.Session!.Output.LeftMotor, controller.Session.Output.RightMotor);

	[Fact]
	public void Scale_StrengthAndCap()
	{
		Assert.Equal(200, RumbleService.Scale(200, new RumbleSettings(Strength: 100)));
		Assert.Equal(100, RumbleService.Scale(200, new RumbleSettings(Strength: 50)));
		Assert.Equal(255, RumbleService.Scale(200, new RumbleSettings(Strength: 200)));
		Assert.Equal(0, RumbleService.Scale(200, new RumbleSettings(Strength: 0)));
		Assert.Equal(0, RumbleService.Scale(200, new RumbleSettings(Muted: true)));
	}

	[Fact]
	public async Task GameRumble_IsScaledLargeLeftSmallRight()
	{
		settings.Update(s => s with { Rumble = new RumbleSettings(Strength: 50) });
		await ConnectAsync();

		rumble.SetGameRumble(200, 100);

		Assert.Equal(((byte)100, (byte)50), Motors);
	}

	[Fact]
	public async Task Muted_SendsNothing_AndUnmutingRestoresCurrentRumble()
	{
		await ConnectAsync();
		rumble.SetGameRumble(200, 100);

		settings.Update(s => s with { Rumble = new RumbleSettings(Muted: true) });
		Assert.Equal(((byte)0, (byte)0), Motors);

		settings.Update(s => s with { Rumble = new RumbleSettings(Muted: false) });
		Assert.Equal(((byte)200, (byte)100), Motors);
	}

	[Fact]
	public async Task TestPulse_RunsForOneSecond_ThenRestoresGameRumble()
	{
		await ConnectAsync();
		rumble.SetGameRumble(0, 40);

		rumble.TestMotors(left: true, right: false);
		Assert.Equal((RumbleService.TestLevel, (byte)0), Motors);

		rumble.SetGameRumble(10, 20);
		Assert.Equal((RumbleService.TestLevel, (byte)0), Motors);

		time.Advance(RumbleService.TestDuration);
		Assert.Equal(((byte)10, (byte)20), Motors);
	}

	[Fact]
	public async Task TestPulse_FollowsStrength_ButNotMute()
	{
		settings.Update(s => s with { Rumble = new RumbleSettings(Strength: 50, Muted: true) });
		await ConnectAsync();

		rumble.TestMotors(left: true, right: true);

		Assert.Equal(((byte)(RumbleService.TestLevel / 2), (byte)(RumbleService.TestLevel / 2)), Motors);
	}

	[Fact]
	public async Task Mode_OverridesTheFirmwareChoice()
	{
		await ConnectAsync();
		Assert.True(controller.Session!.Output.ImprovedRumble);

		settings.Update(s => s with { Rumble = new RumbleSettings(Mode: RumbleMode.Classic) });
		Assert.False(controller.Session.Output.ImprovedRumble);

		settings.Update(s => s with { Rumble = new RumbleSettings(Mode: RumbleMode.Automatic) });
		Assert.True(controller.Session.Output.ImprovedRumble);
	}

	[Fact]
	public async Task Mode_ClassicAppliedOnSessionStart()
	{
		settings.Update(s => s with { Rumble = new RumbleSettings(Mode: RumbleMode.Classic) });

		await ConnectAsync();

		Assert.False(controller.Session!.Output.ImprovedRumble);
	}

	private async Task<FakeDevice> ConnectBluetoothAsync()
	{
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		return device;
	}

	[Fact]
	public async Task Bluetooth_GameRumble_PlaysAsHaptics()
	{
		FakeDevice device = await ConnectBluetoothAsync();

		rumble.SetGameRumble(200, 100);

		Assert.True(controller.Session!.Output.HapticsMode);
		Assert.Equal(((byte)0, (byte)0), Motors);
		Assert.Equal(((byte)200, (byte)100), rumble.Levels);
		TestReports.WaitUntil(() => device.Latest.Writes.Any(w => w[0] == 0x32));
	}

	[Fact]
	public async Task Bluetooth_ClassicSetting_UsesTheMotors()
	{
		settings.Update(s => s with { Rumble = new RumbleSettings(BluetoothHaptics: false) });
		await ConnectBluetoothAsync();

		rumble.SetGameRumble(200, 100);

		Assert.False(controller.Session!.Output.HapticsMode);
		Assert.Equal(((byte)200, (byte)100), Motors);
	}

	[Fact]
	public async Task Usb_KeepsTheMotors_EvenWithHapticsOn()
	{
		await ConnectAsync();

		rumble.SetGameRumble(200, 100);

		Assert.False(controller.Session!.Output.HapticsMode);
		Assert.Equal(((byte)200, (byte)100), Motors);
		Assert.Equal(((byte)200, (byte)100), rumble.Levels);
	}
}
