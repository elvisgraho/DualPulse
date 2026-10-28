namespace DualPulse.Core.Audio;

// A real DualSense's own sound device while it is on USB, where a game sends its haptics (channels 3-4) and speaker
// sound (channels 1-2) natively. Per platform; Open returns null when there is none (not on USB, or no support).
public interface IControllerUsbAudio
{
	IControllerAudioStream? Open();

	// The controller's own microphone, handed over as it records until disposed; null when there is none.
	IDisposable? OpenMicrophone(MicrophoneBlock blocks) => null;

	// The names Windows shows for the controller's own microphone and for the virtual DualSense's (null when absent).
	(string? Real, string? Virtual) MicrophoneNames() => (null, null);

	// The controller microphone's Windows input volume, 0-100; null when there is none.
	int? MicrophoneVolume() => null;

	bool SetMicrophoneVolume(int percent) => false;

	// Sets Windows' mute flag on the virtual DualSense's microphone; false when it isn't there.
	bool SetVirtualMicrophoneMuted(bool muted) => false;
}

// Interleaved 16-bit stereo at 48 kHz from a microphone; raised on its capture thread.
public delegate void MicrophoneBlock(ReadOnlySpan<short> stereo);

// Interleaved 16-bit samples at 48 kHz into the controller; written from the game audio thread.
public interface IControllerAudioStream : IDisposable
{
	void Write(ReadOnlySpan<short> samples, int channels);
}
