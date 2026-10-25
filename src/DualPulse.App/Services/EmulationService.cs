using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DualPulse.Core.Audio;
using DualPulse.Core.Devices;
using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using DualPulse.Emulation;

namespace DualPulse.App.Services;

public enum EmulationStatus
{
	Off,
	Active,
	DriverMissing
}

public interface IEmulationService
{
	EmulationStatus Status { get; }

	event Action? StatusChanged;
}

// One virtual pad of the kind chosen in the settings, fed by whichever controller session is current. Input is
// forwarded on the read thread; game rumble goes through the rumble service (Large = left grip motor). Without the
// pad's driver the controller keeps working.
// - The pad outlives connection changes (USB <-> Bluetooth): between sessions it is parked, fed neutral input so nothing
//   stays held, and unplugged only if no controller is back within PadGrace.
// - Pads are plugged in and out on a worker (usbip attach/detach can take seconds), never under the lock or on the
//   caller's thread; a result is taken only if it is still wanted (generation), otherwise unplugged again.
// - A lost pad (Windows unplugged it) is recreated only after the emulation choice changes or a controller connects.
// - Game audio over Bluetooth goes into the session's haptics and speaker streams; over USB into the controller's own
//   sound device (UsbAudioRoute), as a game would send it natively. A DualSense that has been on Bluetooth ignores that
//   until it is switched off and on, so plugging it in from Bluetooth in DualSense mode shows a notice saying so.
// Every event updates the wanted state under the lock and calls Reconcile, which brings the pad in line with it.
public sealed class EmulationService : IEmulationService, IHostedService, IDisposable
{
	private readonly IControllerService controller;

	private readonly IVirtualPadBackend backend;

	private readonly ISettingsService settings;

	private readonly IRumbleService rumble;

	private readonly IPcControlService? pcControl;

	private readonly IGyroService? gyro;

	private readonly IProfileService? profiles;

	private readonly ILogger<EmulationService> logger;

	private readonly TimeProvider time;

	private readonly IControllerUsbAudio? usbAudio;

	private readonly INotifier? notifier;

	// A USB session this soon after a Bluetooth one is the same controller plugged in (USB opens, then Bluetooth goes);
	// after a power-cycle the gap is seconds.
	private static readonly TimeSpan SwitchWindow = TimeSpan.FromSeconds(3);

	private long? bluetoothEnded;

	// Long enough to read in the middle of a game.
	private static readonly TimeSpan PowerCycleNoticeDuration = TimeSpan.FromSeconds(20);

	private readonly object gate = new();

	private static readonly TimeSpan PadGrace = TimeSpan.FromSeconds(10);

	// While the chosen mode's driver is missing, how often it is looked for again (installing it needs no restart).
	public static readonly TimeSpan DriverRecheck = TimeSpan.FromSeconds(5);

	private ITimer? driverTimer;

	// Game audio from a virtual DualSense arrives at 48 kHz.
	private const int GameAudioRate = 48000;

	private const double GameHapticsCutoffHz = 1000;

	// Soft-limited gain for game haptics at 100 % rumble strength.
	private const double GameHapticsBoost = 3;

	private readonly PadQueue? queue;

	private readonly Action<Action> runPadWork;

	// Pad work collected under the lock, run after it (on the worker).
	private readonly List<Action> padWork = [];

	// Wanted state and what exists, all under gate.
	private ControllerSession? session;

	private PadKind? kind;

	private Lease? lease;

	private Hook? hook;

	private PadKind? creating;

	private int generation;

	private bool retryBlocked;

	private ITimer? parkTimer;

	private object? parkToken;

	private bool disposed;

	private bool unavailableLogged;

	// Deadzone and start point for L2 and R2, read on every report; replaced as a whole when settings change.
	private volatile TriggerRanges ranges = TriggerRanges.None;

	// padWorker: runs pad creation and disposal (tests pass an inline runner); by default a dedicated thread.
	public EmulationService(IControllerService controller, IVirtualPadBackend backend, ISettingsService settings, IRumbleService rumble, ILogger<EmulationService> logger,
		IPcControlService? pcControl = null, IGyroService? gyro = null, IProfileService? profiles = null, TimeProvider? time = null,
		Action<Action>? padWorker = null, IControllerUsbAudio? usbAudio = null, INotifier? notifier = null)
	{
		this.notifier = notifier;
		this.usbAudio = usbAudio;
		this.time = time ?? TimeProvider.System;
		this.profiles = profiles;
		this.rumble = rumble;
		this.pcControl = pcControl;
		this.gyro = gyro;
		this.controller = controller;
		this.backend = backend;
		this.settings = settings;
		this.logger = logger;
		if (padWorker == null)
		{
			queue = new PadQueue(logger);
			runPadWork = queue.Post;
		}
		else
		{
			runPadWork = padWorker;
		}
		kind = settings.Current.Emulation;
		controller.SessionStarted += OnSessionStarted;
		controller.SessionEnded += OnSessionEnded;
		// With game profiles, the effective settings (saved + active profile) drive the trigger range.
		if (profiles != null)
		{
			profiles.Changed += OnProfilesChanged;
		}
		else
		{
			settings.Changed += OnSettingsChanged;
		}
		ranges = TriggerRanges.From(profiles?.Effective ?? settings.Current);
		if (controller.Session is { } current)
		{
			OnSessionStarted(current);
		}
	}

	public EmulationStatus Status { get; private set; }

	public event Action? StatusChanged;

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
		if (profiles != null)
		{
			profiles.Changed -= OnProfilesChanged;
		}
		Update(() =>
		{
			disposed = true;
			DropLease();
			generation++;
			driverTimer?.Dispose();
			driverTimer = null;
		});
		// Unplugging the pad is worth a short wait at shutdown; a hung usbip doesn't hold the app.
		queue?.Dispose();
	}

	private void OnSessionStarted(ControllerSession started)
	{
		bool fromBluetooth = false;
		Update(() =>
		{
			fromBluetooth = started.Transport == Transport.Usb && kind == PadKind.DualSense
				&& bluetoothEnded is { } ended && time.GetElapsedTime(ended) <= SwitchWindow;
			bluetoothEnded = null;
			// A session can end between being announced and reaching here; never bind a pad to a dead one.
			session = started.IsReading ? started : null;
			retryBlocked = false;
			if (kind == null && session != null)
			{
				logger.LogInformation("Emulation off (native mode)");
			}
		});
		if (fromBluetooth)
		{
			logger.LogInformation("Controller plugged in from Bluetooth: cable haptics need it switched off and on once");
			notifier?.Show("Vibration stopped", "To get it back while the controller is plugged in, turn it off and on again.", PowerCycleNoticeDuration);
		}
	}

	private void OnSessionEnded(ControllerSession ended)
	{
		Update(() =>
		{
			if (session == ended)
			{
				session = null;
			}
			if (ended.Transport == Transport.Bluetooth)
			{
				bluetoothEnded = time.GetTimestamp();
			}
		});
	}

	private void OnProfilesChanged() => OnSettingsChanged(profiles!.Effective);

	private void OnSettingsChanged(AppSettings current)
	{
		ranges = TriggerRanges.From(current);
		Update(() =>
		{
			if (kind != current.Emulation)
			{
				kind = current.Emulation;
				retryBlocked = false;
			}
		});
	}

	// Changes the wanted state under the lock, reconciles, then hands the collected pad work to the worker.
	private void Update(Action change)
	{
		List<Action> work;
		lock (gate)
		{
			change();
			Reconcile();
			work = [.. padWork];
			padWork.Clear();
		}
		foreach (Action item in work)
		{
			runPadWork(item);
		}
	}

	// Caller holds gate.
	private void Reconcile()
	{
		if (lease != null && (lease.Kind != kind || disposed))
		{
			DropLease();
		}
		if (disposed)
		{
			return;
		}
		if (kind is not { } wanted)
		{
			SetStatus(EmulationStatus.Off);
			return;
		}
		if (lease == null)
		{
			if (session == null)
			{
				SetStatus(EmulationStatus.Off);
			}
			else if (retryBlocked)
			{
				SetStatus(EmulationStatus.DriverMissing);
			}
			else if (!backend.IsAvailableFor(wanted))
			{
				// Once per spell: every settings change (a slider drag) reconciles.
				if (!unavailableLogged)
				{
					unavailableLogged = true;
					logger.LogWarning("Emulation unavailable: {Reason}", backend.UnavailableReason);
				}
				SetStatus(EmulationStatus.DriverMissing);
				if (driverTimer == null && !disposed)
				{
					driverTimer = time.CreateTimer(_ => Update(() => ClearDriverTimer()), null, DriverRecheck, Timeout.InfiniteTimeSpan);
				}
			}
			else
			{
				unavailableLogged = false;
				// Not active until the pad exists (the audio features stand down only for a running virtual DualSense).
				SetStatus(EmulationStatus.Off);
				if (creating != wanted)
				{
					creating = wanted;
					int ticket = ++generation;
					padWork.Add(() => CreatePad(wanted, ticket));
				}
			}
			return;
		}
		if (session != null)
		{
			CancelPark();
			if (hook?.Session != session)
			{
				Unhook();
				hook = HookUp(session, lease);
			}
		}
		else
		{
			Unhook();
			StartPark();
		}
		SetStatus(EmulationStatus.Active);
	}

	// On the worker. A create no longer wanted by the time it starts is skipped; one that turns out unwanted when it
	// finishes is unplugged again right here (also at shutdown, when nothing more is queued).
	private void CreatePad(PadKind padKind, int ticket)
	{
		lock (gate)
		{
			if (ticket != generation)
			{
				return;
			}
		}
		IVirtualPad? pad = null;
		string? error = null;
		try
		{
			pad = backend.Create(padKind);
		}
		catch (Exception exception)
		{
			error = exception.Message;
		}
		bool taken = false;
		Update(() =>
		{
			if (ticket != generation)
			{
				return;
			}
			creating = null;
			if (pad == null)
			{
				logger.LogWarning("Could not create a {Kind} pad: {Error}", padKind, error);
				retryBlocked = true;
				return;
			}
			taken = true;
			Lease created = new(pad, padKind, new GameOutput());
			if (pad is IDualSensePad dualSense)
			{
				created.Lost = () => OnPadLost(created);
				dualSense.Lost += created.Lost;
				// The game's output is merged for the pad's whole life, also while it is parked between connections.
				created.Output = created.OnGameOutput;
				dualSense.OutputBlock += created.Output;
				if (dualSense.IsLost)
				{
					// Lost before anyone listened.
					logger.LogWarning("The virtual DualSense was lost right after it was plugged in");
					lease = created;
					DropLease();
					retryBlocked = true;
					return;
				}
			}
			lease = created;
			logger.LogInformation("Emulating {Kind}", padKind);
		});
		if (!taken)
		{
			pad?.Dispose();
		}
	}

	// The virtual DualSense went away (Windows unplugged it), bound or parked: DualPulse's own settings drive the
	// controller again, and the status says so.
	private void OnPadLost(Lease lost)
	{
		Update(() =>
		{
			if (lease != lost)
			{
				return;
			}
			logger.LogWarning("The virtual DualSense was lost (unplugged by the host)");
			DropLease();
			retryBlocked = true;
		});
	}

	// Caller holds gate.
	private void DropLease()
	{
		if (lease is not { } dropped)
		{
			return;
		}
		Unhook();
		CancelPark();
		lease = null;
		if (dropped.Pad is IDualSensePad dualSense)
		{
			if (dropped.Lost != null)
			{
				dualSense.Lost -= dropped.Lost;
			}
			if (dropped.Output != null)
			{
				dualSense.OutputBlock -= dropped.Output;
			}
		}
		padWork.Add(dropped.Pad.Dispose);
	}

	// Caller holds gate.
	private void Unhook()
	{
		if (hook is { } current)
		{
			hook = null;
			current.Unhook();
		}
	}

	// Caller holds gate. The next reconcile starts another check if the driver is still missing.
	private void ClearDriverTimer()
	{
		driverTimer?.Dispose();
		driverTimer = null;
	}

	// Caller holds gate.
	private void StartPark()
	{
		if (parkTimer != null)
		{
			return;
		}
		object token = new();
		parkToken = token;
		parkTimer = time.CreateTimer(_ => OnParkExpired(token), null, PadGrace, Timeout.InfiniteTimeSpan);
	}

	// Caller holds gate.
	private void CancelPark()
	{
		parkTimer?.Dispose();
		parkTimer = null;
		parkToken = null;
	}

	private void OnParkExpired(object token)
	{
		Update(() =>
		{
			if (parkToken != token)
			{
				return;
			}
			logger.LogInformation("No controller for {Seconds:0} s: unplugging the virtual pad", PadGrace.TotalSeconds);
			DropLease();
		});
	}

	// Caller holds gate. StatusChanged handlers must not block (the Home view model only posts to the UI thread).
	private void SetStatus(EmulationStatus status)
	{
		if (Status == status)
		{
			return;
		}
		Status = status;
		StatusChanged?.Invoke();
	}

	private static readonly InputSnapshot Neutral = new() { LeftX = 128, LeftY = 128, RightX = 128, RightY = 128 };

	private readonly record struct PadInput(byte LeftX, byte LeftY, byte RightX, byte RightY, byte L2, byte R2, Buttons Buttons);

	// Caller holds gate. Feeds the lease's pad from this session; the returned hook undoes it, leaving the pad neutral.
	private Hook HookUp(ControllerSession bound, Lease leased)
	{
		IVirtualPad pad = leased.Pad;
		// Only what a virtual pad carries is compared: motion sensors and the timestamp change in every report, and a
		// ViGEm submit per report costs more than the rest of the read path.
		// While PC control is on, the pad gets one neutral report (releasing anything a game saw held), then nothing.
		PadInput? lastSubmitted = null;
		InputSnapshot last = Neutral;
		HookState state = new();
		Action<InputSnapshot> forward = snapshot =>
		{
			if (pcControl?.IsActive == true)
			{
				snapshot = Neutral;
			}
			else if (gyro != null)
			{
				snapshot = gyro.ApplyToPad(snapshot);
			}
			TriggerRanges range = ranges;
			if (range != TriggerRanges.None)
			{
				byte l2 = TriggerRange.Apply(snapshot.L2, range.LeftDeadzone, range.LeftStartAt);
				byte r2 = TriggerRange.Apply(snapshot.R2, range.RightDeadzone, range.RightStartAt);
				// The digital trigger buttons (a DS4 pad sends them) follow the shaped value, so a pull inside the
				// deadzone doesn't still count as pressed.
				Buttons buttons = snapshot.Buttons & ~(Buttons.L2 | Buttons.R2);
				buttons |= (l2 > 0 ? Buttons.L2 : Buttons.None) | (r2 > 0 ? Buttons.R2 : Buttons.None);
				snapshot = snapshot with { L2 = l2, R2 = r2, Buttons = buttons };
			}
			// A virtual DualSense carries motion, touch and battery too, so every report goes out.
			PadInput current = new(snapshot.LeftX, snapshot.LeftY, snapshot.RightX, snapshot.RightY, snapshot.L2, snapshot.R2, snapshot.Buttons);
			lock (state.Gate)
			{
				if (!state.Active)
				{
					return;
				}
				last = snapshot;
				if (pad is IDualSensePad)
				{
					pad.Submit(snapshot);
				}
				else if (current != lastSubmitted)
				{
					lastSubmitted = current;
					pad.Submit(snapshot);
				}
			}
		};
		Action<RumbleFeedback> gameRumble = feedback => rumble.SetGameRumble(feedback.Large, feedback.Small);
		bound.InputReceived += forward;
		pad.Feedback += gameRumble;
		Action? unhookGame = pad is IDualSensePad dualSense ? HookGame(bound, dualSense, leased, state) : null;
		return new Hook(bound, () =>
		{
			bound.InputReceived -= forward;
			pad.Feedback -= gameRumble;
			// Under the hook's lock: a handler that already passed its Active check finishes first, so nothing it does
			// lands after this undo.
			lock (state.Gate)
			{
				state.Active = false;
				unhookGame?.Invoke();
				// Nothing stays held while no controller feeds the pad (a stick mid-push, PS held to switch off).
				pad.Submit(last with { LeftX = 128, LeftY = 128, RightX = 128, RightY = 128, L2 = 0, R2 = 0, Buttons = Buttons.None, Touch0 = default, Touch1 = default });
			}
		});
	}

	// A game driving a virtual DualSense: its merged output (GameOutput) replaces DualPulse's composed block, its motor
	// rumble goes through the rumble service (strength, mute), its haptics into the haptics mixer and, if wanted, its
	// speaker audio into the speaker stream. The game's state is applied to this session at once (games only send
	// changes). Returns what undoes it; afterwards DualPulse's own settings drive the controller again.
	// Caller holds gate; the returned undo runs under the hook's lock.
	private Action HookGame(ControllerSession bound, IDualSensePad pad, Lease leased, HookState state)
	{
		GameOutput game = leased.Game;
		GameOutputMonitor monitor = new(logger, TimeProvider.System, bound.Speaker);
		// Game haptics: a wider band than music gets, boosted through a soft limit (games' haptics often peak around 15 %
		// of full scale, barely felt 1:1 over Bluetooth), scaled by the rumble strength and silenced by its mute.
		HapticsAudioConverter haptics = new(GameAudioRate, 2, GameHapticsCutoffHz) { SoftLimit = true };
		float[] hapticBuffer = [];
		float[] speakerBuffer = [];
		UsbAudioRoute? usbRoute = bound.Transport == Transport.Usb && usbAudio != null ? new UsbAudioRoute(usbAudio, time, logger) : null;
		if (game.Carried is { } carried)
		{
			bound.Apply(output => output with { GameBlock = carried });
		}
		if (game.Motors is { } motors)
		{
			rumble.SetGameRumble(motors.Large, motors.Small);
		}
		// The lease merges each block (GameOutput) and hands it here: send is null when nothing changed.
		Action<byte[], byte[]?> output = (block, send) =>
		{
			monitor.Output(block);
			if (send == null)
			{
				return;
			}
			lock (state.Gate)
			{
				if (!state.Active)
				{
					return;
				}
				bound.Apply(current => current with { GameBlock = send });
				if ((block[0] & 0x03) != 0 && game.Motors is { } rumbleNow)
				{
					rumble.SetGameRumble(rumbleNow.Large, rumbleNow.Small);
				}
			}
		};
		GameAudio hapticAudio = (samples, channels) =>
		{
			monitor.Haptics(samples);
			// Over USB the controller plays the whole 4-channel stream itself (speakerAudio).
			if (!state.Active || usbRoute != null)
			{
				return;
			}
			RumbleSettings rumbleSettings = settings.Current.EffectiveRumble;
			haptics.Intensity = rumbleSettings.Muted ? 0 : GameHapticsBoost * Math.Clamp(rumbleSettings.Strength, 0, 200) / 100.0;
			haptics.Process(ToFloat(samples, ref hapticBuffer), bound.HapticMixer);
			bound.WakeHaptics();
		};
		GameAudio speakerAudio = (samples, channels) =>
		{
			monitor.Speaker(samples);
			if (!state.Active)
			{
				return;
			}
			if (usbRoute != null)
			{
				// Natively the controller plays the game's haptics as sent: no boost, only the rumble strength and mute.
				RumbleSettings rumbleSettings = settings.Current.EffectiveRumble;
				usbRoute.Write(samples, channels, settings.Current.GameSoundOnController,
					rumbleSettings.Muted ? 0 : Math.Clamp(rumbleSettings.Strength, 0, 200) / 100.0);
				return;
			}
			// Off by default: few want game sound from the controller, and without it the haptics keep report 0x32.
			if (!settings.Current.GameSoundOnController)
			{
				return;
			}
			bound.Speaker.Push(ToFloat(samples, ref speakerBuffer), GameAudioRate, channels);
			bound.WakeAudio();
		};
		leased.Forward = output;
		pad.Haptics += hapticAudio;
		pad.Speaker += speakerAudio;
		return () =>
		{
			leased.Forward = null;
			pad.Haptics -= hapticAudio;
			pad.Speaker -= speakerAudio;
			bound.Apply(current => current with { GameBlock = null });
			rumble.SetGameRumble(0, 0);
			usbRoute?.Close();
		};
	}

	// The game's 4-channel audio into the real controller's own sound device (USB): channels 1-2 speaker (only when
	// wanted, silent otherwise), 3-4 haptics scaled by the given gain. The device is opened on the first audio (off the
	// service lock); when that fails it is tried again at most once a second. Closed for good by Close.
	private sealed class UsbAudioRoute(IControllerUsbAudio audio, TimeProvider time, ILogger logger)
	{
		private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

		private readonly object gate = new();

		private IControllerAudioStream? stream;

		private long? lastAttempt;

		private bool closed;

		private short[] buffer = [];

		private bool missingLogged;

		private long written;

		public void Write(ReadOnlySpan<short> samples, int channels, bool sound, double hapticsGain)
		{
			if (channels != 4)
			{
				return;
			}
			lock (gate)
			{
				if (closed)
				{
					return;
				}
				if (stream == null)
				{
					if (lastAttempt is { } attempt && time.GetElapsedTime(attempt) < RetryInterval)
					{
						return;
					}
					lastAttempt = time.GetTimestamp();
					try
					{
						stream = audio.Open();
					}
					catch (Exception error)
					{
						logger.LogWarning("Opening the controller's own sound device failed: {Error}", error.Message);
					}
					if (stream == null)
					{
						if (!missingLogged)
						{
							missingLogged = true;
							logger.LogInformation("Controller on USB: its sound device isn't there yet, retrying every second");
						}
						return;
					}
					logger.LogInformation("Controller on USB: game haptics now play on its own sound device");
				}
				if (buffer.Length < samples.Length)
				{
					buffer = new short[samples.Length];
				}
				for (int i = 0; i + 3 < samples.Length; i += 4)
				{
					buffer[i] = sound ? samples[i] : (short)0;
					buffer[i + 1] = sound ? samples[i + 1] : (short)0;
					buffer[i + 2] = (short)Math.Clamp(Math.Round(samples[i + 2] * hapticsGain), short.MinValue, short.MaxValue);
					buffer[i + 3] = (short)Math.Clamp(Math.Round(samples[i + 3] * hapticsGain), short.MinValue, short.MaxValue);
				}
				try
				{
					stream.Write(buffer.AsSpan(0, samples.Length & ~3), 4);
					written += samples.Length / 4;
				}
				catch (Exception error)
				{
					// The device went away (unplugged): drop it; the next audio tries to open it again.
					logger.LogWarning("Writing to the controller's own sound device failed: {Error}", error.Message);
					stream.Dispose();
					stream = null;
				}
			}
		}

		public void Close()
		{
			lock (gate)
			{
				closed = true;
				if (stream != null)
				{
					logger.LogInformation("Controller's own sound device closed after {Seconds:0.0} s of game audio", written / 48000.0);
					stream.Dispose();
					stream = null;
				}
			}
		}
	}

	// The pad's streams each raise on their own thread, so each handler has its own buffer.
	private static ReadOnlySpan<float> ToFloat(ReadOnlySpan<short> samples, ref float[] buffer)
	{
		if (buffer.Length < samples.Length)
		{
			buffer = new float[samples.Length];
		}
		for (int i = 0; i < samples.Length; i++)
		{
			buffer[i] = samples[i] / 32768f;
		}
		return buffer.AsSpan(0, samples.Length);
	}

	// A plugged-in pad and the game's state on it; lives across sessions until unplugged.
	private sealed class Lease(IVirtualPad pad, PadKind kind, GameOutput game)
	{
		public IVirtualPad Pad { get; } = pad;

		public PadKind Kind { get; } = kind;

		public GameOutput Game { get; } = game;

		public Action? Lost { get; set; }

		// Subscribed to the pad's OutputBlock for the lease's whole life.
		public Action<byte[]>? Output { get; set; }

		// The bound session's handler for merged game output, if any.
		public volatile Action<byte[], byte[]?>? Forward;

		// On the pad's thread: merge first (also while parked), then hand to the bound session.
		public void OnGameOutput(byte[] block)
		{
			byte[]? send = Game.Accept(block);
			Forward?.Invoke(block, send);
		}
	}

	// The lease bound to one session.
	private sealed record Hook(ControllerSession Session, Action Unhook);

	// Handlers can still be running on other threads when unhooked: they check Active (under Gate when they change the
	// controller's state), and the undo takes Gate, so it runs after them.
	private sealed class HookState
	{
		public readonly object Gate = new();

		public volatile bool Active = true;
	}

	// One thread that plugs pads in and out, in order. Disposing waits a little for what is queued (unplugging).
	private sealed class PadQueue : IDisposable
	{
		private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

		private readonly System.Collections.Concurrent.BlockingCollection<Action> work = [];

		private readonly Thread thread;

		public PadQueue(ILogger logger)
		{
			thread = new Thread(() =>
			{
				foreach (Action item in work.GetConsumingEnumerable())
				{
					try
					{
						item();
					}
					catch (Exception exception)
					{
						logger.LogWarning("Virtual pad work failed: {Error}", exception.Message);
					}
				}
			}) { Name = "Virtual pads", IsBackground = true };
			thread.Start();
		}

		public void Post(Action item)
		{
			try
			{
				work.Add(item);
			}
			catch (InvalidOperationException)
			{
				// Shut down: nothing more runs.
			}
		}

		public void Dispose()
		{
			work.CompleteAdding();
			thread.Join(DrainTimeout);
		}
	}
}

internal sealed record TriggerRanges(double LeftDeadzone, double LeftStartAt, double RightDeadzone, double RightStartAt)
{
	public static readonly TriggerRanges None = new(0, 0, 0, 0);

	public static TriggerRanges From(AppSettings current)
	{
		TriggerSettings triggers = current.EffectiveTriggers;
		TriggerRanges ranges = new(triggers.Left.Deadzone, triggers.Left.StartAt, triggers.EffectiveRight.Deadzone, triggers.EffectiveRight.StartAt);
		return ranges == None ? None : ranges;
	}
}
