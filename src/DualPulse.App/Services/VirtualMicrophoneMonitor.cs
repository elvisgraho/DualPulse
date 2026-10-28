using DualPulse.Core.Audio;

namespace DualPulse.App.Services;

// What the virtual DualSense's microphone is doing, for the Microphone page: whether something records from it and
// the last few seconds of what it gets. Written by the emulation service.
public sealed class VirtualMicrophoneMonitor
{
	private volatile bool inUse;

	public bool InUse => inUse;

	public PeakHistory Levels { get; } = new();

	internal void SetInUse(bool value)
	{
		inUse = value;
		if (!value)
		{
			Levels.Clear();
		}
	}
}
