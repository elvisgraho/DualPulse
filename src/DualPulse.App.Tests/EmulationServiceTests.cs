using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Audio;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class EmulationServiceTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly FakePadBackend backend = new();

	private readonly FakeUsbAudio usbAudio = new();

	private readonly VirtualMicrophoneMonitor microphoneMonitor = new();

	private readonly FakeNotifier notifier = new();

	private readonly FakeSettingsService settings;

	private readonly ControllerService controller;

	private readonly RumbleService rumble;

	private readonly EmulationService emulation;

	// Pad creation and disposal run inline, so tests see them at once (the app uses a worker thread).
	private static void Inline(Action work) => work();

	// Most tests need a virtual pad, so they start in Xbox 360 mode (new installs start in Native).
	public EmulationServiceTests() : this(new AppSettings(Emulation: PadKind.Xbox360))
	{
	}

	private EmulationServiceTests(AppSettings initial)
	{
		settings = new FakeSettingsService(initial);
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		rumble = new RumbleService(controller, settings, time);
		emulation = new EmulationService(controller, backend, settings, rumble, NullLogger<EmulationService>.Instance, time: time, padWorker: Inline,
			usbAudio: usbAudio, notifier: notifier, microphoneMonitor: microphoneMonitor);
	}

	public void Dispose()
	{
		emulation.Dispose();
		rumble.Dispose();
		controller.Dispose();
	}

	private FakeDevice Connect()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		controller.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
		Assert.NotNull(controller.Session);
		return device;
	}

	[Fact]
	public void Emulation_SessionStart_CreatesPadAndSubmitsInput()
	{
		FakeDevice device = Connect();

		FakePad pad = Assert.Single(backend.Created);
		Assert.Equal(PadKind.Xbox360, pad.Kind);
		Assert.Equal(EmulationStatus.Active, emulation.Status);
		device.Latest.Feed(TestReports.Fixture("cross-usb"));
		TestReports.WaitUntil(() => pad.Submitted == 1);
	}

	[Fact]
	public void Emulation_SensorOnlyChanges_NotResubmitted()
	{
		FakeDevice device = Connect();
		FakePad pad = backend.Created[0];
		byte[] idle = TestReports.Fixture("idle-usb");
		byte[] moved = (byte[])idle.Clone();
		moved[1 + 15] ^= 0x55;
		moved[1 + 27] ^= 0x55;

		device.Latest.Feed(idle);
		device.Latest.Feed(moved);
		device.Latest.Feed(idle);
		device.Latest.Feed(TestReports.Fixture("cross-usb"));

		TestReports.WaitUntil(() => device.Latest.PendingReports == 0 && controller.Session!.Latest?.Buttons != 0);
		TestReports.WaitUntil(() => pad.Submitted == 2);
		Thread.Sleep(100);
		Assert.Equal(2, pad.Submitted);
	}

	[Fact]
	public void Emulation_EndedSession_NotBound()
	{
		FakeControllerService fakeController = new();
		using EmulationService service = new(fakeController, backend, settings, rumble, NullLogger<EmulationService>.Instance);
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		ControllerSession dead = new(device.Info, transport, DualPulse.Core.Protocol.BluetoothLayout.Sequenced, NullLogger.Instance, time);
		dead.Start();
		dead.Stop("test");
		TestReports.WaitUntil(() => !dead.IsReading);

		fakeController.RaiseStarted(dead);

		Assert.Empty(backend.Created);
		Assert.Equal(EmulationStatus.Off, service.Status);
	}

	[Fact]
	public void Emulation_Feedback_FollowsRumbleStrength()
	{
		settings.Update(s => s with { Rumble = new RumbleSettings(Strength: 50) });
		Connect();

		backend.Created[0].Raise(new RumbleFeedback(200, 50, null));

		Assert.Equal(100, controller.Session!.Output.LeftMotor);
		Assert.Equal(25, controller.Session.Output.RightMotor);
	}

	[Fact]
	public void Emulation_Feedback_SetsMotorsLargeFirst()
	{
		Connect();

		backend.Created[0].Raise(new RumbleFeedback(200, 50, null));

		Assert.Equal(200, controller.Session!.Output.LeftMotor);
		Assert.Equal(50, controller.Session.Output.RightMotor);
	}

	[Fact]
	public void Emulation_BackendUnavailable_KeepsSessionAndReportsDriverMissing()
	{
		backend.IsAvailable = false;

		Connect();

		Assert.Empty(backend.Created);
		Assert.Equal(EmulationStatus.DriverMissing, emulation.Status);
		Assert.True(controller.Session!.IsReading);
	}

	[Fact]
	public void Emulation_DriverInstalledLater_StartsWithoutRestart()
	{
		backend.IsAvailable = false;
		Connect();
		Assert.Equal(EmulationStatus.DriverMissing, emulation.Status);

		backend.IsAvailable = true;
		time.Advance(EmulationService.DriverRecheck);

		TestReports.WaitUntil(() => emulation.Status == EmulationStatus.Active);
		Assert.Single(backend.Created);
	}

	[Fact]
	public void Emulation_ModeNull_NoPad_StatusOff()
	{
		settings.Update(s => s with { Emulation = null });

		Connect();

		Assert.Empty(backend.Created);
		Assert.Equal(EmulationStatus.Off, emulation.Status);
	}

	[Fact]
	public void Emulation_NativeMode_IsLogged()
	{
		ListLogger<EmulationService> log = new();
		using EmulationService service = new(controller, backend, settings, rumble, log);
		settings.Update(s => s with { Emulation = null });

		Connect();

		Assert.Contains(log.Entries, e => e.Message == "Emulation off (native mode)");
	}

	[Fact]
	public void Emulation_SessionEnds_DisposesPad_AfterTheGracePeriod()
	{
		FakeDevice device = Connect();
		FakePad pad = backend.Created[0];

		transport.Remove(device);
		Assert.False(pad.Disposed);
		time.Advance(TimeSpan.FromSeconds(10));

		TestReports.WaitUntil(() => pad.Disposed);
		Assert.Equal(EmulationStatus.Off, emulation.Status);
	}

	[Fact]
	public void Emulation_KindChanged_RecreatesPad()
	{
		Connect();

		settings.Update(s => s with { Emulation = PadKind.DualShock4 });

		Assert.Equal(2, backend.Created.Count);
		Assert.True(backend.Created[0].Disposed);
		Assert.Equal(PadKind.DualShock4, backend.Created[1].Kind);
	}

	[Fact]
	public async Task Emulation_GyroStickMode_ReachesThePad()
	{
		settings.Update(s => s with { Gyro = new GyroSettings(Enabled: true, Activation: GyroActivation.Always) });
		using GyroService gyro = new(controller, settings, new FakePcOutput(), time);
		using EmulationService withGyro = new(controller, backend, settings, rumble, NullLogger<EmulationService>.Instance, gyro: gyro, padWorker: Inline);
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		FakePad pad = backend.Created[^1];

		byte[] report = TestReports.Fixture("idle-usb");
		BitConverter.GetBytes((short)(-90 * DualPulse.Core.Mapping.GyroAim.CountsPerDegree)).CopyTo(report, 1 + 17);
		device.Latest.Feed(report);

		TestReports.WaitUntil(() => pad.LastSubmitted?.RightX > 128);
	}

	[Fact]
	public async Task Emulation_RemappedButton_ReachesTheGameAsItsTarget()
	{
		settings.Update(s => s with { ButtonRemaps = [new ButtonRemap(Buttons.Cross, Buttons.Circle)] });
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		FakePad pad = backend.Created[^1];

		device.Latest.Feed(TestReports.Fixture("cross-usb"));

		TestReports.WaitUntil(() => pad.LastSubmitted?.Buttons.HasFlag(Buttons.Circle) == true);
		Assert.False(pad.LastSubmitted!.Value.Buttons.HasFlag(Buttons.Cross));
	}

	[Fact]
	public async Task Emulation_StickDeadzone_CentresSmallPushes()
	{
		settings.Update(s => s with { Sticks = new StickSettings(Left: new StickRange(Deadzone: 0.2)) });
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		FakePad pad = backend.Created[^1];
		byte[] drift = TestReports.Fixture("idle-usb");
		drift[1] = 150;
		drift[3] = 150;

		device.Latest.Feed(drift);

		// Linked: both sticks centre a small push.
		TestReports.WaitUntil(() => pad.Submitted > 0);
		Assert.Equal(128, pad.LastSubmitted!.Value.LeftX);
		Assert.Equal(128, pad.LastSubmitted!.Value.RightX);
	}

	[Fact]
	public async Task Emulation_TriggerRange_ShapesL2()
	{
		settings.Update(s => s with { Triggers = new TriggerSettings(Linked: true, Left: new TriggerEffectSettings(Deadzone: 0.2)) });
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		FakePad pad = backend.Created[^1];

		device.Latest.Feed(TestReports.Fixture("l2-full-usb"));
		TestReports.WaitUntil(() => pad.LastSubmitted?.L2 == 255);
		byte[] light = TestReports.Fixture("idle-usb");
		light[1 + 4] = 40;
		device.Latest.Feed(light);
		TestReports.WaitUntil(() => pad.LastSubmitted?.L2 == 0);
	}

	[Fact]
	public async Task Emulation_ProfileRange_AppliedWhileTheGameHasFocus()
	{
		GameProfile racing = new("Racing", "racing.exe", Triggers: new TriggerSettings(Linked: true, Left: new TriggerEffectSettings(Deadzone: 0.2)));
		settings.Update(s => s with { Profiles = [racing] });
		FakeForegroundWatcher watcher = new();
		using ProfileService profiles = new(settings, watcher, "DualPulse.exe");
		using EmulationService withProfiles = new(controller, backend, settings, rumble, NullLogger<EmulationService>.Instance, profiles: profiles, padWorker: Inline);
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		FakePad pad = backend.Created[^1];
		byte[] light = TestReports.Fixture("idle-usb");
		light[1 + 4] = 40;

		watcher.Focus("racing.exe");
		device.Latest.Feed(light);
		TestReports.WaitUntil(() => pad.LastSubmitted != null);
		Assert.Equal(0, pad.LastSubmitted!.Value.L2);
	}

	[Fact]
	public async Task Emulation_TriggerDeadzone_AlsoHoldsBackTheDigitalTriggerButton()
	{
		settings.Update(s => s with { Triggers = new TriggerSettings(Linked: true, Left: new TriggerEffectSettings(Deadzone: 0.2)) });
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		FakePad pad = backend.Created[^1];
		byte[] light = TestReports.Fixture("idle-usb");
		light[1 + 4] = 40;
		light[1 + 8] |= 0x04;

		device.Latest.Feed(light);

		TestReports.WaitUntil(() => pad.LastSubmitted != null);
		Assert.False(pad.LastSubmitted!.Value.Buttons.HasFlag(DualPulse.Core.Protocol.Buttons.L2));
	}

	private async Task<FakeDualSensePad> ConnectDualSenseOverUsbAsync()
	{
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		return backend.CreatedDualSense[^1];
	}

	// Two 4-channel frames: speaker (1000, -1000), haptics (8000, -8000).
	private static short[] GameQuad() => [1000, -1000, 8000, -8000, 1000, -1000, 8000, -8000];

	[Fact]
	public async Task OnUsb_GameHapticsGoToTheControllersOwnSoundDevice()
	{
		// Over USB the session can't carry them (no Bluetooth audio reports); the controller plays them itself, as natively.
		FakeDualSensePad pad = await ConnectDualSenseOverUsbAsync();

		pad.SendSpeaker(GameQuad());

		short[] written = usbAudio.Stream!.Written.ToArray();
		Assert.Equal(new short[] { 0, 0, 8000, -8000, 0, 0, 8000, -8000 }, written);
	}

	[Fact]
	public async Task OnUsb_GameSoundToo_WhenWanted()
	{
		settings.Update(s => s with { GameSoundOnController = true });
		FakeDualSensePad pad = await ConnectDualSenseOverUsbAsync();

		pad.SendSpeaker(GameQuad());

		Assert.Equal(GameQuad(), usbAudio.Stream!.Written.ToArray());
	}

	[Fact]
	public async Task OnUsb_GameHaptics_FollowTheRumbleStrengthAndMute()
	{
		settings.Update(s => s with { Rumble = new RumbleSettings(Strength: 50) });
		FakeDualSensePad pad = await ConnectDualSenseOverUsbAsync();
		pad.SendSpeaker(GameQuad());
		Assert.Equal(4000, usbAudio.Stream!.Written[2]);

		settings.Update(s => s with { Rumble = new RumbleSettings(Muted: true) });
		pad.SendSpeaker(GameQuad());

		Assert.Equal(0, usbAudio.Stream.Written[^2]);
	}

	[Fact]
	public async Task LeavingUsb_ClosesTheControllersSoundDevice()
	{
		FakeDualSensePad pad = await ConnectDualSenseOverUsbAsync();
		pad.SendSpeaker(GameQuad());

		settings.Update(s => s with { Emulation = PadKind.Xbox360 });

		Assert.True(usbAudio.Stream!.Disposed);
	}

	[Fact]
	public async Task PluggingInFromBluetooth_InDualSenseMode_SaysToPowerCycleForCableHaptics()
	{
		// After Bluetooth a DualSense ignores haptics from its USB sound device until it is switched off and on.
		await ConnectDualSenseAsync();

		transport.Add(FakeHidTransport.UsbPath);

		(string title, string message) = Assert.Single(notifier.Shown);
		Assert.Equal("Vibration stopped", title);
		Assert.Equal("To get it back while the controller is plugged in, turn it off and on again.", message);
		// Long enough to read while playing.
		Assert.Equal(TimeSpan.FromSeconds(20), notifier.LastDuration);
	}

	[Fact]
	public async Task CableAfterAPowerCycle_NeedsNoNotice()
	{
		// Switched off on Bluetooth, then plugged in: the Bluetooth connection ended seconds before.
		(FakeDevice bluetooth, _) = await ConnectDualSenseAsync();
		transport.Remove(bluetooth);
		time.Advance(TimeSpan.FromSeconds(5));

		transport.Add(FakeHidTransport.UsbPath);
		time.Advance(TimeSpan.FromSeconds(2));

		Assert.Empty(notifier.Shown);
	}

	[Fact]
	public async Task PluggingInFromBluetooth_OutsideDualSenseMode_NeedsNoNotice()
	{
		settings.Update(s => s with { Emulation = PadKind.Xbox360 });
		transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		transport.Add(FakeHidTransport.UsbPath);

		Assert.Empty(notifier.Shown);
	}

	[Fact]
	public async Task OnUsb_RecordingFromTheVirtualMicrophone_UsesTheControllersOwn()
	{
		// Ungated, to see the route itself (the gate is on by default and delays the sound).
		settings.Update(s => s with { Microphone = new MicrophoneSettings(NoiseGate: false) });
		FakeDualSensePad pad = await ConnectDualSenseOverUsbAsync();

		pad.SetMicrophoneOpen(true);
		TestReports.WaitUntil(() => usbAudio.Microphone != null);
		usbAudio.Microphone!.Feed([1, 2, 3, 4]);

		Assert.Equal([1, 2, 3, 4], pad.MicrophoneWritten);
	}

	[Fact]
	public async Task OnUsb_Recording_ShowsInTheMonitor_WithWhatTheGameGets()
	{
		// Ungated, to see the route itself (the gate is on by default and delays the sound).
		settings.Update(s => s with { Microphone = new MicrophoneSettings(NoiseGate: false) });
		FakeDualSensePad pad = await ConnectDualSenseOverUsbAsync();
		Assert.False(microphoneMonitor.InUse);

		pad.SetMicrophoneOpen(true);
		TestReports.WaitUntil(() => usbAudio.Microphone != null);
		usbAudio.Microphone!.Feed(Enumerable.Repeat((short)16384, 2 * 1200).ToArray());

		Assert.True(microphoneMonitor.InUse);
		Assert.Equal(0.5f, microphoneMonitor.Levels.Snapshot()[^1], 3);

		pad.SetMicrophoneOpen(false);
		TestReports.WaitUntil(() => !microphoneMonitor.InUse);
	}

	[Fact]
	public async Task OnUsb_GamesHearTheMicrophoneBoosted_AsSet()
	{
		settings.Update(s => s with { Microphone = new MicrophoneSettings(BoostDb: 6, NoiseGate: false) });
		FakeDualSensePad pad = await ConnectDualSenseOverUsbAsync();
		pad.SetMicrophoneOpen(true);
		TestReports.WaitUntil(() => usbAudio.Microphone != null);

		usbAudio.Microphone!.Feed(Enumerable.Repeat((short)1000, 8).ToArray());

		Assert.Equal(8, pad.MicrophoneWritten.Count);
		Assert.All(pad.MicrophoneWritten, s => Assert.InRange(s, 1990, 2000));
	}

	[Fact]
	public async Task OnUsb_TheNoiseGate_IsOnByDefault()
	{
		FakeDualSensePad pad = await ConnectDualSenseOverUsbAsync();
		pad.SetMicrophoneOpen(true);
		TestReports.WaitUntil(() => usbAudio.Microphone != null);

		// A short burst of quiet sound: the gate keeps it out.
		usbAudio.Microphone!.Feed(Enumerable.Repeat((short)30, 2 * 480).ToArray());

		Assert.Equal(2 * 480, pad.MicrophoneWritten.Count);
		Assert.All(pad.MicrophoneWritten, s => Assert.Equal(0, s));
	}

	[Fact]
	public async Task OnUsb_RecordingStops_TheControllersMicrophoneIsClosed()
	{
		FakeDualSensePad pad = await ConnectDualSenseOverUsbAsync();
		pad.SetMicrophoneOpen(true);
		TestReports.WaitUntil(() => usbAudio.Microphone != null);

		pad.SetMicrophoneOpen(false);

		TestReports.WaitUntil(() => usbAudio.Microphone!.Disposed);
	}

	[Fact]
	public async Task OnUsb_TheVirtualPadGoing_ClosesTheControllersMicrophone()
	{
		FakeDualSensePad pad = await ConnectDualSenseOverUsbAsync();
		pad.SetMicrophoneOpen(true);
		TestReports.WaitUntil(() => usbAudio.Microphone != null);

		settings.Update(s => s with { Emulation = null });

		TestReports.WaitUntil(() => usbAudio.Microphone!.Disposed);
	}

	[Fact]
	public async Task OnBluetooth_RecordingFromTheVirtualMicrophone_OpensNothing()
	{
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();

		pad.SetMicrophoneOpen(true);

		Assert.Null(usbAudio.Microphone);
	}

	[Fact]
	public async Task OnBluetooth_TheControllersUsbSoundDeviceIsntUsed()
	{
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();

		pad.SendSpeaker(GameQuad());

		Assert.Null(usbAudio.Stream);
	}

	private sealed class FakeUsbAudio : IControllerUsbAudio
	{
		public FakeUsbStream? Stream { get; private set; }

		public IControllerAudioStream? Open() => Stream = new FakeUsbStream();

		public FakeMicrophone? Microphone { get; private set; }

		public IDisposable? OpenMicrophone(MicrophoneBlock blocks) => Microphone = new FakeMicrophone(blocks);
	}

	private sealed class FakeMicrophone(MicrophoneBlock blocks) : IDisposable
	{
		public bool Disposed { get; private set; }

		public void Feed(short[] stereo) => blocks(stereo);

		public void Dispose() => Disposed = true;
	}

	private sealed class FakeUsbStream : IControllerAudioStream
	{
		public List<short> Written { get; } = [];

		public bool Disposed { get; private set; }

		public void Write(ReadOnlySpan<short> samples, int channels)
		{
			Assert.Equal(4, channels);
			Written.AddRange(samples);
		}

		public void Dispose() => Disposed = true;
	}

	private async Task<(FakeDevice Device, FakeDualSensePad Pad)> ConnectDualSenseAsync()
	{
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		return (device, backend.CreatedDualSense[^1]);
	}

	[Fact]
	public async Task SwitchingBetweenBluetoothAndUsb_KeepsTheVirtualPad()
	{
		// Recreating the pad unplugged the game's controller mid-game (it froze and lost the controller's audio device).
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		ControllerSession bluetooth = controller.Session!;

		FakeDevice usb = transport.Add(FakeHidTransport.UsbPath);

		Assert.NotSame(bluetooth, controller.Session);
		Assert.Single(backend.CreatedDualSense);
		Assert.False(pad.Disposed);
		int before = pad.Submitted;
		usb.Latest.Feed(TestReports.Fixture("idle-usb"));
		TestReports.WaitUntil(() => pad.Submitted > before);
		Assert.Equal(EmulationStatus.Active, emulation.Status);
	}

	[Fact]
	public async Task GameLightsOff_KeepsYourLights_ButTheGamesTriggers()
	{
		settings.Update(s => s with { GameLights = false });
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		byte[] block = new byte[47];
		block[0] = 0x04;
		block[11] = 0x26;
		block[1] = 0x04;
		block[44] = 0xFF;

		pad.SendOutput(block);

		byte[] sent = controller.Session!.Output.GameBlock!;
		Assert.Equal(0, sent[1] & 0x04);
		Assert.Equal(0x04, sent[0] & 0x04);
	}

	[Fact]
	public async Task GameTriggersOff_KeepsYourTriggers_ButTheGamesLights()
	{
		settings.Update(s => s with { GameTriggers = false });
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		byte[] block = new byte[47];
		block[0] = 0x04;
		block[1] = 0x04;
		block[44] = 0xFF;

		pad.SendOutput(block);

		byte[] sent = controller.Session!.Output.GameBlock!;
		Assert.Equal(0, sent[0] & 0x0C);
		Assert.Equal(0x04, sent[1] & 0x04);
	}

	[Fact]
	public async Task SwitchingConnection_CarriesTheGamesLastOutputOver()
	{
		// Games send output only when it changes; the new connection must not wait for the next change.
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		byte[] block = new byte[47];
		block[0] = 0x0C;
		block[11] = 0x26;
		pad.SendOutput(block);

		transport.Add(FakeHidTransport.UsbPath);

		Assert.Equal(0x26, controller.Session!.Output.GameBlock![11]);
	}

	[Fact]
	public async Task SwitchingConnection_CarriesEverythingTheGameSet_NotJustItsLastBlock()
	{
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		byte[] triggers = new byte[47];
		triggers[0] = 0x04;
		triggers[11] = 0x26;
		pad.SendOutput(triggers);
		byte[] lights = new byte[47];
		lights[1] = 0x04;
		lights[44] = 0xFF;
		pad.SendOutput(lights);

		transport.Add(FakeHidTransport.UsbPath);

		byte[] carried = controller.Session!.Output.GameBlock!;
		Assert.Equal(0x26, carried[11]);
		Assert.Equal(0xFF, carried[44]);
		Assert.Equal(0x04, carried[0] & 0x04);
	}

	[Fact]
	public async Task ParkedPad_IsLeftNeutral_SoNothingStaysHeld()
	{
		// Switching the controller off by holding PS must not leave PS (or a pushed stick) held for the grace period.
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		device.Latest.Feed(TestReports.Fixture("cross-bt"));
		TestReports.WaitUntil(() => pad.LastSubmitted?.Buttons.HasFlag(DualPulse.Core.Protocol.Buttons.Cross) == true);

		transport.Remove(device);

		Assert.False(pad.Disposed);
		Assert.Equal(DualPulse.Core.Protocol.Buttons.None, pad.LastSubmitted!.Value.Buttons);
		Assert.Equal(128, pad.LastSubmitted.Value.LeftX);
	}

	[Fact]
	public async Task LostPad_ChoosingTheVirtualDualSenseAgain_Retries()
	{
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();

		pad.Lose();
		Assert.Equal(EmulationStatus.DriverMissing, emulation.Status);
		Assert.True(pad.Disposed);
		settings.Update(s => s with { Emulation = PadKind.Xbox360 });
		settings.Update(s => s with { Emulation = PadKind.DualSense });

		Assert.Equal(2, backend.CreatedDualSense.Count);
		Assert.Equal(EmulationStatus.Active, emulation.Status);
	}

	[Fact]
	public async Task PadLostWhileParked_IsNotReused()
	{
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		transport.Remove(device);

		pad.Lose();
		transport.Add(FakeHidTransport.BluetoothPath);
		time.Advance(TimeSpan.FromSeconds(2));

		Assert.True(pad.Disposed);
		Assert.Equal(2, backend.CreatedDualSense.Count);
		Assert.Equal(EmulationStatus.Active, emulation.Status);
	}

	[Fact]
	public async Task GameMotorRumble_FollowsTheRumbleMute_AndStopsWhenUnbound()
	{
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		byte[] block = new byte[47];
		block[0] = 0x03;
		block[2] = 120;
		block[3] = 200;

		pad.SendOutput(block);
		Assert.NotEqual((0, 0), ((int)rumble.Levels.Left, (int)rumble.Levels.Right));
		settings.Update(s => s with { Rumble = new RumbleSettings(Muted: true) });
		Assert.Equal((0, 0), ((int)rumble.Levels.Left, (int)rumble.Levels.Right));
		settings.Update(s => s with { Rumble = new RumbleSettings(), Emulation = PadKind.Xbox360 });

		Assert.Equal((0, 0), ((int)rumble.Levels.Left, (int)rumble.Levels.Right));
	}

	[Fact]
	public async Task CreatingAPad_DoesntBlockTheCaller()
	{
		// usbip attach can take seconds (up to 30 s if it hangs); neither the UI thread nor controller polling waits.
		using ManualResetEventSlim release = new();
		BlockingPadBackend blocking = new(release);
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		using EmulationService slow = new(controller, blocking, settings, rumble, NullLogger<EmulationService>.Instance);
		System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

		transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
		Assert.Equal(EmulationStatus.Off, slow.Status);
		release.Set();
		TestReports.WaitUntil(() => slow.Status == EmulationStatus.Active);
	}

	[Fact]
	public async Task GameOutputWhileParked_IsCarriedToTheNextConnection()
	{
		// The game keeps running while the controller switches; what it sets in the gap must not be lost.
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		transport.Remove(device);
		byte[] triggers = new byte[47];
		triggers[0] = 0x04;
		triggers[11] = 0x26;

		pad.SendOutput(triggers);
		transport.Add(FakeHidTransport.UsbPath);
		time.Advance(TimeSpan.FromSeconds(2));

		Assert.Equal(0x26, controller.Session!.Output.GameBlock![11]);
	}

	[Fact]
	public async Task GameMotorRumble_OverUsb_GoesOutScaledByTheRumbleSettings()
	{
		settings.Update(s => s with { Emulation = PadKind.DualSense, Rumble = new RumbleSettings(Muted: true) });
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		FakeDualSensePad pad = backend.CreatedDualSense[^1];
		byte[] block = new byte[47];
		block[0] = 0x03;
		block[1] = 0x04;
		block[2] = 120;
		block[3] = 200;
		block[44] = 0x5A;

		pad.SendOutput(block);

		TestReports.WaitUntil(() => device.Latest.Writes.Any(w => w[0] == 0x02 && w[1 + 44] == 0x5A));
		byte[] sent = device.Latest.Writes.Last(w => w[0] == 0x02 && w[1 + 44] == 0x5A);
		Assert.Equal(0, sent[1 + 2]);
		Assert.Equal(0, sent[1 + 3]);
	}

	[Fact]
	public async Task PadLostBeforeItsLostEventWasHeard_CountsAsLost()
	{
		backend.CreateLost = true;

		(_, _) = await ConnectDualSenseAsync();

		Assert.Equal(EmulationStatus.DriverMissing, emulation.Status);
		Assert.True(backend.CreatedDualSense[^1].Disposed);
	}

	[Fact]
	public async Task UnavailableBackend_IsLoggedOnce_NotOnEverySettingsChange()
	{
		ListLogger<EmulationService> log = new();
		backend.IsAvailable = false;
		settings.Update(s => s with { Emulation = PadKind.Xbox360 });
		using EmulationService quiet = new(controller, backend, settings, rumble, log, padWorker: Inline);
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		settings.Update(s => s with { StartMinimized = true });
		settings.Update(s => s with { StartMinimized = false });

		Assert.Single(log.Entries, e => e.Message.StartsWith("Emulation unavailable", StringComparison.Ordinal));
	}

	[Fact]
	public async Task StaleCreates_DontPlugInAPad()
	{
		// DualSense (still plugging in) -> Xbox -> DualSense: the Xbox pad is no longer wanted by the time it would start.
		using ManualResetEventSlim release = new();
		BlockingPadBackend blocking = new(release);
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		using EmulationService slow = new(controller, blocking, settings, rumble, NullLogger<EmulationService>.Instance);
		transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		TestReports.WaitUntil(() => blocking.Started == 1);

		settings.Update(s => s with { Emulation = PadKind.Xbox360 });
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		release.Set();

		TestReports.WaitUntil(() => slow.Status == EmulationStatus.Active);
		Assert.DoesNotContain(PadKind.Xbox360, blocking.Kinds);
	}

	[Fact]
	public async Task PadCreatedDuringShutdown_IsUnpluggedAgain()
	{
		using ManualResetEventSlim release = new();
		BlockingPadBackend blocking = new(release);
		settings.Update(s => s with { Emulation = PadKind.DualSense });
		EmulationService slow = new(controller, blocking, settings, rumble, NullLogger<EmulationService>.Instance);
		transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		TestReports.WaitUntil(() => blocking.Started == 1);

		Task disposing = Task.Run(slow.Dispose, TestContext.Current.CancellationToken);
		Thread.Sleep(100);
		release.Set();
		await disposing;

		TestReports.WaitUntil(() => blocking.Pads.Count == 1 && blocking.Pads[0].Disposed);
	}

	private sealed class BlockingPadBackend(ManualResetEventSlim release) : IVirtualPadBackend
	{
		private int started;

		public int Started => Volatile.Read(ref started);

		public List<PadKind> Kinds { get; } = [];

		public List<FakeDualSensePad> Pads { get; } = [];

		public bool IsAvailable => true;

		public string? UnavailableReason => null;

		public IVirtualPad Create(PadKind kind)
		{
			lock (Kinds)
			{
				Kinds.Add(kind);
			}
			Interlocked.Increment(ref started);
			release.Wait(TimeSpan.FromSeconds(10));
			FakeDualSensePad pad = new();
			lock (Pads)
			{
				Pads.Add(pad);
			}
			return pad;
		}
	}

	[Fact]
	public async Task ControllerGone_UnplugsTheVirtualPad_AfterAGracePeriod()
	{
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();

		transport.Remove(device);
		time.Advance(TimeSpan.FromSeconds(9));
		Assert.False(pad.Disposed);
		time.Advance(TimeSpan.FromSeconds(1));

		TestReports.WaitUntil(() => pad.Disposed);
		Assert.Equal(EmulationStatus.Off, emulation.Status);
	}

	[Fact]
	public async Task ControllerBackWithinTheGracePeriod_ReusesThePad()
	{
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();

		transport.Remove(device);
		time.Advance(TimeSpan.FromSeconds(5));
		transport.Add(FakeHidTransport.BluetoothPath);
		time.Advance(TimeSpan.FromSeconds(10));

		Assert.Single(backend.CreatedDualSense);
		Assert.False(pad.Disposed);
	}

	[Fact]
	public async Task DualSense_GameOutputBlock_DrivesTheController_UntilUnbound()
	{
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		byte[] block = new byte[47];
		block[1] = 0x04;
		block[44] = 0xAB;

		pad.SendOutput(block);
		Assert.Equal(0xAB, controller.Session!.Output.GameBlock![44]);

		settings.Update(s => s with { Emulation = PadKind.Xbox360 });
		Assert.Null(controller.Session.Output.GameBlock);
		Assert.True(pad.Disposed);
	}

	[Fact]
	public async Task DualSense_GameHaptics_PlayOverBluetooth()
	{
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		short[] tone = new short[4800 * 2];
		for (int i = 0; i < 4800; i++)
		{
			tone[2 * i] = tone[2 * i + 1] = (short)(20000 * Math.Sin(2 * Math.PI * 100 * i / 48000));
		}

		pad.SendHaptics(tone);

		TestReports.WaitUntil(() => device.Latest.Writes.Any(w => w[0] == 0x32 && w[13..77].Any(b => b != 0)));
	}

	[Fact]
	public async Task DualSense_RepeatedGameBlocks_DontGoToTheControllerAgain()
	{
		// Games re-send their output every frame; over Bluetooth each copy would compete with the audio reports.
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		byte[] block = new byte[47];
		block[0] = 0x0C;
		block[11] = 0x26;
		pad.SendOutput(block);
		byte[] applied = controller.Session!.Output.GameBlock!;

		pad.SendOutput([.. block]);
		Assert.Same(applied, controller.Session.Output.GameBlock);

		block[11] = 0x21;
		pad.SendOutput([.. block]);
		Assert.Equal(0x21, controller.Session.Output.GameBlock![11]);
	}

	[Fact]
	public async Task DualSense_AudioRoute_HoldsUntilTheGameSetsItAgain()
	{
		// The game sets the audio route (byte 7) only in blocks flagged 0x80; its other blocks carry 0 there, which
		// would otherwise read as "headset" and send the speaker audio nowhere.
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		Assert.False(controller.Session!.Output.UsesHeadset);
		byte[] speaker = new byte[47];
		speaker[0] = 0x80;
		speaker[7] = 0x3C;
		byte[] triggers = new byte[47];
		triggers[0] = 0x0C;
		byte[] headset = new byte[47];
		headset[0] = 0x80;
		headset[7] = 0x0C;

		pad.SendOutput(triggers);
		Assert.False(controller.Session.Output.UsesHeadset);
		pad.SendOutput(speaker);
		pad.SendOutput(triggers);
		Assert.False(controller.Session.Output.UsesHeadset);
		pad.SendOutput(headset);
		pad.SendOutput(triggers);
		Assert.True(controller.Session.Output.UsesHeadset);
	}

	private static short[] GameHapticsTone(double amplitude)
	{
		short[] tone = new short[4800 * 2];
		for (int i = 0; i < 4800; i++)
		{
			tone[2 * i] = tone[2 * i + 1] = (short)(amplitude * 32767 * Math.Sin(2 * Math.PI * 100 * i / 48000));
		}
		return tone;
	}

	[Fact]
	public async Task DualSense_QuietGameHaptics_AreBoosted()
	{
		// Games' haptics often peak around 15 % of full scale; played 1:1 over Bluetooth they were barely felt.
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();

		pad.SendHaptics(GameHapticsTone(0.15));

		TestReports.WaitUntil(() => device.Latest.Writes.Any(w => w[0] == 0x32 && w[13..77].Any(b => Math.Abs((sbyte)b) >= 40)));
	}

	[Fact]
	public async Task DualSense_GameHaptics_FollowTheRumbleMute()
	{
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		settings.Update(s => s with { Rumble = new RumbleSettings(Muted: true) });

		pad.SendHaptics(GameHapticsTone(0.8));
		Thread.Sleep(300);

		Assert.DoesNotContain(device.Latest.Writes, w => w[0] == 0x32 && w[13..77].Any(b => b != 0));
	}

	[Fact]
	public async Task DualSense_GameMotorRumble_PlaysAsHapticsOverBluetooth()
	{
		// The rumble bits can't go to the controller over Bluetooth (they would end audio haptics), so motor values a game
		// sends are played through the haptics stream instead.
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		byte[] block = new byte[47];
		block[0] = 0x03;
		block[2] = 120;
		block[3] = 200;

		pad.SendOutput(block);

		TestReports.WaitUntil(() => device.Latest.Writes.Any(w => w[0] == 0x32 && w[13..77].Any(b => b != 0)));
	}

	private static short[] SpeakerTone()
	{
		short[] tone = new short[4800 * 4];
		for (int i = 0; i < 4800; i++)
		{
			tone[4 * i] = tone[4 * i + 1] = (short)(10000 * Math.Sin(2 * Math.PI * 440 * i / 48000));
		}
		return tone;
	}

	[Fact]
	public async Task DualSense_GameSpeakerAudio_StreamsReport0x39_WhenGameSoundOnTheControllerIsOn()
	{
		settings.Update(s => s with { GameSoundOnController = true });
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();

		pad.SendSpeaker(SpeakerTone());

		TestReports.WaitUntil(() => device.Latest.Writes.Any(w => w[0] == 0x39));
	}

	[Fact]
	public async Task DualSense_GameSpeakerAudio_StaysOnThePc_ByDefault()
	{
		// Few people want game sound from the controller's small speaker; without it the haptics keep report 0x32.
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();

		pad.SendSpeaker(SpeakerTone());
		pad.SendHaptics(GameHapticsTone(0.5));

		TestReports.WaitUntil(() => device.Latest.Writes.Any(w => w[0] == 0x32 && w[13..77].Any(b => b != 0)));
		Assert.DoesNotContain(device.Latest.Writes, w => w[0] == 0x39);
	}

	[Fact]
	public async Task DualSense_EveryReportIsForwarded_EvenWhenOnlyMotionChanges()
	{
		(FakeDevice device, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		byte[] first = TestReports.Fixture("idle-bt");
		byte[] second = TestReports.Fixture("idle-bt");
		second[2 + 15] ^= 0x10;

		device.Latest.Feed(first);
		device.Latest.Feed(second);

		TestReports.WaitUntil(() => pad.Submitted >= 2);
	}

	[Fact]
	public async Task DualSense_LostDevice_Unbinds_AndReportsIt()
	{
		(_, FakeDualSensePad pad) = await ConnectDualSenseAsync();
		pad.SendOutput(new byte[47]);

		pad.Lose();

		TestReports.WaitUntil(() => emulation.Status == EmulationStatus.DriverMissing);
		Assert.Null(controller.Session!.Output.GameBlock);
		Assert.True(pad.Disposed);
	}
}
