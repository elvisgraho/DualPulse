namespace DualPulse.Core.Audio;

// The system's default sound output, where apps and games play (per platform; on Windows the default render endpoint
// for the console and multimedia roles). Changed is raised on any thread. Set returns whether the system took it.
public interface IDefaultAudioOutput
{
	AudioDevice? Current { get; }

	bool Set(string deviceId);

	event Action? Changed;
}
