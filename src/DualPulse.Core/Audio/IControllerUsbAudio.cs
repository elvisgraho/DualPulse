namespace DualPulse.Core.Audio;

// A real DualSense's own sound device while it is on USB, where a game sends its haptics (channels 3-4) and speaker
// sound (channels 1-2) natively. Per platform; Open returns null when there is none (not on USB, or no support).
public interface IControllerUsbAudio
{
	IControllerAudioStream? Open();
}

// Interleaved 16-bit samples at 48 kHz into the controller; written from the game audio thread.
public interface IControllerAudioStream : IDisposable
{
	void Write(ReadOnlySpan<short> samples, int channels);
}
