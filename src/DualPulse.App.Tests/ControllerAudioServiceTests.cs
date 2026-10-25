using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ControllerAudioServiceTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new();

	private readonly FakeAudioSourceFactory sources = new();

	private readonly FakeEmulationService emulation = new() { Status = EmulationStatus.Active };

	private readonly ControllerService controller;

	private readonly ControllerAudioService audio;

	public ControllerAudioServiceTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		audio = new ControllerAudioService(controller, settings, sources, emulation);
	}

	public void Dispose()
	{
		audio.Dispose();
		controller.Dispose();
	}

	private async Task<FakeDevice> ConnectAsync(string path)
	{
		FakeDevice device = transport.Add(path);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		return device;
	}

	private void Enable(ControllerAudioOutput output = ControllerAudioOutput.Auto) =>
		settings.Update(s => s with { Audio = new ControllerAudioSettings(Enabled: true, DeviceId: "dev-b", Output: output, Volume: 60) });

	private static float[] Tone()
	{
		float[] samples = new float[4800 * 2];
		for (int i = 0; i < 4800; i++)
		{
			samples[2 * i] = samples[2 * i + 1] = (float)(0.3 * Math.Sin(2 * Math.PI * 440 * i / 48000));
		}
		return samples;
	}

	[Fact]
	public async Task Off_ByDefault()
	{
		await ConnectAsync(FakeHidTransport.BluetoothPath);

		Assert.Empty(sources.Created);
		Assert.Equal(AudioOutput.Off, controller.Session!.Output.AudioOutput);
	}

	[Fact]
	public async Task On_OverBluetooth_CapturesTheChosenDevice_AndPlaysOnTheSpeaker()
	{
		Enable();
		FakeDevice device = await ConnectAsync(FakeHidTransport.BluetoothPath);

		(string? deviceId, FakeAudioSource source) = Assert.Single(sources.Created);
		Assert.Equal("dev-b", deviceId);
		Assert.True(source.Running);
		Assert.Equal(AudioOutput.Speaker, controller.Session!.Output.AudioOutput);
		Assert.Equal(60, controller.Session.Output.AudioVolume);

		source.Play(Tone());
		TestReports.WaitUntil(() => device.Latest.Writes.Any(w => w[0] == 0x39));
	}

	[Fact]
	public async Task Auto_SwitchesToEarbuds_WhenThePlugGoesIn()
	{
		Enable();
		FakeDevice device = await ConnectAsync(FakeHidTransport.BluetoothPath);

		byte[] plugged = TestReports.Fixture("idle-bt");
		plugged[2 + 53] |= 0x01;
		device.Latest.Feed(plugged);

		TestReports.WaitUntil(() => controller.Session!.Output.AudioOutput == AudioOutput.Headset);
	}

	[Fact]
	public async Task FixedOutput_IgnoresThePlug()
	{
		Enable(ControllerAudioOutput.Speaker);
		FakeDevice device = await ConnectAsync(FakeHidTransport.BluetoothPath);

		byte[] plugged = TestReports.Fixture("idle-bt");
		plugged[2 + 53] |= 0x01;
		device.Latest.Feed(plugged);
		TestReports.WaitUntil(() => controller.Session!.Latest?.HeadphonesPlugged == true);
		Thread.Sleep(30);

		Assert.Equal(AudioOutput.Speaker, controller.Session!.Output.AudioOutput);
	}

	[Fact]
	public async Task OverUsb_Nothing()
	{
		Enable();
		await ConnectAsync(FakeHidTransport.UsbPath);

		Assert.Empty(sources.Created);
	}

	[Fact]
	public async Task TurningItOff_StopsCapture_AndTheAudioPath()
	{
		Enable();
		await ConnectAsync(FakeHidTransport.BluetoothPath);
		FakeAudioSource source = sources.Created[0].Source;

		settings.Update(s => s with { Audio = s.EffectiveAudio with { Enabled = false } });

		Assert.False(source.Running);
		Assert.Equal(AudioOutput.Off, controller.Session!.Output.AudioOutput);
	}

	[Fact]
	public async Task StandsDown_WhileAGameDrivesTheVirtualDualSense()
	{
		settings.Update(s => s with { Emulation = DualPulse.Emulation.PadKind.DualSense, Audio = new ControllerAudioSettings(Enabled: true) });
		await ConnectAsync(FakeHidTransport.BluetoothPath);

		Assert.Empty(sources.Created);
		Assert.Equal(AudioOutput.Off, controller.Session!.Output.AudioOutput);
	}

	[Fact]
	public async Task KeepsPlaying_WhenTheVirtualDualSenseIsntRunning()
	{
		emulation.Status = EmulationStatus.DriverMissing;
		settings.Update(s => s with { Emulation = DualPulse.Emulation.PadKind.DualSense, Audio = new ControllerAudioSettings(Enabled: true) });
		await ConnectAsync(FakeHidTransport.BluetoothPath);

		Assert.NotEmpty(sources.Created);
	}
}
