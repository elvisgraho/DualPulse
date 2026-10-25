using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

public interface IControllerService
{
	ControllerSession? Session { get; }

	// Why a connected controller couldn't be opened (e.g. another app holds it exclusively); null otherwise.
	string? OpenError { get; }

	event Action? SessionChanged;

	event Action<ControllerSession>? SessionStarted;

	event Action<ControllerSession>? SessionEnded;
}

// Owns the ControllerManager and keeps the bound controller's trigger effects in line with the settings (lighting is
// LightingService's).
public sealed class ControllerService : IControllerService, IHostedService, IDisposable
{
	private readonly ControllerManager manager;

	private readonly ISettingsService settings;

	private readonly ModOverride? mods;

	private readonly IProfileService? profiles;

	public ControllerService(IHidTransport transport, TimeProvider time, ISettingsService settings, ILogger<ControllerManager> logger,
		ModOverride? mods = null, IProfileService? profiles = null)
	{
		this.settings = settings;
		this.mods = mods;
		this.profiles = profiles;
		manager = new ControllerManager(transport, time, logger);
		manager.SessionStarted += OnSessionStarted;
		manager.SessionEnded += OnSessionEnded;
		// With game profiles, the effective settings (saved + active profile) drive the triggers.
		if (profiles != null)
		{
			profiles.Changed += OnProfilesChanged;
		}
		else
		{
			settings.Changed += OnSettingsChanged;
		}
		if (mods != null)
		{
			mods.Released += OnModReleased;
		}
	}

	public ControllerSession? Session => manager.Current;

	public string? OpenError => manager.OpenError;

	public event Action? SessionChanged;

	public event Action<ControllerSession>? SessionStarted;

	public event Action<ControllerSession>? SessionEnded;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		manager.Start();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		settings.Changed -= OnSettingsChanged;
		if (profiles != null)
		{
			profiles.Changed -= OnProfilesChanged;
		}
		if (mods != null)
		{
			mods.Released -= OnModReleased;
		}
		manager.Dispose();
	}

	// Triggers a game mod currently controls are left alone.
	private void ApplySettings(ControllerSession session, AppSettings current)
	{
		TriggerSettings triggers = current.EffectiveTriggers;
		TriggerEffect left = triggers.Left.ToEffect();
		TriggerEffect right = triggers.EffectiveRight.ToEffect();
		bool keepLeft = mods?.ControlsLeftTrigger == true;
		bool keepRight = mods?.ControlsRightTrigger == true;
		session.Apply(state => state with { LeftTrigger = keepLeft ? state.LeftTrigger : left, RightTrigger = keepRight ? state.RightTrigger : right });
	}

	private void OnModReleased()
	{
		if (Session is { } session)
		{
			ApplySettings(session, Current);
		}
	}

	private void OnSessionStarted(ControllerSession session)
	{
		ApplySettings(session, Current);
		SessionStarted?.Invoke(session);
		SessionChanged?.Invoke();
	}

	private void OnSessionEnded(ControllerSession session, string reason)
	{
		SessionEnded?.Invoke(session);
		SessionChanged?.Invoke();
	}

	private AppSettings Current => profiles?.Effective ?? settings.Current;

	private void OnProfilesChanged() => OnSettingsChanged(Current);

	private void OnSettingsChanged(AppSettings current)
	{
		if (Session is { } session)
		{
			ApplySettings(session, current);
		}
	}
}
