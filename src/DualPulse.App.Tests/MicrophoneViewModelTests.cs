using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Core.Audio;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class MicrophoneViewModelTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new();

	private readonly FakeMicrophones microphones = new();

	private readonly VirtualMicrophoneMonitor monitor = new();

	private readonly ControllerService controller;

	private readonly MicrophoneViewModel page;

	public MicrophoneViewModelTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		page = new MicrophoneViewModel(controller, settings, microphones, monitor, time: time);
	}

	public void Dispose()
	{
		page.Stop();
		controller.Dispose();
	}

	// Device names and volume arrive from a background lookup.
	private void RefreshUntil(Func<bool> condition) => TestReports.WaitUntil(() =>
	{
		page.Refresh();
		return condition();
	});

	private async Task ConnectAsync(string path)
	{
		transport.Add(path);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		TestReports.WaitUntil(() => controller.Session != null);
	}

	[Fact]
	public void NoController_SaysConnect()
	{
		page.Start();
		page.Refresh();

		Assert.Equal("Connect your controller.", page.Status);
		Assert.Null(microphones.Capture);
	}

	[Fact]
	public async Task Bluetooth_SaysToUseTheCable_AndOpensNothing()
	{
		await ConnectAsync(FakeHidTransport.BluetoothPath);

		page.Start();
		page.Refresh();

		Assert.Equal("Over Bluetooth, Windows gets no microphone from the controller. Plug in the cable to use it.", page.Status);
		Assert.Null(microphones.Capture);
	}

	[Fact]
	public async Task Cable_ShowsTheNameAndTheSound()
	{
		await ConnectAsync(FakeHidTransport.UsbPath);

		page.Start();
		page.Refresh();
		microphones.Capture!.Feed(Enumerable.Repeat((short)16384, 2 * 960).ToArray());
		RefreshUntil(() => page.RealName != null);

		Assert.Equal("Headset Microphone (8- DualSense Wireless Controller)", page.RealName);
		Assert.Equal("Working.", page.Status);
		Assert.Equal(0.5f, page.RealPeaks[^1], 3);
	}

	[Fact]
	public async Task Cable_NoSoundAtAll_SaysToPowerCycle()
	{
		// After Bluetooth the controller's USB sound can stay silent until it is switched off and on.
		await ConnectAsync(FakeHidTransport.UsbPath);
		page.Start();
		page.Refresh();

		time.Advance(TimeSpan.FromSeconds(2));
		page.Refresh();

		Assert.Equal("No sound from the controller. Turn it off and on again while it's plugged in.", page.Status);
	}

	[Fact]
	public async Task Cable_NoMicrophoneDevice_SaysSo()
	{
		microphones.Names = (null, null);
		microphones.Available = false;
		await ConnectAsync(FakeHidTransport.UsbPath);

		page.Start();
		page.Refresh();

		Assert.Equal("Windows hasn't found the controller's microphone yet.", page.Status);
	}

	[Fact]
	public async Task LeavingThePage_ClosesTheMicrophone()
	{
		await ConnectAsync(FakeHidTransport.UsbPath);
		page.Start();
		page.Refresh();

		page.Stop();

		Assert.True(microphones.Capture!.Disposed);
	}

	[Fact]
	public async Task Volume_ReadsAndWritesWindowsInputVolume()
	{
		await ConnectAsync(FakeHidTransport.UsbPath);
		page.Start();
		page.Refresh();

		RefreshUntil(() => page.HasVolume);
		Assert.Equal(65, page.Volume);
		page.Volume = 80;

		Assert.Equal(80, microphones.VolumeValue);
	}

	[Fact]
	public void VirtualSection_OnlyInDualSenseMode()
	{
		page.Refresh();
		Assert.False(page.ShowVirtual);

		settings.Update(s => s with { Emulation = PadKind.DualSense });
		RefreshUntil(() => page.VirtualName != null);

		Assert.True(page.ShowVirtual);
		Assert.Equal("Headset Microphone (7- DualSense Wireless Controller)", page.VirtualName);
		Assert.Equal("Not in use.", page.VirtualStatus);
	}

	[Fact]
	public void VirtualSection_ShowsRecordingAndWhatTheGameGets()
	{
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		monitor.SetInUse(true);
		monitor.Levels.Push(Enumerable.Repeat((short)16384, 2 * 1200).ToArray());

		page.Refresh();

		Assert.Equal("A game or app is recording from it.", page.VirtualStatus);
		Assert.Equal(0.5f, page.VirtualPeaks[^1], 3);
	}

	[Fact]
	public void MuteButtonSettings_LiveHere()
	{
		Assert.Equal(["Off", "On", "Pulse"], page.MicLedChoices.Select(c => c.Label));

		page.MuteButtonMutesMic = false;
		page.SelectedMicLed = page.MicLedChoices[2];

		Assert.False(settings.Current.EffectiveLighting.MuteButtonMutesMic);
		Assert.Equal(MicLed.Pulse, settings.Current.EffectiveLighting.MicLed);
	}

	[Fact]
	public void MicHint_SaysWhetherThereIsAMicrophone()
	{
		FakeMicrophone mic = new();
		using MicMuteService micMute = new(controller, settings, mic);
		MicrophoneViewModel withMute = new(controller, settings, microphones, monitor, micMute, time);
		Assert.Equal("Shows whether your microphone is muted.", withMute.MicHint);

		mic.SetFromWindows(null);

		Assert.Equal("No microphone found.", withMute.MicHint);
	}

	private sealed class FakeMicrophones : IControllerUsbAudio
	{
		public (string?, string?) Names { get; set; } = ("Headset Microphone (8- DualSense Wireless Controller)", "Headset Microphone (7- DualSense Wireless Controller)");

		public bool Available { get; set; } = true;

		public int VolumeValue { get; private set; } = 65;

		public FakeCapture? Capture { get; private set; }

		public IControllerAudioStream? Open() => null;

		public IDisposable? OpenMicrophone(MicrophoneBlock blocks) => Available ? Capture = new FakeCapture(blocks) : null;

		public (string? Real, string? Virtual) MicrophoneNames() => Names;

		public int? MicrophoneVolume() => Available ? VolumeValue : null;

		public bool SetMicrophoneVolume(int percent)
		{
			VolumeValue = percent;
			return true;
		}
	}

	private sealed class FakeCapture(MicrophoneBlock blocks) : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Feed(short[] stereo) => blocks(stereo);

		public void Dispose() => Disposed = true;
	}
}
