using Microsoft.Extensions.Hosting;
using DualPulse.Core.Devices;
using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

public interface IPcControlService
{
	// While on, the controller drives the mouse and keyboard and the virtual pad is paused.
	bool IsActive { get; set; }

	event Action? ActiveChanged;
}

// PC control: switched with PS+Options on the controller (or from the page and tray). Input is processed on the
// controller's read thread and sent straight to Windows; switching off lets go of every held key and button.
public sealed class PcControlService : IPcControlService, IHostedService, IDisposable
{
	// TouchpadMouse pixels per touch unit at TouchpadSpeed 1.
	private const double BaseTouchpadSpeed = 0.6;

	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly IPcOutput output;

	private readonly TimeProvider time;

	private readonly long start;

	private readonly object gate = new();

	private readonly PcControl control = new();

	private readonly PcControlSwitch toggle = new();

	private ControllerSession? watched;

	private bool active;

	public PcControlService(IControllerService controller, ISettingsService settings, IPcOutput output, TimeProvider time)
	{
		this.controller = controller;
		this.settings = settings;
		this.output = output;
		this.time = time;
		start = time.GetTimestamp();
		ApplySettings(settings.Current);
		settings.Changed += OnSettingsChanged;
		controller.SessionStarted += Watch;
		controller.SessionEnded += Unwatch;
		if (controller.Session is { } session)
		{
			Watch(session);
		}
	}

	public event Action? ActiveChanged;

	public bool IsActive
	{
		get => active;
		set
		{
			lock (gate)
			{
				if (active == value)
				{
					return;
				}
				active = value;
				if (!value)
				{
					control.Release(output);
				}
			}
			ActiveChanged?.Invoke();
		}
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		IsActive = false;
		settings.Changed -= OnSettingsChanged;
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
				control.Release(output);
			}
		}
	}

	// Read thread.
	private void OnInput(InputSnapshot snapshot)
	{
		if (toggle.Pressed(snapshot.Buttons))
		{
			IsActive = !IsActive;
			return;
		}
		lock (gate)
		{
			if (active)
			{
				control.Process(snapshot, time.GetElapsedTime(start), output);
			}
		}
	}

	private void OnSettingsChanged(AppSettings current)
	{
		lock (gate)
		{
			ApplySettings(current);
		}
	}

	private void ApplySettings(AppSettings current)
	{
		MappingSettings mapping = current.EffectiveMapping;
		control.TouchpadEnabled = mapping.TouchpadEnabled;
		control.Touchpad.Speed = BaseTouchpadSpeed * Math.Clamp(mapping.TouchpadSpeed, 0.1, 5);
		control.Sticks.MoveEnabled = mapping.StickMouseEnabled;
		control.Sticks.Speed = Math.Clamp(mapping.StickSpeed, 0.1, 5);
		control.Sticks.ScrollEnabled = mapping.StickScrollEnabled;
		IReadOnlyDictionary<Buttons, PcAction> actions = mapping.ButtonActions();
		// While the Mute button mutes the microphone, it isn't also volume mute (that stays on the R1 media layer).
		if (current.EffectiveLighting.MuteButtonMutesMic && actions.ContainsKey(Buttons.Mute))
		{
			actions = actions.Where(a => a.Key != Buttons.Mute).ToDictionary();
		}
		control.ButtonActions = actions;
		control.WindowSwitcherEnabled = mapping.WindowSwitcherEnabled;
		control.MediaLayerEnabled = mapping.MediaLayerEnabled;
	}
}
