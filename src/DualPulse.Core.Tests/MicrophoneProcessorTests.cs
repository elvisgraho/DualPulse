using DualPulse.Core.Audio;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class MicrophoneProcessorTests
{
	private const int Frame = MicrophoneProcessor.FrameLength;

	private readonly FakeSuppressor suppressor = new();

	private static short[] Stereo(int frames, short value) => Enumerable.Repeat(value, frames * 2).ToArray();

	private static short[] Run(MicrophoneProcessor processor, short[] stereo)
	{
		List<short> output = [];
		processor.Process(stereo, block => output.AddRange(block));
		return [.. output];
	}

	private MicrophoneProcessor Create(bool suppression = false, bool gate = false, int boostDb = 0) =>
		new(() => suppressor) { NoiseSuppression = suppression, VoiceGate = gate, BoostDb = boostDb };

	[Fact]
	public void AllOff_PassesThroughAtOnce()
	{
		short[] input = [1, 2, 3, 4];

		Assert.Equal(input, Run(Create(), input));
		Assert.Equal(0, suppressor.Frames);
	}

	[Fact]
	public void NoiseSuppression_GoesThroughTheSuppressor_In10msMonoFrames()
	{
		MicrophoneProcessor processor = Create(suppression: true);

		short[] partial = Run(processor, Stereo(Frame - 1, 1000));
		short[] output = Run(processor, Stereo(1, 1000));

		Assert.Empty(partial);
		Assert.Equal(1, suppressor.Frames);
		// The fake halves the sound; the mono result goes to both sides.
		Assert.Equal(Stereo(Frame, 500), output);
	}

	[Fact]
	public void VoiceGate_SilentWithoutSpeech()
	{
		suppressor.Voice = 0.05f;

		short[] output = Run(Create(gate: true), Stereo(Frame * 3, 1000));

		Assert.All(output, s => Assert.Equal(0, s));
	}

	[Fact]
	public void VoiceGate_OpensForSpeech_WithoutTheSuppressorChangingTheSound()
	{
		// Gate only: RNNoise just listens for speech; the sound itself stays as it was.
		suppressor.Voice = 0.95f;
		MicrophoneProcessor processor = Create(gate: true);
		Run(processor, Stereo(Frame, 1000));

		short[] output = Run(processor, Stereo(Frame, 1000));

		Assert.All(output, s => Assert.Equal(1000, s));
	}

	[Fact]
	public void VoiceGate_StaysOpenBriefly_AfterSpeech()
	{
		MicrophoneProcessor processor = Create(gate: true);
		suppressor.Voice = 0.95f;
		Run(processor, Stereo(Frame * 2, 1000));

		suppressor.Voice = 0.05f;
		short[] pause = Run(processor, Stereo(Frame * 10, 1000));
		short[] later = Run(processor, Stereo(Frame * 40, 1000));

		Assert.All(pause, s => Assert.Equal(1000, s));
		Assert.All(later[^(Frame * 2)..], s => Assert.Equal(0, s));
	}

	[Fact]
	public void Boost_6dB_DoublesQuietSound()
	{
		short[] output = Run(Create(boostDb: 6), Stereo(4, 1000));

		Assert.All(output, s => Assert.InRange(s, 1990, 2000));
	}

	[Fact]
	public void Boost_NeverClips()
	{
		short[] output = Run(Create(boostDb: 12), Stereo(4, 30000));

		Assert.All(output, s => Assert.InRange(s, 30000, short.MaxValue));
		Assert.All(output, s => Assert.NotEqual(short.MaxValue, s));
	}

	[Fact]
	public void WithoutRNNoise_SuppressionAndGateDoNothing()
	{
		MicrophoneProcessor processor = new(() => null) { NoiseSuppression = true, VoiceGate = true };

		Assert.Equal(Stereo(2, 1000), Run(processor, Stereo(2, 1000)));
	}

	private sealed class FakeSuppressor : INoiseSuppressor
	{
		public int Frames { get; private set; }

		public float Voice { get; set; } = 0.95f;

		public float Process(Span<float> frame)
		{
			Assert.Equal(Frame, frame.Length);
			Frames++;
			for (int i = 0; i < frame.Length; i++)
			{
				frame[i] /= 2;
			}
			return Voice;
		}

		public void Dispose()
		{
		}
	}
}
