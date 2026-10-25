using Microsoft.Extensions.Hosting;
using DualPulse.Emulation;

namespace DualPulse.App.Services;

public interface IProfileService
{
	// The profile applied now, or null.
	GameProfile? Active { get; }

	// The saved settings with the active profile's parts laid on top; what the controller, gyro and emulation use.
	AppSettings Effective { get; }

	// Raised when the active profile or the saved settings change, on whichever thread caused it.
	event Action? Changed;
}

// Applies a game's profile while its window has focus, otherwise the named profile picked by hand (if any). DualPulse's
// own window keeps whatever is applied, so the user can tweak a profile while playing. The saved settings are never
// rewritten here.
public sealed class ProfileService : IProfileService, IHostedService, IDisposable
{
	private readonly ISettingsService settings;

	private readonly IForegroundWatcher watcher;

	private readonly string ownExe;

	private readonly object gate = new();

	private string? gameExe;

	public ProfileService(ISettingsService settings, IForegroundWatcher watcher, string ownExe)
	{
		this.settings = settings;
		this.watcher = watcher;
		this.ownExe = ownExe;
		Effective = settings.Current;
		settings.Changed += OnSettingsChanged;
		watcher.Changed += OnForegroundChanged;
		OnForegroundChanged(watcher.ForegroundExe);
	}

	public GameProfile? Active { get; private set; }

	public AppSettings Effective { get; private set; }

	public event Action? Changed;

	public static AppSettings Merge(AppSettings saved, GameProfile? profile)
	{
		if (profile == null)
		{
			return saved;
		}
		GyroSettings savedGyro = saved.EffectiveGyro;
		// Calibration belongs to the controller, not the game.
		GyroSettings? gyro = profile.Gyro is { } own ? own with { OffsetX = savedGyro.OffsetX, OffsetY = savedGyro.OffsetY, OffsetZ = savedGyro.OffsetZ } : saved.Gyro;
		return saved with { Triggers = profile.Triggers ?? saved.Triggers, Gyro = gyro, Sticks = profile.Sticks ?? saved.Sticks };
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		settings.Changed -= OnSettingsChanged;
		watcher.Changed -= OnForegroundChanged;
	}

	private void OnForegroundChanged(string? exe)
	{
		if (exe != null && string.Equals(exe, ownExe, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		lock (gate)
		{
			gameExe = exe;
		}
		Refresh(settings.Current);
	}

	private void OnSettingsChanged(AppSettings current) => Refresh(current);

	private void Refresh(AppSettings saved)
	{
		lock (gate)
		{
			string? exe = gameExe;
			GameProfile? game = exe == null ? null : saved.EffectiveProfiles.FirstOrDefault(p => !p.IsNamed && string.Equals(p.Exe, exe, StringComparison.OrdinalIgnoreCase));
			Active = game ?? saved.EffectiveProfiles.FirstOrDefault(p => p.IsNamed && p.Exe == saved.ChosenProfile);
			Effective = Merge(saved, Active);
		}
		Changed?.Invoke();
	}
}
