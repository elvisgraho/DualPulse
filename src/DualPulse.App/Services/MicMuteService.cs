using Microsoft.Extensions.Hosting;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Emulation;

namespace DualPulse.App.Services;

public interface IMicMuteService
{
	// Whether the Windows microphone is muted; null when there is no recording device.
	bool? IsMicMuted { get; }

	// Raised when the mute state changes, on whichever thread noticed it.
	event Action? Changed;
}

// The Mute button mutes the Windows microphone (while the setting is on); the mute LED shows the result through
// LightingService. A press toggles once however long it is held. Runs on the read thread.
public sealed class MicMuteService : IMicMuteService, IHostedService, IDisposable
{
	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly IMicrophone mic;

	private readonly object gate = new();

	private ControllerSession? watched;

	private bool wasHeld;

	public MicMuteService(IControllerService controller, ISettingsService settings, IMicrophone mic)
	{
		this.controller = controller;
		this.settings = settings;
		this.mic = mic;
		mic.Changed += OnMicChanged;
		controller.SessionStarted += Watch;
		controller.SessionEnded += Unwatch;
		if (controller.Session is { } session)
		{
			Watch(session);
		}
	}

	public bool? IsMicMuted => mic.IsMuted;

	public event Action? Changed;

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		mic.Changed -= OnMicChanged;
		controller.SessionStarted -= Watch;
		controller.SessionEnded -= Unwatch;
		lock (gate)
		{
			if (watched != null)
			{
				watched.InputReceived -= OnInput;
				watched = null;
			}
		}
	}

	private void Watch(ControllerSession session)
	{
		lock (gate)
		{
			if (watched != null)
			{
				watched.InputReceived -= OnInput;
			}
			watched = session;
			wasHeld = false;
			session.InputReceived += OnInput;
		}
	}

	private void Unwatch(ControllerSession session)
	{
		lock (gate)
		{
			if (watched == session)
			{
				session.InputReceived -= OnInput;
				watched = null;
			}
		}
	}

	private void OnInput(InputSnapshot input)
	{
		bool held = (input.Buttons & Buttons.Mute) != 0;
		bool pressed;
		lock (gate)
		{
			pressed = held && !wasHeld;
			wasHeld = held;
		}
		if (pressed && settings.Current.EffectiveLighting.MuteButtonMutesMic)
		{
			mic.Toggle();
		}
	}

	private void OnMicChanged() => Changed?.Invoke();
}
