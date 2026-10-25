using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class GyroAimTests
{
	private readonly GyroAim gyro = new();

	private TimeSpan now;

	// Raw gyro counts for a turn rate in degrees per second (turning right is negative yaw; tilting up is positive pitch).
	private static short Yaw(double degreesPerSecondRight) => (short)Math.Round(-degreesPerSecondRight * GyroAim.CountsPerDegree);

	private static short Pitch(double degreesPerSecondUp) => (short)Math.Round(degreesPerSecondUp * GyroAim.CountsPerDegree);

	private static InputSnapshot Input(short gyroX = 0, short gyroY = 0, short gyroZ = 0, byte rightX = 128, byte rightY = 128,
		short accelY = 0, short accelZ = 0, short accelX = 0) =>
		new()
		{
			LeftX = 128, LeftY = 128, RightX = rightX, RightY = rightY, GyroX = gyroX, GyroY = gyroY, GyroZ = gyroZ,
			AccelX = accelX, AccelY = accelY, AccelZ = accelZ
		};

	// Feeds the same reading for the given time in 4 ms reports and returns the total mouse movement.
	private (int X, int Y) Mouse(InputSnapshot input, int milliseconds = 1000)
	{
		int x = 0;
		int y = 0;
		gyro.Mouse(input, now);
		for (int elapsed = 4; elapsed <= milliseconds; elapsed += 4)
		{
			now += TimeSpan.FromMilliseconds(4);
			(int dx, int dy) = gyro.Mouse(input, now);
			x += dx;
			y += dy;
		}
		return (x, y);
	}

	[Fact]
	public void Still_AndBelowTheDeadzone_DoNotMove()
	{
		Assert.Equal((0, 0), Mouse(Input()));
		Assert.Equal((0, 0), Mouse(Input(gyroY: Yaw(0.3))));
	}

	[Fact]
	public void TurningRight_MovesTheMouseRight_BySensitivity()
	{
		(int x, int y) = Mouse(Input(gyroY: Yaw(90)));

		double expected = 90 * GyroAim.PixelsPerDegree;
		Assert.InRange(x, expected * 0.95, expected * 1.05);
		Assert.Equal(0, y);
	}

	[Fact]
	public void TiltingUp_MovesTheMouseUp()
	{
		(_, int y) = Mouse(Input(gyroX: Pitch(30)), 200);

		Assert.True(y < 0);
	}

	[Fact]
	public void Invert_FlipsTheDirection()
	{
		gyro.InvertX = true;

		(int x, _) = Mouse(Input(gyroY: Yaw(90)), 200);

		Assert.True(x < 0);
	}

	[Fact]
	public void Calibration_RemovesConstantDrift()
	{
		short drift = Yaw(3);
		gyro.Offset = (0, drift, 0);

		Assert.Equal((0, 0), Mouse(Input(gyroY: drift)));
	}

	[Fact]
	public void TiltedGrip_TurningAroundRealUp_MovesFully()
	{
		// Held tilted 45 degrees towards the player: gravity and the turn are split between the yaw and roll sensors.
		double tilt = Math.Sqrt(0.5);
		short up = (short)(8192 * tilt);
		short split = (short)Math.Round(-90 * GyroAim.CountsPerDegree * tilt);

		(int x, _) = Mouse(Input(gyroY: split, gyroZ: split, accelY: up, accelZ: up));

		double expected = 90 * GyroAim.PixelsPerDegree;
		Assert.InRange(x, expected * 0.95, expected * 1.05);
	}

	[Fact]
	public void DiagonalMove_HandAcceleration_DoesNotBleedPitchIntoTurn()
	{
		// Up is learned while the controller rests flat.
		Mouse(Input(accelY: 8192), 200);

		// Turning right while tilting up; moving the hand throws the accelerometer sideways.
		(int x, _) = Mouse(Input(gyroX: Pitch(30), gyroY: Yaw(90), accelX: 5800, accelY: 5800));

		double expected = 90 * GyroAim.PixelsPerDegree;
		Assert.InRange(x, expected * 0.95, expected * 1.05);
	}

	[Fact]
	public void RollAxis_TurnsWithTheRollSensor()
	{
		gyro.Axis = GyroAxis.Roll;

		(int x, _) = Mouse(Input(gyroZ: Yaw(90)), 200);

		Assert.True(x > 0);
	}

	[Fact]
	public void SmallMovements_AreSmoothed_LargeOnesPassStraightThrough()
	{
		// A single jittery sample at a low rate is averaged with the still samples around it.
		gyro.Mouse(Input(), now);
		now += TimeSpan.FromMilliseconds(4);
		(int jitter, _) = gyro.Mouse(Input(gyroY: Yaw(2)), now);
		Assert.Equal(0, jitter);
	}

	[Fact]
	public void Stick_GyroDeflectsTheRightStick_WithAntiDeadzone()
	{
		(byte x, byte y) = gyro.Stick(Input(gyroY: Yaw(90)));

		// 90 deg/s at sensitivity 1 is half deflection; the anti-deadzone lifts it past games' inner deadzone.
		double deflection = GyroAim.StickAntiDeadzone + (1 - GyroAim.StickAntiDeadzone) * (90 / GyroAim.FullStickRate);
		Assert.InRange(x, 128 + (int)(deflection * 127) - 2, 128 + (int)(deflection * 127) + 2);
		Assert.Equal(128, y);
	}

	[Fact]
	public void Stick_AddsToTheRealStick_AndClamps()
	{
		Assert.Equal((byte)255, gyro.Stick(Input(gyroY: Yaw(90), rightX: 255)).X);
		// Without gyro movement the real stick passes through unchanged (a fresh instance: no smoothing history).
		Assert.Equal(((byte)200, (byte)90), new GyroAim().Stick(Input(rightX: 200, rightY: 90)));
	}

	[Fact]
	public void CalibrationHelper_AveragesStillSamples()
	{
		GyroCalibration calibration = new();
		calibration.Add(Input(gyroX: 10, gyroY: -20, gyroZ: 4));
		calibration.Add(Input(gyroX: 14, gyroY: -24, gyroZ: 6));

		Assert.Equal(((short)12, (short)-22, (short)5), calibration.Offset);
		Assert.Equal(2, calibration.Samples);
	}
}
