using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DualPulse.Core.Devices;
using DualPulse.Core.Power;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

public interface IPowerService
{
	// Turns the bound Bluetooth controller off; false over USB, without a controller, or if the radio refused.
	bool PowerOffNow(string reason);
}

// Bluetooth power-off: the PS+Triangle shortcut, idle auto-off and power-off on exit. The policy runs from the read
// thread, throttled (16 ms for the shortcut, 10 s for the idle check); the power-off itself runs on the thread pool.
public sealed class PowerService : IPowerService, IHostedService, IDisposable
{
	private static readonly TimeSpan ShortcutInterval = TimeSpan.FromMilliseconds(16);

	private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(10);

	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly TimeProvider time;

	private readonly ILogger<PowerService> logger;

	private readonly Func<long, bool> powerOff;

	private readonly object gate = new();

	private ControllerSession? watched;

	private PowerPolicy policy = new();

	private DateTimeOffset lastEvaluation = DateTimeOffset.MinValue;

	private DateTimeOffset lastIdleCheck = DateTimeOffset.MinValue;

	public PowerService(IControllerService controller, ISettingsService settings, TimeProvider time, ILogger<PowerService> logger, IBluetoothRadio radio)
		: this(controller, settings, time, logger, radio.Disconnect)
	{
	}

	internal PowerService(IControllerService controller, ISettingsService settings, TimeProvider time, ILogger<PowerService> logger, Func<long, bool> powerOff)
	{
		this.controller = controller;
		this.settings = settings;
		this.time = time;
		this.logger = logger;
		this.powerOff = powerOff;
		controller.SessionStarted += Watch;
		controller.SessionEnded += Unwatch;
		if (controller.Session is { } session)
		{
			Watch(session);
		}
	}

	public bool PowerOffNow(string reason)
	{
		ControllerSession? session = controller.Session;
		if (session == null || session.Transport != Transport.Bluetooth)
		{
			return false;
		}
		return PowerOff(session, reason);
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		if (settings.Current.EffectivePower.PowerOffOnExit)
		{
			PowerOffNow("app exit");
		}
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
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
			policy = new PowerPolicy();
			lastEvaluation = DateTimeOffset.MinValue;
			lastIdleCheck = time.GetUtcNow();
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

	// Read thread.
	private void OnInput(InputSnapshot snapshot)
	{
		ControllerSession? session = watched;
		DateTimeOffset now = time.GetUtcNow();
		if (session == null || now - lastEvaluation < ShortcutInterval)
		{
			return;
		}
		lastEvaluation = now;
		PowerSettings power = settings.Current.EffectivePower;
		if (now - lastIdleCheck >= IdleInterval)
		{
			lastIdleCheck = now;
		}
		else
		{
			power = power with { IdleTimeoutMinutes = 0 };
		}
		PowerAction action = policy.Evaluate(snapshot, session.Battery, session.Activity, now, power);
		if (action != PowerAction.None)
		{
			string reason = action == PowerAction.ShortcutPowerOff ? "PS+Triangle" : "idle";
			ThreadPool.QueueUserWorkItem(_ => PowerOff(session, reason));
		}
	}

	private bool PowerOff(ControllerSession session, string reason)
	{
		long? mac = session.ReadBluetoothAddress();
		if (mac == null)
		{
			logger.LogWarning("Power off ({Reason}) skipped: Bluetooth address unavailable", reason);
			return false;
		}
		bool done = powerOff(mac.Value);
		logger.LogInformation("Power off ({Reason}) {Mac}: {Result}", reason, BluetoothPower.FormatMacAddress(mac.Value), done ? "disconnected" : "radio refused");
		return done;
	}
}
