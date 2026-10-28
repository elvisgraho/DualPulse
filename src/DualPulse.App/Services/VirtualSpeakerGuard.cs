using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DualPulse.Core.Audio;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

// Keeps the PC's sound on the PC when Windows moves the default output to a DualSense, switching back to the last
// output the user had:
// - While a virtual DualSense runs with game sound off: its speaker device has to exist (games send their haptics
//   through it), but game sound played there is dropped.
// - Right after the controller is plugged in by cable: Windows may make its sound device the default. Switched back
//   when the move happens within PlugInWindow of the cable connection (either may come first), once per plug-in.
// Otherwise choosing a DualSense as the output is the user's business.
public sealed class VirtualSpeakerGuard : IHostedService, IDisposable
{
	private readonly IDefaultAudioOutput output;

	private readonly ISettingsService settings;

	private readonly IEmulationService emulation;

	private static readonly TimeSpan PlugInWindow = TimeSpan.FromSeconds(10);

	private readonly ILogger<VirtualSpeakerGuard> logger;

	private readonly IControllerService? controller;

	private readonly TimeProvider time;

	private readonly object gate = new();

	// When the default last became a DualSense (null while it isn't one), and when the controller last connected by
	// cable (null once that plug-in has been handled).
	private DateTimeOffset? dualSenseSince;

	private DateTimeOffset? cableArrivedAt;

	// The last default output that wasn't a DualSense.
	private AudioDevice? lastOwn;

	public VirtualSpeakerGuard(IDefaultAudioOutput output, ISettingsService settings, IEmulationService emulation, ILogger<VirtualSpeakerGuard> logger,
		IControllerService? controller = null, TimeProvider? time = null)
	{
		this.controller = controller;
		this.time = time ?? TimeProvider.System;
		if (controller != null)
		{
			controller.SessionStarted += OnSessionStarted;
		}
		this.output = output;
		this.settings = settings;
		this.emulation = emulation;
		this.logger = logger;
		output.Changed += Check;
		settings.Changed += OnSettingsChanged;
		// Raised under the emulation service's lock: check elsewhere (switching the output takes a moment).
		emulation.StatusChanged += OnEmulationChanged;
		Check();
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		output.Changed -= Check;
		settings.Changed -= OnSettingsChanged;
		emulation.StatusChanged -= OnEmulationChanged;
		if (controller != null)
		{
			controller.SessionStarted -= OnSessionStarted;
		}
	}

	private void OnSessionStarted(ControllerSession session)
	{
		if (session.Transport != Transport.Usb)
		{
			return;
		}
		lock (gate)
		{
			cableArrivedAt = time.GetUtcNow();
		}
		Task.Run(Check);
	}

	private void OnSettingsChanged(AppSettings current) => Check();

	private void OnEmulationChanged() => Task.Run(Check);

	private void Check()
	{
		AudioDevice? back;
		lock (gate)
		{
			AudioDevice? current = output.Current;
			if (current == null)
			{
				return;
			}
			if (!IsDualSense(current))
			{
				lastOwn = current;
				dualSenseSince = null;
				return;
			}
			DateTimeOffset now = time.GetUtcNow();
			dualSenseSince ??= now;
			bool virtualPadTookIt = VirtualDualSenseActive.In(settings, emulation) && !settings.Current.GameSoundOnController;
			bool pluggedIn = cableArrivedAt is { } arrived && now - arrived <= PlugInWindow
				&& (dualSenseSince.Value - arrived).Duration() <= PlugInWindow;
			if (lastOwn == null || !(virtualPadTookIt || pluggedIn))
			{
				return;
			}
			if (pluggedIn)
			{
				cableArrivedAt = null;
			}
			back = lastOwn;
		}
		if (output.Set(back.Id))
		{
			logger.LogInformation("Windows made the DualSense the sound output; switched back to {Device}", back.Name);
		}
		else
		{
			logger.LogWarning("Windows made the DualSense the sound output and switching back to {Device} failed", back.Name);
		}
	}

	private static bool IsDualSense(AudioDevice device) => device.Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase);
}
