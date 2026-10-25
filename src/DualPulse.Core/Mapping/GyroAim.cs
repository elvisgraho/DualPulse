using DualPulse.Core.Protocol;

namespace DualPulse.Core.Mapping;

public enum GyroAxis
{
	// Turning the controller left/right around the real up direction (player space), however it is held.
	Yaw,
	// Tilting it sideways.
	Roll
}

// Gyro aiming: the controller's turn rate (degrees per second) becomes mouse movement or right-stick deflection. A
// small deadzone stops resting hands drifting, small movements are smoothed (soft tiered smoothing: averaged when
// slow, passed straight through when fast) and Offset removes the sensor's resting bias (calibration). Raw sensor
// units are about 16.4 counts per degree per second (16-bit, +/-2000 deg/s).
public sealed class GyroAim
{
	public const double CountsPerDegree = 16.384;

	// Mouse pixels per degree of turn at sensitivity 1.
	public const double PixelsPerDegree = 15;

	// Turn rate that gives full stick deflection at sensitivity 1.
	public const double FullStickRate = 180;

	// Lifts any gyro deflection past games' inner stick deadzone, so slow aiming still moves.
	public const double StickAntiDeadzone = 0.12;

	private const double Deadzone = 0.5;

	private const double SmoothBelow = 3;

	private const double DirectAbove = 6;

	private const int SmoothingSamples = 4;

	// The accelerometer reads about 8192 counts per g; readings far from 1 g include hand movement, not just gravity.
	private const double OneG = 8192;

	private const double MinGravity = 0.75 * OneG;

	private const double MaxGravity = 1.25 * OneG;

	// Up is only relearned while the controller is nearly still; during a move it holds, so hand acceleration and
	// the rotation itself can't swing it and bleed one axis into the other.
	private const double StillBelow = 10;

	// How quickly the up direction follows the accelerometer while still, per report.
	private const double GravityBlend = 0.05;

	// Player space lets some roll count towards turning, so wrist-steering still turns (as in JoyShockMapper).
	private const double YawRelax = 1.41;

	private static readonly TimeSpan MaxStep = TimeSpan.FromMilliseconds(50);

	private readonly (double X, double Y)[] recent = new (double X, double Y)[SmoothingSamples];

	private int next;

	private TimeSpan? previous;

	private double pendingX;

	private double pendingY;

	private (double X, double Y, double Z)? up;

	public double Sensitivity { get; set; } = 1;

	public bool InvertX { get; set; }

	public bool InvertY { get; set; }

	public GyroAxis Axis { get; set; } = GyroAxis.Yaw;

	public (short X, short Y, short Z) Offset { get; set; }

	public (int Dx, int Dy) Mouse(in InputSnapshot input, TimeSpan now)
	{
		double seconds = previous is { } last ? Math.Min((now - last).TotalSeconds, MaxStep.TotalSeconds) : 0;
		previous = now;
		(double right, double up) = Rates(input);
		if (seconds <= 0)
		{
			return (0, 0);
		}
		pendingX += right * seconds * PixelsPerDegree * Sensitivity;
		pendingY -= up * seconds * PixelsPerDegree * Sensitivity;
		int dx = WholePart(pendingX);
		int dy = WholePart(pendingY);
		pendingX -= dx;
		pendingY -= dy;
		return (dx, dy);
	}

	// The right stick with the gyro added: real stick plus gyro, clamped to the stick's range.
	public (byte X, byte Y) Stick(in InputSnapshot input)
	{
		(double right, double up) = Rates(input);
		double fullRate = FullStickRate / Math.Max(Sensitivity, 0.01);
		double x = (input.RightX - 128) / 127.5 + Lift(right / fullRate);
		double y = (input.RightY - 128) / 127.5 + Lift(-up / fullRate);
		return (ToAxis(x), ToAxis(y));
	}

	// Degrees per second to the right and upwards, after offset, deadzone, inversion and smoothing.
	private (double Right, double Up) Rates(in InputSnapshot input)
	{
		double gyroX = input.GyroX - Offset.X;
		double gyroY = input.GyroY - Offset.Y;
		double gyroZ = input.GyroZ - Offset.Z;
		double turn = Axis == GyroAxis.Yaw ? PlayerYaw(input, gyroX, gyroY, gyroZ) : gyroZ;
		double right = -turn / CountsPerDegree;
		double up = gyroX / CountsPerDegree;
		if (InvertX)
		{
			right = -right;
		}
		if (InvertY)
		{
			up = -up;
		}
		double magnitude = Math.Sqrt(right * right + up * up);
		if (magnitude < Deadzone)
		{
			(right, up, magnitude) = (0, 0, 0);
		}
		recent[next] = (right, up);
		next = (next + 1) % SmoothingSamples;
		if (magnitude >= DirectAbove)
		{
			return (right, up);
		}
		double averageRight = recent.Average(r => r.X);
		double averageUp = recent.Average(r => r.Y);
		if (magnitude <= SmoothBelow)
		{
			return (averageRight, averageUp);
		}
		double direct = (magnitude - SmoothBelow) / (DirectAbove - SmoothBelow);
		return (averageRight + (right - averageRight) * direct, averageUp + (up - averageUp) * direct);
	}

	// The turn rate around the real up direction, learned from the accelerometer while still; the yaw sensor alone
	// when there is no usable reading yet.
	private double PlayerYaw(in InputSnapshot input, double gyroX, double gyroY, double gyroZ)
	{
		double length = Math.Sqrt((double)input.AccelX * input.AccelX + (double)input.AccelY * input.AccelY + (double)input.AccelZ * input.AccelZ);
		bool still = Math.Sqrt(gyroX * gyroX + gyroY * gyroY + gyroZ * gyroZ) / CountsPerDegree < StillBelow;
		if (length >= MinGravity && length <= MaxGravity && (still || up == null))
		{
			(double X, double Y, double Z) sample = (input.AccelX / length, input.AccelY / length, input.AccelZ / length);
			up = up is { } current
				? Normalize((current.X + (sample.X - current.X) * GravityBlend, current.Y + (sample.Y - current.Y) * GravityBlend, current.Z + (sample.Z - current.Z) * GravityBlend))
				: sample;
		}
		if (up is not { } u)
		{
			return gyroY;
		}
		// The controller is never held upside down, so up always has a positive Y.
		double sign = u.Y < 0 ? -1 : 1;
		double world = sign * (gyroX * u.X + gyroY * u.Y + gyroZ * u.Z);
		double local = Math.Sqrt(gyroY * gyroY + gyroZ * gyroZ);
		return Math.Sign(world) * Math.Min(Math.Abs(world) * YawRelax, local);
	}

	private static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
	{
		double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
		return length > 0 ? (v.X / length, v.Y / length, v.Z / length) : v;
	}

	private static double Lift(double deflection)
	{
		if (deflection == 0)
		{
			return 0;
		}
		double size = Math.Min(Math.Abs(deflection), 1);
		return Math.Sign(deflection) * (StickAntiDeadzone + (1 - StickAntiDeadzone) * size);
	}

	private static byte ToAxis(double value)
	{
		return (byte)Math.Clamp(Math.Round(128 + Math.Clamp(value, -1, 1) * 127.5), 0, 255);
	}

	private static int WholePart(double value)
	{
		return (int)Math.Truncate(value + (value >= 0 ? 1e-9 : -1e-9));
	}
}

// Averages the gyro while the controller lies still; the result is GyroAim.Offset.
public sealed class GyroCalibration
{
	private long sumX;

	private long sumY;

	private long sumZ;

	public int Samples { get; private set; }

	public (short X, short Y, short Z) Offset => Samples == 0
		? ((short)0, (short)0, (short)0)
		: ((short)Math.Round((double)sumX / Samples), (short)Math.Round((double)sumY / Samples), (short)Math.Round((double)sumZ / Samples));

	public void Add(in InputSnapshot input)
	{
		sumX += input.GyroX;
		sumY += input.GyroY;
		sumZ += input.GyroZ;
		Samples++;
	}
}
