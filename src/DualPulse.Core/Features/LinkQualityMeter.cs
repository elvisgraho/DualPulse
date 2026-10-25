namespace DualPulse.Core.Features;

public sealed class LinkQualitySnapshot
{
	public double ReportsPerSecond { get; }

	// Null when the transport has no sequence counter (USB).
	public double? LossPercent { get; }

	public double MaxGapMilliseconds { get; }

	public LinkQualitySnapshot(double reportsPerSecond, double? lossPercent, double maxGapMilliseconds)
	{
		ReportsPerSecond = reportsPerSecond;
		LossPercent = lossPercent;
		MaxGapMilliseconds = maxGapMilliseconds;
	}

	// Kept short: it is appended to the connection-type line of the Connection Status card.
	public string Summary => LossPercent.HasValue ? $"{ReportsPerSecond:F0} Hz · {LossPercent.Value:0.#}% loss" : $"{ReportsPerSecond:F0} Hz";
}

// measures input report rate, loss and the worst gap between reports, in one-second windows. Windows
// cannot report Bluetooth RSSI for a connected classic-Bluetooth controller, so this is the link-quality readout.
// Bluetooth 0x31 reports carry a 4-bit sequence number (high nibble of byte 1); gaps in it are dropped reports.
// Observe() is called from the read thread; Current is read from the UI thread.
public sealed class LinkQualityMeter
{
	private static readonly long WindowTicks = TimeSpan.TicksPerSecond;

	private long windowStartTicks = -1;

	private long previousTicks = -1;

	private int previousSequence = -1;

	private int received;

	private int lost;

	private long maxGapTicks;

	private bool hasSequence;

	public LinkQualitySnapshot? Current { get; private set; }

	public void Observe(int? sequence, long nowTicks)
	{
		if (windowStartTicks < 0)
		{
			windowStartTicks = nowTicks;
		}
		if (previousTicks >= 0)
		{
			maxGapTicks = Math.Max(maxGapTicks, nowTicks - previousTicks);
		}
		previousTicks = nowTicks;
		if (sequence.HasValue)
		{
			hasSequence = true;
			if (previousSequence >= 0)
			{
				lost += ((sequence.Value - previousSequence) & 0xF) - 1 + (sequence.Value == previousSequence ? 16 : 0);
			}
			previousSequence = sequence.Value;
		}
		received++;
		long elapsed = nowTicks - windowStartTicks;
		if (elapsed >= WindowTicks)
		{
			double seconds = (double)elapsed / TimeSpan.TicksPerSecond;
			double? loss = hasSequence ? (double?)(100.0 * lost / (received + lost)) : null;
			Current = new LinkQualitySnapshot(received / seconds, loss, (double)maxGapTicks / TimeSpan.TicksPerMillisecond);
			windowStartTicks = nowTicks;
			received = 0;
			lost = 0;
			maxGapTicks = 0;
		}
	}
}
