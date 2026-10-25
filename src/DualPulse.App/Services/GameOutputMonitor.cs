using Microsoft.Extensions.Logging;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

// Diagnostics for a game driving the virtual DualSense: logs each new combination of output flags (flag0, flag1,
// flag2 of the common block) with the rumble motors and audio bytes (volumes, audio control), the first motor movement
// under a known combination, and a summary of the game's haptics and speaker audio for each 5 s window that isn't
// silent. With the speaker stream, the summary also counts the game's output blocks and the speaker buffer's underruns
// and dropped frames in the window. Called from the pad's threads.
internal sealed class GameOutputMonitor(ILogger logger, TimeProvider time, SpeakerStream? speaker = null)
{
	private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

	// Below this peak a window counts as silence (Windows streams near-zero samples while the speaker is open).
	private const int SilencePeak = 64;

	private readonly object gate = new();

	private readonly HashSet<(byte, byte, byte)> seenFlags = [];

	private bool motorsSeen;

	private long windowStart = time.GetTimestamp();

	private int hapticBlocks;

	private int hapticPeak;

	private int speakerBlocks;

	private int speakerPeak;

	private int outputBlocks;

	private int underrunsBefore;

	private int overflowedBefore;

	public void Output(byte[] block)
	{
		if (block.Length < 39)
		{
			return;
		}
		(byte, byte, byte) flags = (block[0], block[1], block[38]);
		bool moving = block[2] != 0 || block[3] != 0;
		string? line = null;
		lock (gate)
		{
			outputBlocks++;
			if (seenFlags.Add(flags))
			{
				line = $"Game output flags {flags.Item1:X2} {flags.Item2:X2} {flags.Item3:X2}, motors R {block[2]} L {block[3]}, audio {block[4]:X2} {block[5]:X2} {block[6]:X2} {block[7]:X2}";
				motorsSeen |= moving;
			}
			else if (moving && !motorsSeen)
			{
				motorsSeen = true;
				line = $"Game rumble motors R {block[2]} L {block[3]}";
			}
		}
		if (line != null)
		{
			logger.LogInformation("{Line}", line);
		}
	}

	public void Haptics(ReadOnlySpan<short> samples) => Audio(samples, haptics: true);

	public void Speaker(ReadOnlySpan<short> samples) => Audio(samples, haptics: false);

	private void Audio(ReadOnlySpan<short> samples, bool haptics)
	{
		int peak = 0;
		foreach (short sample in samples)
		{
			peak = Math.Max(peak, Math.Abs((int)sample));
		}
		string? line = null;
		lock (gate)
		{
			if (haptics)
			{
				hapticBlocks++;
				hapticPeak = Math.Max(hapticPeak, peak);
			}
			else
			{
				speakerBlocks++;
				speakerPeak = Math.Max(speakerPeak, peak);
			}
			TimeSpan elapsed = time.GetElapsedTime(windowStart);
			if (elapsed >= Window)
			{
				line = Math.Max(hapticPeak, speakerPeak) < SilencePeak ? null : $"Game audio, last {elapsed.TotalSeconds:0} s: haptics {hapticBlocks} blocks (peak {hapticPeak}), speaker {speakerBlocks} blocks (peak {speakerPeak})";
				if (speaker != null)
				{
					int underruns = speaker.Underruns;
					int overflowed = speaker.Overflowed;
					if (line != null)
					{
						line += $", output {outputBlocks} blocks; speaker buffer {underruns - underrunsBefore} underruns, {overflowed - overflowedBefore} frames dropped";
					}
					underrunsBefore = underruns;
					overflowedBefore = overflowed;
				}
				windowStart = time.GetTimestamp();
				hapticBlocks = hapticPeak = speakerBlocks = speakerPeak = outputBlocks = 0;
			}
		}
		if (line != null)
		{
			logger.LogInformation("{Line}", line);
		}
	}
}
