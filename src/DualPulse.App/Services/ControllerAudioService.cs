using Microsoft.Extensions.Hosting;
using DualPulse.Core.Audio;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

// PC sound on the controller over Bluetooth: while enabled, captures the chosen output device into the session's
// speaker stream and sets the audio path (Auto follows the headset jack) and volume. Over USB the controller is
// already a Windows sound device, so nothing happens there. Nothing happens either while a virtual DualSense is
// active: a game's own speaker audio goes there.
public sealed class ControllerAudioService : IHostedService, IDisposable
{
	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly ISystemAudioSourceFactory sources;

	private readonly object gate = new();

	private ControllerSession? feeding;

	private ISystemAudioSource? source;

	private string? sourceDevice;

	private ControllerSession? watched;

	private bool plugged;

	private readonly IEmulationService? emulation;

	public ControllerAudioService(IControllerService controller, ISettingsService settings, ISystemAudioSourceFactory sources, IEmulationService? emulation = null)
	{
		this.controller = controller;
		this.settings = settings;
		this.sources = sources;
		this.emulation = emulation;
		// Raised under the emulation service's lock: re-evaluate elsewhere (starting capture can take a moment).
		if (emulation != null)
		{
			emulation.StatusChanged += OnEmulationChanged;
		}
		settings.Changed += OnSettingsChanged;
		controller.SessionChanged += Update;
		Update();
	}

	public IReadOnlyList<AudioDevice> OutputDevices() => sources.OutputDevices();

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		settings.Changed -= OnSettingsChanged;
		if (emulation != null)
		{
			emulation.StatusChanged -= OnEmulationChanged;
		}
		controller.SessionChanged -= Update;
		lock (gate)
		{
			StopCapture();
			Watch(null);
		}
	}

	private void OnSettingsChanged(AppSettings current) => Update();

	private void OnEmulationChanged() => Task.Run(Update);

	private void Update()
	{
		ControllerAudioSettings audio = settings.Current.EffectiveAudio;
		ControllerSession? session = controller.Session;
		bool wanted = audio.Enabled && session?.Transport == Transport.Bluetooth && !VirtualDualSenseActive.In(settings, emulation);
		lock (gate)
		{
			Watch(session);
			if (!wanted)
			{
				StopCapture();
				if (session != null && session.Output.AudioOutput != AudioOutput.Off)
				{
					session.Apply(state => state with { AudioOutput = AudioOutput.Off });
				}
				return;
			}
			if (feeding != session || sourceDevice != audio.DeviceId || source == null)
			{
				StopCapture();
				ISystemAudioSource created = sources.Create(audio.DeviceId);
				created.Blocks += OnBlock;
				feeding = session;
				source = created;
				sourceDevice = audio.DeviceId;
				if (!created.Start())
				{
					StopCapture();
				}
			}
			ApplyPath(session!, audio);
		}
	}

	// Caller holds gate.
	private void ApplyPath(ControllerSession session, ControllerAudioSettings audio)
	{
		AudioOutput output = audio.Output switch
		{
			ControllerAudioOutput.Speaker => AudioOutput.Speaker,
			ControllerAudioOutput.Earbuds => AudioOutput.Headset,
			_ => plugged ? AudioOutput.Headset : AudioOutput.Speaker
		};
		byte volume = (byte)Math.Clamp(audio.Volume, 0, 100);
		if (session.Output.AudioOutput != output || session.Output.AudioVolume != volume)
		{
			session.Apply(state => state with { AudioOutput = output, AudioVolume = volume });
		}
	}

	// Caller holds gate.
	private void StopCapture()
	{
		if (source != null)
		{
			source.Blocks -= OnBlock;
			source.Dispose();
		}
		source = null;
		sourceDevice = null;
		feeding = null;
	}

	// Caller holds gate. Follows the headset jack of the current session (for Auto).
	private void Watch(ControllerSession? session)
	{
		if (watched == session)
		{
			return;
		}
		if (watched != null)
		{
			watched.InputReceived -= OnInput;
		}
		watched = session;
		plugged = false;
		if (session != null)
		{
			session.InputReceived += OnInput;
		}
	}

	// Read thread.
	private void OnInput(InputSnapshot input)
	{
		if (input.HeadphonesPlugged == plugged)
		{
			return;
		}
		lock (gate)
		{
			plugged = input.HeadphonesPlugged;
			if (feeding is { } session)
			{
				ApplyPath(session, settings.Current.EffectiveAudio);
			}
		}
	}

	// Capture thread.
	private void OnBlock(ReadOnlySpan<float> interleaved)
	{
		ControllerSession? session;
		ISystemAudioSource? current;
		lock (gate)
		{
			session = feeding;
			current = source;
		}
		if (session == null || current == null)
		{
			return;
		}
		session.Speaker.Push(interleaved, current.SampleRate, current.Channels);
		session.WakeAudio();
	}
}
