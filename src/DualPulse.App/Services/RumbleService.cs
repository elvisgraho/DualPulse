using Microsoft.Extensions.Hosting;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

public interface IRumbleService
{
	// Rumble a game sent to the virtual pad. Large is the left (heavy) motor, small the right (light) one.
	void SetGameRumble(byte large, byte small);

	// Runs the chosen motors for TestDuration at the current strength (even when game rumble is muted), then goes back
	// to whatever the game asked for.
	void TestMotors(bool left, bool right);

	// What the motors (or, over Bluetooth, the haptics) are playing now, after strength and mute.
	(byte Left, byte Right) Levels { get; }

	// A short tick on the light motor, as feedback (callers use it over Bluetooth only: on the cable classic rumble
	// switches the haptics off until the controller is power-cycled).
	void Pulse()
	{
	}
}

// Owns the controller's motors and rumble mode: scales and mutes game rumble per the settings, runs test pulses and
// applies the rumble mode (automatic follows the firmware).
public sealed class RumbleService : IRumbleService, IHostedService, IDisposable
{
	public const byte TestLevel = 200;

	public static readonly TimeSpan TestDuration = TimeSpan.FromSeconds(1);

	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly TimeProvider time;

	private readonly object gate = new();

	private byte gameLarge;

	private byte gameSmall;

	private ITimer? testTimer;

	private (byte Left, byte Right)? test;

	public RumbleService(IControllerService controller, ISettingsService settings, TimeProvider time)
	{
		this.controller = controller;
		this.settings = settings;
		this.time = time;
		controller.SessionStarted += OnSessionStarted;
		settings.Changed += OnSettingsChanged;
	}

	public static byte Scale(byte value, RumbleSettings rumble)
	{
		return rumble.Muted ? (byte)0 : (byte)Math.Min(255, (int)Math.Round(value * Math.Clamp(rumble.Strength, 0, 200) / 100.0));
	}

	public (byte Left, byte Right) Levels { get; private set; }

	public void SetGameRumble(byte large, byte small)
	{
		lock (gate)
		{
			gameLarge = large;
			gameSmall = small;
			ApplyMotors();
		}
	}

	public void TestMotors(bool left, bool right)
	{
		lock (gate)
		{
			testTimer?.Dispose();
			test = (left ? TestLevel : (byte)0, right ? TestLevel : (byte)0);
			testTimer = time.CreateTimer(_ => EndTest(), null, TestDuration, Timeout.InfiniteTimeSpan);
			ApplyMotors();
		}
	}

	public void Pulse()
	{
		lock (gate)
		{
			testTimer?.Dispose();
			test = (0, TestLevel);
			testTimer = time.CreateTimer(_ => EndTest(), null, PulseDuration, Timeout.InfiniteTimeSpan);
			ApplyMotors();
		}
	}

	private static readonly TimeSpan PulseDuration = TimeSpan.FromMilliseconds(80);

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		controller.SessionStarted -= OnSessionStarted;
		settings.Changed -= OnSettingsChanged;
		lock (gate)
		{
			testTimer?.Dispose();
			testTimer = null;
		}
	}

	private void EndTest()
	{
		lock (gate)
		{
			testTimer?.Dispose();
			testTimer = null;
			test = null;
			ApplyMotors();
		}
	}

	private void OnSessionStarted(ControllerSession session)
	{
		lock (gate)
		{
			gameLarge = 0;
			gameSmall = 0;
			ApplyMode(session);
		}
	}

	private void OnSettingsChanged(AppSettings current)
	{
		lock (gate)
		{
			if (controller.Session is { } session)
			{
				ApplyMode(session);
			}
			ApplyMotors();
		}
	}

	// Caller holds gate.
	private void ApplyMotors()
	{
		if (controller.Session is not { } session)
		{
			return;
		}
		RumbleSettings rumble = settings.Current.EffectiveRumble;
		(byte left, byte right) = test is { } pulse
			? (Scale(pulse.Left, rumble with { Muted = false }), Scale(pulse.Right, rumble with { Muted = false }))
			: (Scale(gameLarge, rumble), Scale(gameSmall, rumble));
		Levels = (left, right);
		OutputState output = session.Output;
		if (UsesHaptics(session, rumble))
		{
			// The actuators belong to the haptics stream; report 0x31 carries no rumble.
			if (!output.HapticsMode || output.LeftMotor != 0 || output.RightMotor != 0)
			{
				session.Apply(state => state with { HapticsMode = true, LeftMotor = 0, RightMotor = 0 });
			}
			session.SetHapticRumble(left, right);
			return;
		}
		if (output.HapticsMode)
		{
			session.SetHapticRumble(0, 0);
		}
		if (output.HapticsMode || output.LeftMotor != left || output.RightMotor != right)
		{
			session.Apply(state => state with { HapticsMode = false, LeftMotor = left, RightMotor = right });
		}
	}

	private static bool UsesHaptics(ControllerSession session, RumbleSettings rumble) =>
		rumble.BluetoothHaptics && session.Transport == Transport.Bluetooth;

	// Caller holds gate.
	private void ApplyMode(ControllerSession session)
	{
		bool improved = settings.Current.EffectiveRumble.Mode switch
		{
			RumbleMode.Classic => false,
			RumbleMode.Improved => true,
			_ => session.Firmware?.SupportsImprovedRumble ?? false
		};
		bool haptics = UsesHaptics(session, settings.Current.EffectiveRumble);
		if (session.Output.ImprovedRumble != improved || session.Output.HapticsMode != haptics)
		{
			session.Apply(state => state with { ImprovedRumble = improved, HapticsMode = haptics });
		}
	}
}
