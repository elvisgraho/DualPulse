using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Features;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class LightingServiceTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new();

	private readonly ControllerService controller;

	private readonly LightingService lighting;

	public LightingServiceTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		lighting = new LightingService(controller, settings, time);
	}

	public void Dispose()
	{
		lighting.Dispose();
		controller.Dispose();
	}

	private async Task<FakeDevice> ConnectAsync()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		return device;
	}

	[Fact]
	public async Task Lighting_AppliesColourPlayerLedsAndMicOnSessionStart()
	{
		settings.Update(s => s with
		{
			Lightbar = new Rgb(255, 0, 0),
			Lighting = new LightingSettings(PlayerLeds: PlayerLedPattern.Player3, PlayerLedBrightness: LedBrightness.Low, MicLed: MicLed.On)
		});

		await ConnectAsync();

		OutputState output = controller.Session!.Output;
		Assert.Equal(new Rgb(255, 0, 0), output.Lightbar);
		Assert.Equal(0b10101, output.PlayerLeds);
		Assert.Equal(LedBrightness.Low, output.PlayerLedBrightness);
		Assert.Equal(MicLed.On, output.MicLed);
	}

	[Fact]
	public async Task Lighting_DefaultIsAccentAtFullBrightness()
	{
		await ConnectAsync();

		Assert.Equal(AppSettings.AccentLightbar, controller.Session!.Output.Lightbar);
		Assert.Equal(0b00100, controller.Session.Output.PlayerLeds);
	}

	[Fact]
	public async Task Lighting_SettingsChange_ReappliesImmediately()
	{
		await ConnectAsync();

		settings.Update(s => s with { Lightbar = new Rgb(0, 0, 255), Lighting = new LightingSettings(Brightness: 50) });

		Assert.Equal(new Rgb(0, 0, 128), controller.Session!.Output.Lightbar);
	}

	[Fact]
	public async Task Lighting_Breathing_AnimatesWithTime()
	{
		settings.Update(s => s with { Lightbar = new Rgb(200, 100, 50), Lighting = new LightingSettings(Mode: LightbarMode.Breathing) });
		await ConnectAsync();
		Rgb start = controller.Session!.Output.Lightbar;

		time.Advance(LightingEngine.BreathingPeriod / 2);

		Assert.Equal(new Rgb(30, 15, 8), start);
		Assert.Equal(new Rgb(200, 100, 50), controller.Session.Output.Lightbar);
	}

	[Fact]
	public async Task Lighting_Static_SendsNothingWhileIdle()
	{
		FakeDevice device = await ConnectAsync();
		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0);
		// The session re-sends its output while the controller settles (first 2.5 s); idle means after that.
		for (int i = 0; i < 4; i++)
		{
			time.Advance(TimeSpan.FromSeconds(1));
			Thread.Sleep(30);
		}
		Thread.Sleep(100);
		int writes = device.Latest.Writes.Count;

		for (int i = 0; i < 20; i++)
		{
			time.Advance(TimeSpan.FromMilliseconds(250));
		}
		Thread.Sleep(100);

		Assert.Equal(writes, device.Latest.Writes.Count);
	}

	[Fact]
	public async Task Lighting_LowBattery_FlashesThePlayerLeds()
	{
		FakeDevice device = await ConnectAsync();
		byte[] low = TestReports.Fixture("idle-usb");
		low[1 + 52] = 0x01;
		device.Latest.Feed(low);
		TestReports.WaitUntil(() => controller.Session!.Battery.Level == 10);

		HashSet<byte> seen = [];
		for (int i = 0; i < 8; i++)
		{
			time.Advance(TimeSpan.FromMilliseconds(500));
			Thread.Sleep(20);
			seen.Add(controller.Session!.Output.PlayerLeds);
		}

		Assert.Contains((byte)0b00100, seen);
		Assert.Contains((byte)0, seen);
	}

	[Fact]
	public async Task Lighting_BatteryPattern_ShowsTheLevel()
	{
		settings.Update(s => s with { Lighting = new LightingSettings(PlayerLeds: PlayerLedPattern.Battery) });
		FakeDevice device = await ConnectAsync();
		byte[] half = TestReports.Fixture("idle-usb");
		half[1 + 52] = 0x05;
		device.Latest.Feed(half);
		TestReports.WaitUntil(() => controller.Session!.Battery.Level == 50);

		time.Advance(TimeSpan.FromMilliseconds(500));

		TestReports.WaitUntil(() => controller.Session!.Output.PlayerLeds == 0b00111);
	}

	[Fact]
	public async Task Lighting_MuteLed_FollowsTheMicrophone()
	{
		FakeMicrophone mic = new();
		using MicMuteService micMute = new(controller, settings, mic);
		using LightingService withMic = new(controller, settings, time, micMute: micMute);
		await ConnectAsync();
		Assert.Equal(MicLed.Off, controller.Session!.Output.MicLed);

		mic.SetFromWindows(true);
		Assert.Equal(MicLed.On, controller.Session.Output.MicLed);

		mic.SetFromWindows(null);
		Assert.Equal(MicLed.Off, controller.Session.Output.MicLed);
	}

	[Fact]
	public async Task Lighting_MuteLed_UsesTheSavedChoice_WhenTheMicSettingIsOff()
	{
		settings.Update(s => s with { Lighting = new LightingSettings(MicLed: MicLed.Pulse, MuteButtonMutesMic: false) });
		FakeMicrophone mic = new() { IsMuted = true };
		using MicMuteService micMute = new(controller, settings, mic);
		using LightingService withMic = new(controller, settings, time, micMute: micMute);

		await ConnectAsync();

		Assert.Equal(MicLed.Pulse, controller.Session!.Output.MicLed);
	}
}
