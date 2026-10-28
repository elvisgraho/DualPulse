namespace DualPulse.Core.Audio;

// What games hear from the virtual DualSense's microphone (48 kHz 16-bit stereo in and out), in this order:
// - Low cut: a 2nd-order Butterworth high-pass at 100 Hz per side, taking out rumble below the voice (before the
//   gate, so rumble doesn't open it).
// - Noise gate, made to ignore mouth clicks: it measures loudness (RMS) every 5 ms and opens only when the sound stays
//   above the threshold for 20 ms (clicks and smacks are shorter). The sound runs 30 ms behind, so the start of a
//   word is still there when it opens (5 ms fade in). It closes once the smoothed level has stayed 6 dB below the
//   threshold for 300 ms, fading over 200 ms. Closed, it turns the sound down 30 dB instead of muting it, so opening
//   and closing never pumps. The threshold is set by hand: the DualSense gates its own microphone, so its quiet is
//   near-silence that a self-tuning threshold would mistake for the room.
// - Boost: up to +12 dB, through a soft limit that never reaches full scale.
// Without the gate nothing is delayed; with none of them the sound passes unchanged.
public sealed class MicrophoneProcessor
{
	private const int HopFrames = 240;

	private const int OpenAfterHops = 4;

	private const int HoldHops = 60;

	private const int DelayFrames = 1440;

	private const double CloseBelowDb = 6;

	// -30 dB.
	private const float ClosedGain = 0.0316f;

	private const float AttackStep = (1 - ClosedGain) / 240;

	private const float ReleaseStep = (1 - ClosedGain) / 9600;

	// Smoothing of the level that decides closing (about 15 ms; it falls about 1.5 dB per 5 ms).
	private const double Smoothing = 0.7;

	// The last 30 ms of sound (stereo), played out from the oldest.
	private readonly short[] delay = new short[DelayFrames * 2];

	private int delayAt;

	private double hopEnergy;

	private int hopFill;

	private double smoothedPower;

	private int loudHops;

	private int quietHops;

	private bool open;

	private float gain = ClosedGain;

	private short[] gated = [];

	public bool NoiseGate { get; set; }

	public bool LowCut { get; set; }

	// The high-pass (RBJ cookbook, 100 Hz, Q 0.707 at 48 kHz) and its state per side: last two inputs and outputs.
	private static readonly (double B0, double B1, double B2, double A1, double A2) HighPass = HighPassAt(100);

	private readonly double[] filterState = new double[8];

	private short[] filtered = [];

	private static (double, double, double, double, double) HighPassAt(double hz)
	{
		double w = 2 * Math.PI * hz / 48000, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * Math.Sqrt(0.5)), a0 = 1 + alpha;
		return ((1 + cos) / 2 / a0, -(1 + cos) / a0, (1 + cos) / 2 / a0, -2 * cos / a0, (1 - alpha) / a0);
	}

	private ReadOnlySpan<short> Filtered(ReadOnlySpan<short> stereo)
	{
		if (filtered.Length < stereo.Length)
		{
			filtered = new short[stereo.Length];
		}
		(double b0, double b1, double b2, double a1, double a2) = HighPass;
		for (int i = 0; i < stereo.Length; i++)
		{
			int side = (i & 1) * 4;
			double x = stereo[i];
			double y = b0 * x + b1 * filterState[side] + b2 * filterState[side + 1] - a1 * filterState[side + 2] - a2 * filterState[side + 3];
			filterState[side + 1] = filterState[side];
			filterState[side] = x;
			filterState[side + 3] = filterState[side + 2];
			filterState[side + 2] = y;
			filtered[i] = (short)Math.Clamp(Math.Round(y), short.MinValue, short.MaxValue);
		}
		return filtered.AsSpan(0, stereo.Length);
	}

	// Whether the gate is open (for diagnostics).
	public bool IsOpen => open;

	// Where the gate opens, in dBFS (RMS over 5 ms).
	public int GateThresholdDb { get; set; } = -45;

	private const double LimitFrom = 0.75;

	private const double LimitCeiling = 0.98;

	private short[] boosted = [];

	// 0-12.
	public int BoostDb { get; set; }

	public void Process(ReadOnlySpan<short> stereo, MicrophoneBlock blocks)
	{
		double boost = Math.Pow(10, Math.Clamp(BoostDb, 0, 12) / 20.0);
		if (LowCut)
		{
			stereo = Filtered(stereo);
		}
		else
		{
			Array.Clear(filterState);
		}
		if (NoiseGate)
		{
			stereo = Gated(stereo);
		}
		else
		{
			Reset();
		}
		if (boost == 1)
		{
			blocks(stereo);
			return;
		}
		if (boosted.Length < stereo.Length)
		{
			boosted = new short[stereo.Length];
		}
		for (int i = 0; i < stereo.Length; i++)
		{
			boosted[i] = Boosted(stereo[i], boost);
		}
		blocks(boosted.AsSpan(0, stereo.Length));
	}

	private ReadOnlySpan<short> Gated(ReadOnlySpan<short> stereo)
	{
		if (gated.Length < stereo.Length)
		{
			gated = new short[stereo.Length];
		}
		for (int i = 0; i + 1 < stereo.Length; i += 2)
		{
			gain = open ? Math.Min(1, gain + AttackStep) : Math.Max(ClosedGain, gain - ReleaseStep);
			gated[i] = (short)Math.Round(delay[delayAt] * gain);
			gated[i + 1] = (short)Math.Round(delay[delayAt + 1] * gain);
			delay[delayAt] = stereo[i];
			delay[delayAt + 1] = stereo[i + 1];
			delayAt = (delayAt + 2) % delay.Length;
			double mono = (stereo[i] + stereo[i + 1]) / 2.0;
			hopEnergy += mono * mono;
			if (++hopFill == HopFrames)
			{
				Decide(hopEnergy / HopFrames / (32767.0 * 32767.0));
				hopEnergy = 0;
				hopFill = 0;
			}
		}
		return gated.AsSpan(0, stereo.Length & ~1);
	}

	// Once per 5 ms hop, with its mean power (full scale = 1).
	private void Decide(double power)
	{
		smoothedPower = Smoothing * smoothedPower + (1 - Smoothing) * power;
		double levelDb = 10 * Math.Log10(power + 1e-12);
		double smoothedDb = 10 * Math.Log10(smoothedPower + 1e-12);
		loudHops = levelDb >= GateThresholdDb ? loudHops + 1 : 0;
		if (!open)
		{
			if (loudHops >= OpenAfterHops)
			{
				open = true;
				quietHops = 0;
			}
			return;
		}
		quietHops = smoothedDb < GateThresholdDb - CloseBelowDb ? quietHops + 1 : 0;
		if (quietHops >= HoldHops)
		{
			open = false;
		}
	}

	private void Reset()
	{
		Array.Clear(delay);
		delayAt = 0;
		hopEnergy = 0;
		hopFill = 0;
		loudHops = 0;
		quietHops = 0;
		open = false;
		gain = ClosedGain;
		smoothedPower = 0;
	}

	private static short Boosted(double sample, double boost)
	{
		double x = sample * boost / short.MaxValue;
		double magnitude = Math.Abs(x);
		if (magnitude > LimitFrom)
		{
			magnitude = LimitFrom + (LimitCeiling - LimitFrom) * Math.Tanh((magnitude - LimitFrom) / (LimitCeiling - LimitFrom));
		}
		return (short)Math.Round(Math.Sign(x) * magnitude * short.MaxValue);
	}
}
