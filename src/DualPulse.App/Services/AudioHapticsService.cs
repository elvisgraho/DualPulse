using Microsoft.Extensions.Hosting;
using DualPulse.Core.Audio;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

// System audio felt through the grips: while the setting is on, captures what the computer plays and turns it into
// haptics. Over Bluetooth (with haptics allowed) it goes into the session's haptics mixer, mixed with game rumble; on a
// cable into channels 3-4 of the controller's own sound device, as games play haptics natively (never the rumble
// motors, which would switch the haptics off until the controller is power-cycled). Capture runs only while needed,
// and not while a virtual DualSense is active (a game's own haptics play then).
public sealed class AudioHapticsService : IHostedService, IDisposable
{
	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly ISystemAudioSource source;

	private readonly object gate = new();

	private ControllerSession? feeding;

	private HapticsAudioConverter? converter;

	private readonly IEmulationService? emulation;

	private readonly IControllerUsbAudio? usbAudio;

	// On a cable: the controller's sound device (opened on the first block; tried again at most once a second).
	private IControllerAudioStream? usbStream;

	private const long NeverTried = -10_000;

	private long usbOpenTried = NeverTried;

	private readonly UsbHapticsStream usbFrames = new();

	public AudioHapticsService(IControllerService controller, ISettingsService settings, ISystemAudioSource source, IEmulationService? emulation = null,
		IControllerUsbAudio? usbAudio = null)
	{
		this.usbAudio = usbAudio;
		this.controller = controller;
		this.settings = settings;
		this.source = source;
		this.emulation = emulation;
		// Raised under the emulation service's lock: re-evaluate elsewhere (starting capture can take a moment).
		if (emulation != null)
		{
			emulation.StatusChanged += OnEmulationChanged;
		}
		source.Blocks += OnBlock;
		settings.Changed += OnSettingsChanged;
		controller.SessionChanged += Update;
		Update();
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		source.Blocks -= OnBlock;
		settings.Changed -= OnSettingsChanged;
		if (emulation != null)
		{
			emulation.StatusChanged -= OnEmulationChanged;
		}
		controller.SessionChanged -= Update;
		lock (gate)
		{
			StopFeeding();
		}
	}

	private void OnSettingsChanged(AppSettings current) => Update();

	private void OnEmulationChanged() => Task.Run(Update);

	private void Update()
	{
		RumbleSettings rumble = settings.Current.EffectiveRumble;
		ControllerSession? session = controller.Session;
		bool overBluetooth = session?.Transport == Transport.Bluetooth && rumble.BluetoothHaptics;
		bool overCable = session?.Transport == Transport.Usb && usbAudio != null;
		bool wanted = rumble.AudioHaptics && (overBluetooth || overCable) && !VirtualDualSenseActive.In(settings, emulation);
		lock (gate)
		{
			if (!wanted)
			{
				StopFeeding();
				return;
			}
			if (feeding != session)
			{
				StopFeeding();
				if (!source.Start())
				{
					return;
				}
				converter = new HapticsAudioConverter(source.SampleRate, source.Channels);
				feeding = session;
			}
			converter!.Intensity = rumble.AudioHapticsIntensity;
			converter.Style = rumble.AudioHapticsStyle;
		}
	}

	// Caller holds gate.
	private void StopFeeding()
	{
		if (feeding != null)
		{
			source.Stop();
		}
		feeding = null;
		converter = null;
		usbStream?.Dispose();
		usbStream = null;
		usbOpenTried = NeverTried;
	}

	// Capture thread.
	private void OnBlock(ReadOnlySpan<float> interleaved)
	{
		ControllerSession? session;
		HapticsAudioConverter? current;
		lock (gate)
		{
			session = feeding;
			current = converter;
		}
		if (session == null || current == null)
		{
			return;
		}
		if (session.Transport == Transport.Usb)
		{
			PlayOnCable(interleaved, current);
			return;
		}
		current.Process(interleaved, session.HapticMixer);
		session.WakeHaptics();
	}

	// Capture thread.
	private void PlayOnCable(ReadOnlySpan<float> interleaved, HapticsAudioConverter current)
	{
		IControllerAudioStream? stream;
		lock (gate)
		{
			if (usbStream == null && usbAudio != null && Environment.TickCount64 - usbOpenTried >= 1000)
			{
				usbOpenTried = Environment.TickCount64;
				usbStream = usbAudio.Open();
			}
			stream = usbStream;
		}
		if (stream == null)
		{
			return;
		}
		current.Process(interleaved, usbFrames.Push);
		short[] samples = usbFrames.Drain();
		if (samples.Length > 0)
		{
			stream.Write(samples, UsbHapticsStream.ChannelCount);
		}
	}
}

// Whether a virtual DualSense is chosen and actually running (a game's own haptics and speaker audio play then).
// Without the emulation service, the choice alone counts.
internal static class VirtualDualSenseActive
{
	public static bool In(ISettingsService settings, IEmulationService? emulation) =>
		settings.Current.Emulation == DualPulse.Emulation.PadKind.DualSense && (emulation == null || emulation.Status == EmulationStatus.Active);
}
