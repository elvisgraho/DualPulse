namespace DualPulse.Core.Protocol;

// One adaptive trigger effect: the 11 bytes written into an output report. Immutable; default is Off.
public readonly struct TriggerEffect
{
	public const int Length = 11;

	private static readonly byte[] OffBytes = [0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

	private readonly byte[]? bytes;

	internal TriggerEffect(ReadOnlySpan<byte> source)
	{
		if (source.Length != Length)
		{
			throw new ArgumentException($"A trigger effect is {Length} bytes.", nameof(source));
		}
		bytes = source.ToArray();
	}

	public static TriggerEffect Off => default;

	public ReadOnlySpan<byte> Bytes => bytes ?? OffBytes;
}
