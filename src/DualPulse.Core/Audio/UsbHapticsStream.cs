namespace DualPulse.Core.Audio;

// Haptic frames (3 kHz, 8-bit, left and right) as the controller's own USB sound device wants them on a cable:
// 48 kHz, 16-bit, 4 channels, with the speaker channels (1-2) silent and the haptics on 3-4. Each frame becomes 16
// samples that ramp from the previous frame's level to its own, so the steps don't add a 3 kHz buzz.
public sealed class UsbHapticsStream
{
	private const int Upsample = 16;

	private const int Channels = 4;

	private readonly List<short> pending = [];

	private (int Left, int Right) previous;

	public void Push(sbyte left, sbyte right)
	{
		(int Left, int Right) next = (left * 256, right * 256);
		for (int i = 1; i <= Upsample; i++)
		{
			pending.Add(0);
			pending.Add(0);
			pending.Add((short)(previous.Left + (next.Left - previous.Left) * i / Upsample));
			pending.Add((short)(previous.Right + (next.Right - previous.Right) * i / Upsample));
		}
		previous = next;
	}

	// Everything pushed since the last drain, interleaved for a 4-channel stream.
	public short[] Drain()
	{
		short[] samples = [.. pending];
		pending.Clear();
		return samples;
	}

	public static int ChannelCount => Channels;
}
