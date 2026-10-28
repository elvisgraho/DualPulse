namespace DualPulse.Core.Audio;

// The last few seconds of a 48 kHz stereo stream as peak levels (0-1, the louder side), one per bin, for drawing a
// waveform. Only finished bins are shown. Thread-safe: pushed from a capture thread, read from the UI.
public sealed class PeakHistory
{
	private const int SampleRate = 48000;

	private readonly object gate = new();

	private readonly float[] bins;

	private readonly int framesPerBin;

	private int next;

	private int framesInBin;

	private float binPeak;

	private long framesSeen;

	public PeakHistory(int binsPerSecond = 40, double seconds = 5)
	{
		bins = new float[(int)Math.Round(binsPerSecond * seconds)];
		framesPerBin = SampleRate / binsPerSecond;
	}

	// Every frame pushed so far; it stops growing when no sound arrives.
	public long FramesSeen
	{
		get
		{
			lock (gate)
			{
				return framesSeen;
			}
		}
	}

	public void Push(ReadOnlySpan<short> stereo)
	{
		lock (gate)
		{
			for (int i = 0; i + 1 < stereo.Length; i += 2)
			{
				float level = Math.Max(Math.Abs(stereo[i] / 32767f), Math.Abs(stereo[i + 1] / 32767f));
				binPeak = Math.Max(binPeak, Math.Min(1f, level));
				framesSeen++;
				if (++framesInBin == framesPerBin)
				{
					bins[next] = binPeak;
					next = (next + 1) % bins.Length;
					framesInBin = 0;
					binPeak = 0;
				}
			}
		}
	}

	// Oldest first.
	public float[] Snapshot()
	{
		lock (gate)
		{
			return [.. bins[next..], .. bins[..next]];
		}
	}

	// For smooth scrolling: the finished bins plus the one still filling (last), and how full that one is (0-1).
	public (float[] Peaks, double Progress) Live()
	{
		lock (gate)
		{
			return ([.. bins[next..], .. bins[..next], binPeak], framesInBin / (double)framesPerBin);
		}
	}

	public void Clear()
	{
		lock (gate)
		{
			Array.Clear(bins);
			framesInBin = 0;
			binPeak = 0;
		}
	}
}
