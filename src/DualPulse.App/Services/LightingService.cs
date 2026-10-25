using Microsoft.Extensions.Hosting;
using DualPulse.Core.Devices;
using DualPulse.Core.Features;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

// Keeps the controller's lightbar, player LEDs and mic LED in line with the settings. Static lighting is written once
// per change; breathing and rainbow re-evaluate about 30 times a second and battery mode once a second, and a write
// only happens when the colour actually changed. No timer runs while no controller is bound.
public sealed class LightingService : IHostedService, IDisposable
{
	private static readonly TimeSpan AnimationInterval = TimeSpan.FromMilliseconds(33);

	private static readonly TimeSpan BatteryInterval = TimeSpan.FromSeconds(1);

	// Player-LED animations (battery charging blink, low-battery flash) change at most twice a second.
	private static readonly TimeSpan PlayerLedInterval = TimeSpan.FromMilliseconds(500);

	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly TimeProvider time;

	private readonly long startTimestamp;

	private readonly ModOverride? mods;

	private readonly IMicMuteService? micMute;

	private readonly object gate = new();

	private ITimer? timer;

	public LightingService(IControllerService controller, ISettingsService settings, TimeProvider time, ModOverride? mods = null,
		IMicMuteService? micMute = null)
	{
		this.mods = mods;
		this.micMute = micMute;
		if (micMute != null)
		{
			micMute.Changed += Reschedule;
		}
		this.controller = controller;
		this.settings = settings;
		this.time = time;
		startTimestamp = time.GetTimestamp();
		controller.SessionStarted += OnSessionStarted;
		controller.SessionEnded += OnSessionEnded;
		settings.Changed += OnSettingsChanged;
		if (mods != null)
		{
			mods.Released += Reschedule;
		}
		if (controller.Session is { } session)
		{
			OnSessionStarted(session);
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
		controller.SessionStarted -= OnSessionStarted;
		controller.SessionEnded -= OnSessionEnded;
		settings.Changed -= OnSettingsChanged;
		if (mods != null)
		{
			mods.Released -= Reschedule;
		}
		if (micMute != null)
		{
			micMute.Changed -= Reschedule;
		}
		lock (gate)
		{
			timer?.Dispose();
			timer = null;
		}
	}

	private void OnSessionStarted(ControllerSession session)
	{
		Reschedule();
	}

	private void OnSessionEnded(ControllerSession session)
	{
		lock (gate)
		{
			timer?.Dispose();
			timer = null;
		}
	}

	private void OnSettingsChanged(AppSettings current)
	{
		Reschedule();
	}

	private void Reschedule()
	{
		lock (gate)
		{
			timer?.Dispose();
			timer = null;
			if (controller.Session == null)
			{
				return;
			}
			Apply();
			LightingSettings lighting = settings.Current.EffectiveLighting;
			TimeSpan? interval = lighting.Mode switch
			{
				LightbarMode.Breathing or LightbarMode.Rainbow => AnimationInterval,
				LightbarMode.Battery => BatteryInterval,
				_ => null
			};
			if (lighting.PlayerLeds == PlayerLedPattern.Battery || lighting.LowBatteryFlash)
			{
				interval = interval is { } current && current < PlayerLedInterval ? current : PlayerLedInterval;
			}
			if (interval is { } period)
			{
				timer = time.CreateTimer(_ => Tick(), null, period, period);
			}
		}
	}

	private void Tick()
	{
		lock (gate)
		{
			if (timer != null)
			{
				Apply();
			}
		}
	}

	// Caller holds gate.
	private void Apply()
	{
		if (controller.Session is not { } session)
		{
			return;
		}
		AppSettings current = settings.Current;
		LightingSettings lighting = current.EffectiveLighting;
		TimeSpan elapsed = time.GetElapsedTime(startTimestamp);
		Rgb lightbar = LightingEngine.Lightbar(lighting.Mode, current.EffectiveLightbar, lighting.Brightness, elapsed, session.Battery.Level);
		// Until the first report the battery reads as empty; only a real reading may flash the LEDs.
		bool lowFlash = lighting.LowBatteryFlash && session.Latest != null;
		byte playerLeds = LightingEngine.PlayerLedBits(lighting.PlayerLeds, session.Battery, lowFlash, elapsed);
		OutputState output = session.Output;
		// Parts a game mod currently controls are left as they are.
		if (mods != null)
		{
			lightbar = mods.ControlsLightbar ? output.Lightbar : lightbar;
			playerLeds = mods.ControlsPlayerLeds ? output.PlayerLeds : playerLeds;
		}
		LedBrightness ledBrightness = lighting.PlayerLedBrightness;
		// With the Mute button driving the microphone, the mute LED shows whether it is muted.
		MicLed micLed = micMute != null && lighting.MuteButtonMutesMic ? (micMute.IsMicMuted == true ? MicLed.On : MicLed.Off) : lighting.MicLed;
		micLed = mods?.ControlsMicLed == true ? output.MicLed : micLed;
		if (output.Lightbar == lightbar && output.PlayerLeds == playerLeds && output.PlayerLedBrightness == ledBrightness && output.MicLed == micLed)
		{
			return;
		}
		session.Apply(state => state with { Lightbar = lightbar, PlayerLeds = playerLeds, PlayerLedBrightness = ledBrightness, MicLed = micLed });
	}
}
