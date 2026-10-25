namespace DualPulse.Core.Audio;

public sealed record AudioDevice(string Id, string Name);

// Output devices whose sound can be captured, and capture sources for them (per platform, like ISystemAudioSource).
public interface ISystemAudioSourceFactory
{
	IReadOnlyList<AudioDevice> OutputDevices();

	// deviceId null: the default output device.
	ISystemAudioSource Create(string? deviceId);
}
