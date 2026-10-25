using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class AudioHapticsTests
{
	private static float[] Tone(double hz, double seconds, int sampleRate = 48000, int channels = 2, float amplitude = 0.8f)
	{
		int frames = (int)(sampleRate * seconds);
		float[] samples = new float[frames * channels];
		for (int i = 0; i < frames; i++)
		{
			float value = (float)(amplitude * Math.Sin(2 * Math.PI * hz * i / sampleRate));
			for (int c = 0; c < channels; c++)
			{
				samples[i * channels + c] = value;
			}
		}
		return samples;
	}

	// Peak level of what the mixer holds after a steady tone (two reports' worth).
	private static (int Left, int Right) Peak(HapticsMixer mixer)
	{
		sbyte[] left = new sbyte[32];
		sbyte[] right = new sbyte[32];
		int peakLeft = 0;
		int peakRight = 0;
		for (int i = 0; i < 2; i++)
		{
			mixer.Fill(left, right);
			peakLeft = Math.Max(peakLeft, left.Max(s => Math.Abs((int)s)));
			peakRight = Math.Max(peakRight, right.Max(s => Math.Abs((int)s)));
		}
		return (peakLeft, peakRight);
	}

	[Fact]
	public void Converter_KeepsBass_CutsHighs()
	{
		HapticsMixer bass = new();
		HapticsMixer high = new();

		new HapticsAudioConverter(48000, 2).Process(Tone(100, 0.5), bass);
		new HapticsAudioConverter(48000, 2).Process(Tone(1000, 0.5), high);

		int full = (int)(0.8 * 127);
		Assert.InRange(Peak(bass).Left, full * 0.8, 127);
		Assert.InRange(Peak(high).Left, 0, full * 0.25);
	}

	[Fact]
	public void Converter_SoftLimit_LiftsQuietHaptics_AndSaturatesLoudOnes()
	{
		// Game haptics: a boost through a tanh curve, so soft effects are felt and strong ones don't clip harshly.
		HapticsMixer quiet = new();
		HapticsMixer loud = new();

		new HapticsAudioConverter(48000, 2) { SoftLimit = true, Intensity = 3 }.Process(Tone(100, 0.5, amplitude: 0.15f), quiet);
		new HapticsAudioConverter(48000, 2) { SoftLimit = true, Intensity = 3 }.Process(Tone(100, 0.5, amplitude: 0.8f), loud);

		Assert.InRange(Peak(quiet).Left, 45, 60);
		Assert.InRange(Peak(loud).Left, 115, 127);
	}

	[Fact]
	public void Converter_WiderCutoff_PassesSharperDetail()
	{
		HapticsMixer narrow = new();
		HapticsMixer wide = new();

		new HapticsAudioConverter(48000, 2).Process(Tone(800, 0.5), narrow);
		new HapticsAudioConverter(48000, 2, cutoffHz: 1000).Process(Tone(800, 0.5), wide);

		int full = (int)(0.8 * 127);
		Assert.InRange(Peak(narrow).Left, 0, full * 0.4);
		Assert.InRange(Peak(wide).Left, full * 0.5, 127);
	}

	[Fact]
	public void Converter_OutputsAt3kHz_ForAnySourceRate()
	{
		HapticsMixer at48 = new();
		HapticsMixer at44 = new();

		new HapticsAudioConverter(48000, 2).Process(Tone(100, 0.04), at48);
		new HapticsAudioConverter(44100, 2).Process(Tone(100, 0.04, 44100), at44);

		Assert.InRange(at48.Buffered, 119, 120);
		Assert.InRange(at44.Buffered, 119, 120);
	}

	[Fact]
	public void Converter_Intensity_ScalesTheLevel()
	{
		HapticsMixer half = new();

		new HapticsAudioConverter(48000, 2) { Intensity = 0.5 }.Process(Tone(100, 0.5), half);

		Assert.InRange(Peak(half).Left, 0.8 * 127 * 0.5 * 0.8, 0.8 * 127 * 0.5 * 1.1);
	}

	[Fact]
	public void Converter_NoiseGate_TreatsNearSilenceAsSilence()
	{
		HapticsMixer mixer = new();

		new HapticsAudioConverter(48000, 2).Process(Tone(100, 0.5, amplitude: 0.005f), mixer);

		Assert.False(mixer.Fill(new sbyte[32], new sbyte[32]));
	}

	[Fact]
	public void Converter_Mono_FeedsBothSides()
	{
		HapticsMixer mixer = new();

		new HapticsAudioConverter(48000, 1).Process(Tone(100, 0.5, channels: 1), mixer);

		(int left, int right) = Peak(mixer);
		Assert.True(left > 60);
		Assert.Equal(left, right);
	}

	[Fact]
	public void Mixer_SumsAudioAndRumble_AndClamps()
	{
		HapticsMixer mixer = new();
		for (int i = 0; i < 32; i++)
		{
			mixer.Push(120, -120);
		}
		mixer.SetRumble(255, 255);
		sbyte[] left = new sbyte[32];
		sbyte[] right = new sbyte[32];

		Assert.True(mixer.Fill(left, right));

		Assert.All(left, s => Assert.InRange(s, -127, 127));
		Assert.Contains(left, s => s == 127);
		Assert.Contains(right, s => s == -127);
	}

	[Fact]
	public void Mixer_Underrun_IsSilence_AndIdleWithoutRumble()
	{
		HapticsMixer mixer = new();
		sbyte[] left = new sbyte[32];
		sbyte[] right = new sbyte[32];

		Assert.False(mixer.Fill(left, right));
		Assert.All(left, s => Assert.Equal(0, s));

		mixer.SetRumble(100, 0);
		Assert.True(mixer.Fill(left, right));
	}

	[Fact]
	public void Mixer_DropsTheOldestFrames_BeyondSixReports()
	{
		HapticsMixer mixer = new();
		for (int i = 0; i < 300; i++)
		{
			mixer.Push((sbyte)(i % 100), 0);
		}

		Assert.Equal(192, mixer.Buffered);
		sbyte[] left = new sbyte[32];
		mixer.Fill(left, new sbyte[32]);
		Assert.Equal((sbyte)(108 % 100), left[0]);
	}

	// Feeds audio in 10 ms chunks (like capture does) and takes one report after each chunk; returns each report's peak.
	private static List<int> PunchyPeaks(Func<int, float> sampleAt, double seconds, out List<sbyte> output)
	{
		HapticsAudioConverter converter = new(48000, 1) { Style = HapticsAudioStyle.Punchy };
		HapticsMixer mixer = new();
		List<int> peaks = [];
		output = [];
		float[] chunk = new float[480];
		sbyte[] left = new sbyte[32];
		sbyte[] right = new sbyte[32];
		for (int start = 0; start < (int)(48000 * seconds); start += chunk.Length)
		{
			for (int i = 0; i < chunk.Length; i++)
			{
				chunk[i] = sampleAt(start + i);
			}
			converter.Process(chunk, mixer);
			while (mixer.Buffered >= 32)
			{
				mixer.Fill(left, right);
				peaks.Add(left.Max(v => Math.Abs((int)v)));
				output.AddRange(left);
			}
		}
		return peaks;
	}

	// 60 Hz kick: 50 ms burst every 500 ms.
	private static Func<int, float> Kicks(float amplitude) =>
		i => i % 24000 < 2400 ? (float)(amplitude * Math.Sin(2 * Math.PI * 60 * i / 48000)) : 0f;

	[Fact]
	public void Punchy_KickPattern_GivesDistinctPulses()
	{
		List<int> peaks = PunchyPeaks(Kicks(0.8f), 3, out _);

		// After the first second: strong during kicks, close to nothing between them.
		List<int> settled = peaks.Skip(94).ToList();
		int max = settled.Max();
		Assert.True(max > 80);
		Assert.True(settled.Count(p => p < max * 0.15) > settled.Count / 2);
	}

	[Fact]
	public void Punchy_SteadyBass_SettlesBelowItsOnset()
	{
		List<int> peaks = PunchyPeaks(i => (float)(0.8 * Math.Sin(2 * Math.PI * 60 * i / 48000)), 2, out _);

		int onset = peaks.Take(10).Max();
		double settled = peaks.TakeLast(20).Average();
		Assert.True(settled < onset * 0.75, $"onset {onset}, settled {settled}");
		Assert.True(settled > 10);
	}

	[Fact]
	public void Punchy_AutoLevel_EvensOutQuietTracks_Moderately()
	{
		int loud = PunchyPeaks(Kicks(0.8f), 4, out _).Skip(250).Max();
		int quiet = PunchyPeaks(Kicks(0.1f), 4, out _).Skip(250).Max();

		Assert.InRange(quiet, loud * 0.6, loud * 1.3);
	}

	[Fact]
	public void Punchy_SoftIntro_StaysSofterThanTheHeavyPart()
	{
		// Like a chill intro before the band kicks in: 4 s of soft kicks, then 2 s of loud ones.
		Func<int, float> soft = Kicks(0.03f);
		Func<int, float> loud = Kicks(0.8f);
		List<int> peaks = PunchyPeaks(i => i < 4 * 48000 ? soft(i) : loud(i), 6, out _);

		int intro = peaks.Skip(20).Take(355).Max();
		int heavy = peaks.Skip(375).Max();
		Assert.True(intro <= heavy * 0.5, $"intro {intro}, heavy {heavy}");
	}

	[Fact]
	public void Punchy_QuietPassage_AfterALoudOne_StaysQuiet()
	{
		Func<int, float> loud = Kicks(0.8f);
		Func<int, float> quiet = Kicks(0.1f);
		List<int> peaks = PunchyPeaks(i => i < 2 * 48000 ? loud(i) : quiet(i), 10, out _);

		int heavy = peaks.Take(187).Max();
		int calm = peaks.Skip(200).Max();
		Assert.True(calm <= heavy * 0.6, $"calm {calm}, heavy {heavy}");
	}

	[Fact]
	public void Punchy_PlaysOnTheSweetSpotCarrier_EvenForSubBass()
	{
		PunchyPeaks(i => (float)(0.8 * Math.Sin(2 * Math.PI * 35 * i / 48000)), 2, out List<sbyte> output);

		// Zero crossings over the last second: about 2 per cycle of the 100 Hz carrier, not of the 35 Hz input.
		List<sbyte> lastSecond = output.TakeLast(3000).ToList();
		int crossings = 0;
		for (int i = 1; i < lastSecond.Count; i++)
		{
			if ((lastSecond[i - 1] < 0) != (lastSecond[i] < 0))
			{
				crossings++;
			}
		}
		Assert.InRange(crossings, 180, 220);
	}

	[Fact]
	public void Punchy_Silence_StaysSilent()
	{
		List<int> peaks = PunchyPeaks(i => (float)(0.003 * Math.Sin(2 * Math.PI * 60 * i / 48000)), 1, out _);

		Assert.All(peaks, p => Assert.Equal(0, p));
	}

	[Fact]
	public void Punchy_KeepsThePanning_OneLevelForBothSides()
	{
		// Kicks panned left: full level on the left, a quarter on the right.
		HapticsAudioConverter converter = new(48000, 2) { Style = HapticsAudioStyle.Punchy };
		HapticsMixer mixer = new();
		Func<int, float> kick = Kicks(0.8f);
		float[] chunk = new float[960];
		sbyte[] left = new sbyte[32];
		sbyte[] right = new sbyte[32];
		int peakLeft = 0;
		int peakRight = 0;
		for (int start = 0; start < 48000 * 3; start += 480)
		{
			for (int i = 0; i < 480; i++)
			{
				float value = kick(start + i);
				chunk[2 * i] = value;
				chunk[2 * i + 1] = value / 4;
			}
			converter.Process(chunk, mixer);
			while (mixer.Buffered >= 32)
			{
				mixer.Fill(left, right);
				if (start > 48000)
				{
					peakLeft = Math.Max(peakLeft, left.Max(v => Math.Abs((int)v)));
					peakRight = Math.Max(peakRight, right.Max(v => Math.Abs((int)v)));
				}
			}
		}

		Assert.InRange(peakRight, peakLeft * 0.18, peakLeft * 0.32);
	}
}
