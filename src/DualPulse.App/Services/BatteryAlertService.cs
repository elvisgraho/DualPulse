using Microsoft.Extensions.Hosting;
using DualPulse.Core.Devices;
using DualPulse.Core.Features;

namespace DualPulse.App.Services;

// Shows a short notification on screen; duration null keeps the usual few seconds.
public interface INotifier
{
	void Show(string title, string message, TimeSpan? duration = null);
}

// Warns once when the controller's battery drops to 20% and again at 10% while discharging (BatteryNotifier's
// thresholds and re-arming). Checked every 30 seconds; nothing runs the check while no controller is bound.
public sealed class BatteryAlertService : IHostedService, IDisposable
{
	public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

	private readonly IControllerService controller;

	private readonly INotifier notifier;

	private readonly ITimer timer;

	private readonly object gate = new();

	private BatteryNotifier batteryNotifier = new();

	public BatteryAlertService(IControllerService controller, INotifier notifier, TimeProvider time)
	{
		this.controller = controller;
		this.notifier = notifier;
		controller.SessionStarted += OnSessionStarted;
		timer = time.CreateTimer(_ => Check(), null, CheckInterval, CheckInterval);
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		controller.SessionStarted -= OnSessionStarted;
		timer.Dispose();
	}

	private void OnSessionStarted(ControllerSession session)
	{
		lock (gate)
		{
			batteryNotifier = new BatteryNotifier();
		}
	}

	private void Check()
	{
		if (controller.Session is not { } session)
		{
			return;
		}
		DualSenseBattery battery = session.Battery;
		string? message;
		lock (gate)
		{
			message = batteryNotifier.Evaluate(battery.Level, battery.State);
		}
		if (message != null)
		{
			notifier.Show("Controller battery", message);
		}
	}
}
