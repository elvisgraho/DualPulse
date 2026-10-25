using Microsoft.Extensions.Hosting;
using DualPulse.Core.Devices;
using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

public enum GyroCalibrationState
{
	Idle,
	Calibrating,
	Done
}

public interface IGyroService
{
	// The input as the virtual pad should see it: in stick mode, with the gyro added to the right stick while active.
	InputSnapshot ApplyToPad(InputSnapshot input);

	GyroCalibrationState CalibrationState { get; }

	// Raised on the read thread when calibration starts or finishes.
	event Action? CalibrationChanged;

	// Put the controller down still, then call this; the offset is saved after 1.5 s.
	void StartCalibration();
}

// Gyro aiming, in games: added to the virtual pad's right stick (stick mode) or sent as mouse movement (mouse mode),
// while the activation button is held. Mouse mode pauses while PC control is on. Calibration averages the gyro for
// 1.5 s while the controller lies still and saves the result as the resting offset. Runs on the read thread.
public sealed class GyroService : IGyroService, IHostedService, IDisposable
{
	private static readonly TimeSpan CalibrationTime = TimeSpan.FromSeconds(1.5);

	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly IPcOutput output;

	private readonly TimeProvider time;

	private readonly IPcControlService? pcControl;

	private readonly IProfileService? profiles;

	private readonly long start;

	private readonly object gate = new();

	private readonly GyroAim aim = new();

	private GyroSettings options = new();

	private ControllerSession? watched;

	private bool wasActive;

	private GyroCalibration? calibration;

	private TimeSpan calibrationStart;

	public GyroService(IControllerService controller, ISettingsService settings, IPcOutput output, TimeProvider time, IPcControlService? pcControl = null,
		IProfileService? profiles = null)
	{
		this.controller = controller;
		this.settings = settings;
		this.output = output;
		this.time = time;
		this.pcControl = pcControl;
		this.profiles = profiles;
		start = time.GetTimestamp();
		ApplySettings(profiles?.Effective ?? settings.Current);
		// With game profiles, the effective settings (saved + active profile) drive the gyro.
		if (profiles != null)
		{
			profiles.Changed += OnProfilesChanged;
		}
		else
		{
			settings.Changed += OnSettingsChanged;
		}
		controller.SessionStarted += Watch;
		controller.SessionEnded += Unwatch;
		if (controller.Session is { } session)
		{
			Watch(session);
		}
	}

	public GyroCalibrationState CalibrationState { get; private set; }

	public event Action? CalibrationChanged;

	public InputSnapshot ApplyToPad(InputSnapshot input)
	{
		lock (gate)
		{
			if (!options.Enabled || options.Output != GyroOutput.Stick || !IsActive(input.Buttons))
			{
				return input;
			}
			(byte x, byte y) = aim.Stick(input);
			return input with { RightX = x, RightY = y };
		}
	}

	public void StartCalibration()
	{
		lock (gate)
		{
			calibration = new GyroCalibration();
			calibrationStart = time.GetElapsedTime(start);
			CalibrationState = GyroCalibrationState.Calibrating;
		}
		CalibrationChanged?.Invoke();
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
		if (profiles != null)
		{
			profiles.Changed -= OnProfilesChanged;
		}
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

	private bool IsActive(Buttons buttons)
	{
		return options.Activation switch
		{
			GyroActivation.Always => true,
			GyroActivation.WhileL1 => (buttons & Buttons.L1) != 0,
			GyroActivation.WhileR1 => (buttons & Buttons.R1) != 0,
			_ => (buttons & Buttons.L2) != 0
		};
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

	// Read thread: calibration samples and mouse mode.
	private void OnInput(InputSnapshot input)
	{
		TimeSpan now = time.GetElapsedTime(start);
		(short X, short Y, short Z)? finishedOffset = null;
		int dx = 0;
		int dy = 0;
		lock (gate)
		{
			if (calibration != null)
			{
				calibration.Add(input);
				if (now - calibrationStart >= CalibrationTime)
				{
					finishedOffset = calibration.Offset;
					calibration = null;
				}
			}
			bool active = options.Enabled && options.Output == GyroOutput.Mouse && IsActive(input.Buttons) && pcControl?.IsActive != true;
			if (active)
			{
				(dx, dy) = aim.Mouse(input, now);
				// The first report after activation only starts the clock.
				if (!wasActive)
				{
					(dx, dy) = (0, 0);
				}
			}
			wasActive = active;
		}
		if (dx != 0 || dy != 0)
		{
			output.MoveMouse(dx, dy);
		}
		if (finishedOffset is { } offset)
		{
			settings.Update(s => s with { Gyro = s.EffectiveGyro with { OffsetX = offset.X, OffsetY = offset.Y, OffsetZ = offset.Z } });
			CalibrationState = GyroCalibrationState.Done;
			CalibrationChanged?.Invoke();
		}
	}

	private void OnProfilesChanged() => OnSettingsChanged(profiles!.Effective);

	private void OnSettingsChanged(AppSettings current)
	{
		lock (gate)
		{
			ApplySettings(current);
		}
	}

	private void ApplySettings(AppSettings current)
	{
		options = current.EffectiveGyro;
		aim.Sensitivity = Math.Clamp(options.Sensitivity, 0.1, 5);
		aim.InvertX = options.InvertX;
		aim.InvertY = options.InvertY;
		aim.Axis = options.Axis;
		aim.Offset = (options.OffsetX, options.OffsetY, options.OffsetZ);
	}
}
