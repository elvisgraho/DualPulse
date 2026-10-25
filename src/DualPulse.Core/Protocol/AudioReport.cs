using System.Buffers.Binary;
using System.IO.Hashing;
using Concentus;
using Concentus.Enums;

namespace DualPulse.Core.Protocol;

// Bluetooth audio output report 0x39 (547 bytes, one per 21.33 ms), carrying two haptic blocks and two Opus frames:
// - Byte 1: sequence in the high nibble.
// - Block 0x91 (6 bytes): flag mask 0x7E (its set bits below bit 6 announce the bytes that follow; bit 0 would admit
//   the microphone), four buffer-length bytes, and a counter rising by 2 (one per frame).
// - Block 0xD2: length 64, doubled: 2 x 64 bytes of signed 8-bit stereo haptics (32 frames each) at 3 kHz.
// - Block 0xD3 (speaker) or 0xD6 (headset): length 200, doubled: two 200-byte Opus frames.
// - CRC-32 over 0xA2 + bytes 0-542 in the last 4 bytes.
// Facts from the ds5bridge protocol notes and DS5Dongle (MPL-2.0); see THIRD-PARTY-NOTICES.
public static class AudioReport
{
	public const int Length = 547;

	public const int OpusFrameLength = 200;

	public static readonly TimeSpan Interval = TimeSpan.FromTicks(2 * HapticsReport.Interval.Ticks);

	private const byte ReportId = 0x39;

	private const byte FlagMask = 0x7E;

	private const int HapticsOffset = 12;

	private const int OpusOffset = 142;

	private const int CrcOffset = Length - 4;

	// hapticsLeft and hapticsRight hold 64 frames (two blocks); opus holds two contiguous 200-byte frames.
	public static int Build(Span<byte> destination, byte sequence, byte counter, byte bufferLength, ReadOnlySpan<sbyte> hapticsLeft,
		ReadOnlySpan<sbyte> hapticsRight, bool headset, ReadOnlySpan<byte> opus)
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
		report[3] = 0x06;
		report[4] = FlagMask;
		report[5..9].Fill(bufferLength);
		report[9] = counter;
		report[10] = 0xD2;
		report[11] = 0x40;
		for (int frame = 0; frame < 2 * HapticsReport.Frames; frame++)
		{
			report[HapticsOffset + 2 * frame] = (byte)hapticsLeft[frame];
			report[HapticsOffset + 2 * frame + 1] = (byte)hapticsRight[frame];
		}
		report[140] = headset ? (byte)0xD6 : (byte)0xD3;
		report[141] = OpusFrameLength;
		opus[..(2 * OpusFrameLength)].CopyTo(report[OpusOffset..]);
		Span<byte> crcInput = stackalloc byte[CrcOffset + 1];
		crcInput[0] = 0xA2;
		report[..CrcOffset].CopyTo(crcInput[1..]);
		BinaryPrimitives.WriteUInt32LittleEndian(report[CrcOffset..], Crc32.HashToUInt32(crcInput));
		return Length;
	}
}

// Captured audio for the controller's speaker or headset: resampled to 45 kHz (the controller plays 960 samples per
// 21.33 ms report, labelled as 48 kHz Opus) into a buffer capped at six reports (oldest dropped, so latency stays
// low). Each report takes two 480-sample frames once a two-report head start is buffered (again after running dry);
// otherwise it plays silence. Idle after about a second without sound. Opus through Concentus. Thread-safe: audio is
// pushed on the capture thread, frames taken on the session's audio thread.
public sealed class SpeakerStream
{
	public const int OutputRate = 45000;

	public const int FramesPerReport = 960;

	public const int MaxBufferedFrames = FramesPerReport * 6;

	private const int HeadStartFrames = FramesPerReport * 2;

	private const int IdleAfterReports = 47;

	private const float SoundThreshold = 1e-4f;

	private readonly object gate = new();

	private readonly (float Left, float Right)[] buffer = new (float, float)[MaxBufferedFrames];

	private readonly IOpusEncoder encoder;

	private readonly short[] frame = new short[480 * 2];

	private int head;

	private int count;

	private bool playing;

	private bool heardSound;

	private int silentReports;

	private int underruns;

	private int overflowed;

	private int sourceRate;

	private double position;

	private (float Left, float Right) previous;

	public SpeakerStream()
	{
		encoder = OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
		encoder.Bitrate = AudioReport.OpusFrameLength * 8 * 100;
		encoder.UseVBR = false;
	}

	public int Buffered
	{
		get
		{
			lock (gate)
			{
				return count;
			}
		}
	}

	// Diagnostics since creation: reports that found the buffer dry while playing, and frames dropped because it was full.
	public int Underruns
	{
		get
		{
			lock (gate)
			{
				return underruns;
			}
		}
	}

	public int Overflowed
	{
		get
		{
			lock (gate)
			{
				return overflowed;
			}
		}
	}

	// Sound was pushed in the last second or so.
	public bool IsActive
	{
		get
		{
			lock (gate)
			{
				return heardSound && silentReports < IdleAfterReports;
			}
		}
	}

	public void Push(ReadOnlySpan<float> interleaved, int sampleRate, int channels)
	{
		channels = Math.Max(1, channels);
		lock (gate)
		{
			if (sampleRate != sourceRate)
			{
				sourceRate = sampleRate;
				position = 0;
				previous = (0, 0);
			}
			double step = (double)sampleRate / OutputRate;
			bool sound = false;
			for (int i = 0; i + channels <= interleaved.Length; i += channels)
			{
				(float Left, float Right) current = (interleaved[i], interleaved[channels > 1 ? i + 1 : i]);
				sound |= Math.Abs(current.Left) > SoundThreshold || Math.Abs(current.Right) > SoundThreshold;
				while (position <= 1)
				{
					float t = (float)position;
					Add((previous.Left + (current.Left - previous.Left) * t, previous.Right + (current.Right - previous.Right) * t));
					position += step;
				}
				position -= 1;
				previous = current;
			}
			if (sound)
			{
				heardSound = true;
				silentReports = 0;
			}
		}
	}

	// Fills two Opus frames (400 bytes); returns whether they carry buffered audio (false: silence).
	public bool Next(Span<byte> opus)
	{
		bool audio;
		lock (gate)
		{
			if (!playing && count >= HeadStartFrames)
			{
				playing = true;
			}
			audio = playing && count >= FramesPerReport;
			if (!audio)
			{
				if (playing)
				{
					underruns++;
				}
				playing = false;
				silentReports++;
			}
			for (int half = 0; half < 2; half++)
			{
				for (int i = 0; i < 480; i++)
				{
					(float left, float right) = (0f, 0f);
					if (audio)
					{
						(left, right) = buffer[head];
						head = (head + 1) % MaxBufferedFrames;
						count--;
					}
					frame[2 * i] = ToPcm(left);
					frame[2 * i + 1] = ToPcm(right);
				}
				Encode(opus.Slice(half * AudioReport.OpusFrameLength, AudioReport.OpusFrameLength));
			}
		}
		return audio;
	}

	// Two silent Opus frames, for priming the controller before audio starts.
	public void Silence(Span<byte> opus)
	{
		lock (gate)
		{
			Array.Clear(frame);
			Encode(opus[..AudioReport.OpusFrameLength]);
			Encode(opus.Slice(AudioReport.OpusFrameLength, AudioReport.OpusFrameLength));
		}
	}

	// Caller holds gate.
	private void Add((float Left, float Right) sample)
	{
		if (count == MaxBufferedFrames)
		{
			head = (head + 1) % MaxBufferedFrames;
			count--;
			overflowed++;
		}
		buffer[(head + count) % MaxBufferedFrames] = sample;
		count++;
	}

	// Caller holds gate. CBR at 160 kbit/s gives exactly 200 bytes; anything shorter is zero padded.
	private void Encode(Span<byte> destination)
	{
		destination.Clear();
		encoder.Encode(frame, 480, destination, AudioReport.OpusFrameLength);
	}

	private static short ToPcm(float value) => (short)Math.Clamp(Math.Round(value * short.MaxValue), short.MinValue, short.MaxValue);
}
