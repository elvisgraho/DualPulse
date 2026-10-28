namespace DualPulse.Core.Audio;

// What games hear from the virtual DualSense's microphone (48 kHz 16-bit stereo in and out):
// - Noise gate, made to ignore mouth clicks: it measures loudness (RMS) every 5 ms and opens only when the sound stays
//   above the open threshold for 20 ms (clicks and smacks are shorter). The sound runs 30 ms behind, so the start of a
//   word is still there when it opens (5 ms fade in). It stays open 200 ms after the sound drops below the close
//   threshold, then fades out over 150 ms. The thresholds follow the room: the noise floor is the quietest level
//   heard (rising at most 1 dB a second; exact digital silence, as at the start of a recording, doesn't count); open
//   is 12 dB above it, close 6 dB above it.
// - Boost: up to +12 dB, through a soft limit that never reaches full scale.
// Without the gate nothing is delayed; without either the sound passes unchanged.
public sealed class MicrophoneProcessor
{
	private const int HopFrames = 240;

	private const int OpenAfterHops = 4;

	private const int HoldHops = 40;

	private const int DelayFrames = 1440;

	private const float AttackStep = 1f / 240;

	private const float ReleaseStep = 1f / 7200;

	private const double OpenAboveFloorDb = 12;

	private const double CloseAboveFloorDb = 6;

	private const double FloorRisePerHopDb = 0.005;

	private const double HighestFloorDb = -35;

	// Below this a hop is digital silence, not a room.
	private const double SilenceDb = -100;

	// The last 30 ms of sound (stereo), played out from the oldest.
	private readonly short[] delay = new short[DelayFrames * 2];

	private int delayAt;

	private double hopEnergy;

	private int hopFill;

	private double? floorDb;

	private int loudHops;

	private int quietHops;

	private bool open;

	private float gain;

	private short[] gated = [];

	public bool NoiseGate { get; set; }

	private const double LimitFrom = 0.75;

	private const double LimitCeiling = 0.98;

	private short[] boosted = [];

	// 0-12.
	public int BoostDb { get; set; }

	public void Process(ReadOnlySpan<short> stereo, MicrophoneBlock blocks)
	{
		double boost = Math.Pow(10, Math.Clamp(BoostDb, 0, 12) / 20.0);
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
			gain = open ? Math.Min(1, gain + AttackStep) : Math.Max(0, gain - ReleaseStep);
			gated[i] = (short)Math.Round(delay[delayAt] * gain);
			gated[i + 1] = (short)Math.Round(delay[delayAt + 1] * gain);
			delay[delayAt] = stereo[i];
			delay[delayAt + 1] = stereo[i + 1];
			delayAt = (delayAt + 2) % delay.Length;
			double mono = (stereo[i] + stereo[i + 1]) / 2.0;
			hopEnergy += mono * mono;
			if (++hopFill == HopFrames)
			{
				Decide(10 * Math.Log10(hopEnergy / HopFrames / (32767.0 * 32767.0) + 1e-12));
				hopEnergy = 0;
				hopFill = 0;
			}
		}
		return gated.AsSpan(0, stereo.Length & ~1);
	}

	// Once per 5 ms hop, with its level in dBFS.
	private void Decide(double levelDb)
	{
		if (levelDb > SilenceDb)
		{
			double floor = floorDb is { } current ? Math.Min(levelDb, current + FloorRisePerHopDb) : levelDb;
			floorDb = Math.Min(floor, HighestFloorDb);
		}
		if (floorDb == null)
		{
			return;
		}
		double openAt = floorDb.Value + OpenAboveFloorDb;
		double closeAt = floorDb.Value + CloseAboveFloorDb;
		loudHops = levelDb >= openAt ? loudHops + 1 : 0;
		if (!open)
		{
			if (loudHops >= OpenAfterHops)
			{
				open = true;
				quietHops = 0;
			}
			return;
		}
		quietHops = levelDb < closeAt ? quietHops + 1 : 0;
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
		gain = 0;
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
