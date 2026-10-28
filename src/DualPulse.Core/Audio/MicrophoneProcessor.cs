namespace DualPulse.Core.Audio;

// What games hear from the virtual DualSense's microphone (48 kHz 16-bit stereo in and out): boosted by up to 12 dB
// through a soft limit that never reaches full scale. Without a boost the sound passes unchanged.
public sealed class MicrophoneProcessor
{
	private const double LimitFrom = 0.75;

	private const double LimitCeiling = 0.98;

	private short[] boosted = [];

	// 0-12.
	public int BoostDb { get; set; }

	public void Process(ReadOnlySpan<short> stereo, MicrophoneBlock blocks)
	{
		double boost = Math.Pow(10, Math.Clamp(BoostDb, 0, 12) / 20.0);
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
