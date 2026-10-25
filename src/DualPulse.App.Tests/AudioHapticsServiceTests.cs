using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class AudioHapticsServiceTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new();

	private readonly FakeAudioSource source = new();

	private readonly FakeEmulationService emulation = new() { Status = EmulationStatus.Active };

	private readonly FakeControllerUsbAudio usbAudio = new();

	private readonly ControllerService controller;

	private readonly AudioHapticsService audio;

	public AudioHapticsServiceTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		audio = new AudioHapticsService(controller, settings, source, emulation, usbAudio);
	}

	public void Dispose()
	{
		audio.Dispose();
		controller.Dispose();
	}

	private static float[] Bass()
	{
		float[] samples = new float[4800 * 2];
		for (int i = 0; i < 4800; i++)
		{
			samples[2 * i] = samples[2 * i + 1] = (float)(0.8 * Math.Sin(2 * Math.PI * 100 * i / 48000));
		}
		return samples;
	}

	private void Enable(bool on = true) => settings.Update(s => s with { Rumble = new RumbleSettings(AudioHaptics: on) });

	[Fact]
	public async Task Off_ByDefault_NoCapture()
	{
		transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		Assert.False(source.Running);
	}

	[Fact]
	public async Task On_OverBluetooth_CapturesAndPlaysAsHaptics()
	{
		Enable();
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		Assert.True(source.Running);

		source.Play(Bass());

		TestReports.WaitUntil(() => device.Latest.Writes.Any(w => w[0] == 0x32 && w[13..77].Any(b => b != 0)));
	}

	[Fact]
	public async Task OverUsb_PlaysThroughTheControllersSoundDevice()
	{
		Enable();
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		Assert.True(source.Running);

		source.Play(Bass());

		FakeControllerAudioStream stream = Assert.Single(usbAudio.Opened);
		Assert.NotEmpty(stream.Written);
		// Speaker channels silent, haptics on channels 3-4.
		Assert.All(Enumerable.Range(0, stream.Written.Count / 4), frame => Assert.Equal(0, stream.Written[frame * 4]));
		Assert.Contains(Enumerable.Range(0, stream.Written.Count / 4), frame => stream.Written[frame * 4 + 2] != 0);

		Enable(false);
		Assert.False(source.Running);
		Assert.True(stream.Disposed);
	}

	[Fact]
	public async Task TurningItOff_OrClassicRumble_StopsCapture()
	{
		Enable();
		transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		settings.Update(s => s with { Rumble = s.EffectiveRumble with { BluetoothHaptics = false } });
		Assert.False(source.Running);

		settings.Update(s => s with { Rumble = s.EffectiveRumble with { BluetoothHaptics = true } });
		Assert.True(source.Running);
		Enable(false);
		Assert.False(source.Running);
	}

	[Fact]
	public async Task StandsDown_WhileAGameDrivesTheVirtualDualSense()
	{
		settings.Update(s => s with { Emulation = DualPulse.Emulation.PadKind.DualSense, Rumble = new RumbleSettings(AudioHaptics: true) });
		transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		Assert.False(source.Running);
	}

	[Fact]
	public async Task KeepsFeeding_WhenTheVirtualDualSenseIsntRunning_ThenStandsDownOnceItIs()
	{
		// Chosen but not plugged in (usbip-win2 missing, pad lost): nothing else plays haptics, so this keeps going.
		emulation.Status = EmulationStatus.DriverMissing;
		settings.Update(s => s with { Emulation = DualPulse.Emulation.PadKind.DualSense, Rumble = new RumbleSettings(AudioHaptics: true) });
		transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		Assert.True(source.Running);

		emulation.Status = EmulationStatus.Active;
		emulation.RaiseStatusChanged();

		TestReports.WaitUntil(() => !source.Running);
	}
}
