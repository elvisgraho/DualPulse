using System.Buffers.Binary;
using System.IO.Hashing;

namespace DualPulse.Core.Protocol;

// Bluetooth haptics output report 0x32 (142 bytes): byte 1 carries the sequence in its high nibble; block 0x11
// (0x91 0x07, FE 00 00 00 00, buffer depth, counter); block 0x12 (0x92 0x40, 32 frames of signed 8-bit stereo at
// 3 kHz); CRC-32 over 0xA2 + bytes 0-137 in the last 4 bytes. One report per 10.67 ms. Facts from SAxense (Egor
// Vorontsov, MPL-2.0), the dualsense-bridge README and AnyPS5 issue #2056; see THIRD-PARTY-NOTICES.
public static class HapticsReport
{
	public const int Length = 142;

	public const int Frames = 32;

	public const double SampleRate = 3000;

	public static readonly TimeSpan Interval = TimeSpan.FromTicks((long)(TimeSpan.TicksPerSecond * Frames / SampleRate));

	private const byte ReportId = 0x32;

	private const byte CrcSeed = 0xA2;

	private const int SamplesOffset = 13;

	private const int CrcOffset = Length - 4;

	public static int Build(Span<byte> destination, byte sequence, byte counter, byte bufferDepth, ReadOnlySpan<sbyte> left, ReadOnlySpan<sbyte> right)
	{
		if (destination.Length < Length)
		{
			throw new ArgumentException($"Destination must be at least {Length} bytes.", nameof(destination));
		}
		Span<byte> report = destination[..Length];
		report.Clear();
		report[0] = ReportId;
		report[1] = (byte)((sequence & 0x0F) << 4);
		report[2] = 0x91;
		report[3] = 0x07;
		report[4] = 0xFE;
		report[9] = bufferDepth;
		report[10] = counter;
		report[11] = 0x92;
		report[12] = 0x40;
		for (int frame = 0; frame < Frames; frame++)
		{
			report[SamplesOffset + 2 * frame] = (byte)left[frame];
			report[SamplesOffset + 2 * frame + 1] = (byte)right[frame];
		}
		Span<byte> crcInput = stackalloc byte[CrcOffset + 1];
		crcInput[0] = CrcSeed;
		report[..CrcOffset].CopyTo(crcInput[1..]);
		BinaryPrimitives.WriteUInt32LittleEndian(report[CrcOffset..], Crc32.HashToUInt32(crcInput));
		return Length;
	}
}

// Game rumble as a haptic waveform: the heavy (left) motor becomes a low sine on the left actuator, the light (right)
// motor a higher one on the right, each scaled by its motor level. The phase carries over between reports, so the
// tone has no clicks at report boundaries.
public sealed class RumbleWaveform
{
	public const double HeavyHz = 70;

	public const double LightHz = 160;

	private double heavyPhase;

	private double lightPhase;

	public void Fill(byte large, byte small, Span<sbyte> left, Span<sbyte> right)
	{
		double heavyStep = 2 * Math.PI * HeavyHz / HapticsReport.SampleRate;
		double lightStep = 2 * Math.PI * LightHz / HapticsReport.SampleRate;
		for (int frame = 0; frame < HapticsReport.Frames; frame++)
		{
			left[frame] = (sbyte)Math.Round(large / 255.0 * 127 * Math.Sin(heavyPhase));
			right[frame] = (sbyte)Math.Round(small / 255.0 * 127 * Math.Sin(lightPhase));
			heavyPhase = (heavyPhase + heavyStep) % (2 * Math.PI);
			lightPhase = (lightPhase + lightStep) % (2 * Math.PI);
		}
	}
}
