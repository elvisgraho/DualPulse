using Microsoft.Extensions.Hosting;
using DualPulse.Core.Devices;
using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

// Controller shortcuts (while the setting is on): hold PS and press the D-pad. Right/Left: next/previous profile
// (Default, then the named profiles); Up/Down: next/previous adaptive trigger preset (as Home sets it). The player
// LEDs briefly show which one (1-4), over Bluetooth a short tick is felt, and a pop-up names it. Runs on the read
// thread.
public sealed class ShortcutService : IHostedService, IDisposable
{
	private static readonly TriggerPreset[] Presets = [TriggerPreset.Off, TriggerPreset.Feedback, TriggerPreset.Weapon, TriggerPreset.Vibration];

	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly INotifier? notifier;

	private readonly Action<int>? showNumber;

	private readonly IRumbleService? rumble;

	private readonly object gate = new();

	private readonly ShortcutRecognizer recognizer = new();

	private ControllerSession? watched;

	// showNumber: shows 1-4 on the player LEDs for a moment.
	public ShortcutService(IControllerService controller, ISettingsService settings, INotifier? notifier = null, Action<int>? showNumber = null,
		IRumbleService? rumble = null)
	{
		this.controller = controller;
		this.settings = settings;
		this.notifier = notifier;
		this.showNumber = showNumber;
		this.rumble = rumble;
		controller.SessionStarted += Watch;
		controller.SessionEnded += Unwatch;
		if (controller.Session is { } session)
		{
			Watch(session);
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
			}
		}
	}

	private void OnInput(InputSnapshot input)
	{
		Shortcut? shortcut;
		lock (gate)
		{
			shortcut = recognizer.Next(input.Buttons);
		}
		if (shortcut is not { } pressed || !settings.Current.ControllerShortcuts)
		{
			return;
		}
		(int number, string title)? done = pressed switch
		{
			Shortcut.NextProfile => SwitchProfile(1),
			Shortcut.PreviousProfile => SwitchProfile(-1),
			Shortcut.NextTriggers => SwitchTriggers(1),
			_ => SwitchTriggers(-1)
		};
		if (done is not { } result)
		{
			return;
		}
		showNumber?.Invoke(result.number);
		if (controller.Session?.Transport == Transport.Bluetooth)
		{
			rumble?.Pulse();
		}
		notifier?.Show(result.title, "", TimeSpan.FromSeconds(2));
	}

	// Default and the named profiles; nothing to switch between without a named one.
	private (int, string)? SwitchProfile(int step)
	{
		AppSettings current = settings.Current;
		List<GameProfile?> choices = [null, .. current.EffectiveProfiles.Where(p => p.IsNamed)];
		if (choices.Count < 2)
		{
			return null;
		}
		int at = Math.Max(0, choices.FindIndex(p => p?.Exe == current.ChosenProfile));
		int next = (at + step + choices.Count) % choices.Count;
		GameProfile? chosen = choices[next];
		settings.Update(s => s with { ChosenProfile = chosen?.Exe });
		return (Math.Min(next + 1, 4), $"Profile: {chosen?.Name ?? "Default"}");
	}

	// Custom triggers (set on the Triggers page) count as before the first preset.
	private (int, string)? SwitchTriggers(int step)
	{
		int at = Array.IndexOf(Presets, settings.Current.TriggerPreset);
		int next = at < 0 ? (step > 0 ? 0 : Presets.Length - 1) : (at + step + Presets.Length) % Presets.Length;
		TriggerPreset preset = Presets[next];
		settings.Update(s => s with { TriggerPreset = preset, Triggers = TriggerSettings.FromPreset(preset) });
		return (next + 1, $"Triggers: {preset}");
	}
}
