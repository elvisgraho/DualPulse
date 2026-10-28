using Microsoft.Extensions.Hosting;
using DualPulse.Core.Steam;

namespace DualPulse.App.Services;

// Notices when Steam Input is on for the game being played, which then can take the adaptive triggers and lights from
// DualPulse: checked every 3 s while a controller is connected and Steam runs. Names the game (kept while another
// window has focus) and says so once per game per run. Changed is raised on the timer thread.
public sealed class SteamInputWatch : IHostedService, IDisposable
{
	public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(3);

	private readonly ISteamClient steam;

	private readonly IControllerService controller;

	private readonly INotifier? notifier;

	private readonly ITimer timer;

	private readonly object gate = new();

	private readonly HashSet<int> told = [];

	public SteamInputWatch(ISteamClient steam, IControllerService controller, INotifier? notifier = null, TimeProvider? time = null)
	{
		this.steam = steam;
		this.controller = controller;
		this.notifier = notifier;
		timer = (time ?? TimeProvider.System).CreateTimer(_ => Check(), null, CheckInterval, CheckInterval);
	}

	public bool Active { get; private set; }

	public SteamApp? Game { get; private set; }

	public event Action? Changed;

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose() => timer.Dispose();

	private void Check()
	{
		bool active;
		try
		{
			active = controller.Session != null && steam.IsRunning() && steam.InputActive();
		}
		catch (Exception)
		{
			active = false;
		}
		SteamApp? game = null;
		bool tell = false;
		bool changed;
		lock (gate)
		{
			if (active)
			{
				game = SafeForegroundGame() ?? Game;
				tell = told.Add(game?.AppId ?? 0);
			}
			changed = active != Active || game != Game;
			Active = active;
			Game = game;
		}
		if (tell)
		{
			notifier?.Show(game == null ? "Steam Input is on for this game" : $"Steam Input is on for {game.Name}",
				"It can take your adaptive triggers and lights. Turn it off in Steam: the game's Properties, Controller.");
		}
		if (changed)
		{
			Changed?.Invoke();
		}
	}

	private SteamApp? SafeForegroundGame()
	{
		try
		{
			return steam.ForegroundGame();
		}
		catch (Exception)
		{
			return null;
		}
	}
}
