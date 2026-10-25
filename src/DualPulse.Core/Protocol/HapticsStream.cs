namespace DualPulse.Core.Protocol;

// When to send Bluetooth haptics reports and what goes in them (the mixer's frames): while there is sound, then 250 ms of silence (short
// effects are otherwise cut off or keep buzzing), then nothing. Each new stream restarts the counter at 0 (a running
// counter made effects play at about twice their length).
public sealed class HapticsStream
{
	public static readonly TimeSpan SilenceTail = TimeSpan.FromMilliseconds(250);

	// About 50 ms of buffering in the controller; 0x08 is about 25 ms, 0x04 plays nothing.
	public const byte BufferDepth = 0x20;

	private readonly sbyte[] left = new sbyte[HapticsReport.Frames];

	private readonly sbyte[] right = new sbyte[HapticsReport.Frames];

	private TimeSpan lastActive;

	private byte counter;

	private byte sequence;

	public bool IsStreaming { get; private set; }

	// Builds the report for this tick into report and returns true, or returns false when nothing should be sent.
	public bool Next(HapticsMixer mixer, TimeSpan now, Span<byte> report)
	{
		if (mixer.Fill(left, right))
		{
			lastActive = now;
			if (!IsStreaming)
			{
				IsStreaming = true;
				counter = 0;
			}
		}
		else if (!IsStreaming || now - lastActive > SilenceTail)
		{
			IsStreaming = false;
			return false;
		}
		HapticsReport.Build(report, sequence, counter, BufferDepth, left, right);
		sequence = (byte)((sequence + 1) & 0x0F);
		counter++;
		return true;
	}
}
