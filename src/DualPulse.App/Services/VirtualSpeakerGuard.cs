using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DualPulse.Core.Audio;

namespace DualPulse.App.Services;

// Keeps the PC's sound on the PC while a virtual DualSense runs with game sound off. The virtual pad's speaker device
// has to exist (games send their haptics through it), but Windows may make it the default output, and the game's
// sound played there is dropped. Then this switches the default back to the last output the user had. With game
// sound on the controller, or without a running virtual DualSense, the default is left alone.
public sealed class VirtualSpeakerGuard : IHostedService, IDisposable
{
	private readonly IDefaultAudioOutput output;

	private readonly ISettingsService settings;

	private readonly IEmulationService emulation;

	private readonly ILogger<VirtualSpeakerGuard> logger;

	private readonly object gate = new();

	// The last default output that wasn't a DualSense.
	private AudioDevice? lastOwn;

	public VirtualSpeakerGuard(IDefaultAudioOutput output, ISettingsService settings, IEmulationService emulation, ILogger<VirtualSpeakerGuard> logger)
	{
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
				return;
			}
			if (lastOwn == null || !VirtualDualSenseActive.In(settings, emulation) || settings.Current.GameSoundOnController)
			{
				return;
			}
			back = lastOwn;
		}
		if (output.Set(back.Id))
		{
			logger.LogInformation("Windows made the virtual DualSense the sound output; switched back to {Device}", back.Name);
		}
		else
		{
			logger.LogWarning("Windows made the virtual DualSense the sound output and switching back to {Device} failed", back.Name);
		}
	}

	private static bool IsDualSense(AudioDevice device) => device.Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase);
}
