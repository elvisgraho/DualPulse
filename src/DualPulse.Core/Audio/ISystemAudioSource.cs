namespace DualPulse.Core.Audio;

// Interleaved float samples (-1..1) of what the system is playing.
public delegate void AudioBlock(ReadOnlySpan<float> interleaved);

// What the computer is playing (loopback of the default output device), for system-audio haptics. One implementation
// per platform: WASAPI loopback on Windows; PipeWire/PulseAudio monitor on Linux or Core Audio on macOS would be others.
public interface ISystemAudioSource : IDisposable
{
	// Format of the blocks; valid once Start has succeeded.
	int SampleRate { get; }

	int Channels { get; }

	// Raised on the capture thread.
	event AudioBlock? Blocks;

	// Returns false when there is no output device to capture.
	bool Start();

	void Stop();
}
