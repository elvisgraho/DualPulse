using Microsoft.Extensions.Hosting;
using DualPulse.Core.Audio;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

// System audio felt through the grips: while the setting is on and the controller is on Bluetooth with haptics
// allowed, captures what the computer plays and feeds it into the session's haptics mixer (mixed with game rumble).
// Capture runs only while needed, and not while a virtual DualSense is active (a game's own haptics play then).
public sealed class AudioHapticsService : IHostedService, IDisposable
{
	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly ISystemAudioSource source;

	private readonly object gate = new();

	private ControllerSession? feeding;

	private HapticsAudioConverter? converter;

	private readonly IEmulationService? emulation;

	public AudioHapticsService(IControllerService controller, ISettingsService settings, ISystemAudioSource source, IEmulationService? emulation = null)
	{
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
		bool wanted = rumble.AudioHaptics && rumble.BluetoothHaptics && session?.Transport == Transport.Bluetooth
			&& !VirtualDualSenseActive.In(settings, emulation);
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
		current.Process(interleaved, session.HapticMixer);
		session.WakeHaptics();
	}
}

// Whether a virtual DualSense is chosen and actually running (a game's own haptics and speaker audio play then).
// Without the emulation service, the choice alone counts.
internal static class VirtualDualSenseActive
{
	public static bool In(ISettingsService settings, IEmulationService? emulation) =>
		settings.Current.Emulation == DualPulse.Emulation.PadKind.DualSense && (emulation == null || emulation.Status == EmulationStatus.Active);
}
