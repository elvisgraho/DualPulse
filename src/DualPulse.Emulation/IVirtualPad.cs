using DualPulse.Core.Protocol;

namespace DualPulse.Emulation;

public enum PadKind
{
	Xbox360,
	DualShock4,
	// DualPulse's own (USB/IP): games with native DualSense support drive triggers, lights, haptics and speaker themselves.
	DualSense
}

// Feedback from a game to the virtual pad. Large is the left (low-frequency) motor; Lightbar is DS4-only.
public readonly record struct RumbleFeedback(byte Large, byte Small, Rgb? Lightbar);

public interface IVirtualPad : IDisposable
{
	PadKind Kind { get; }

	// Called on the controller's read thread for every input report.
	void Submit(in InputSnapshot input);

	event Action<RumbleFeedback>? Feedback;
}

public interface IVirtualPadBackend
{
	bool IsAvailable { get; }

	string? UnavailableReason { get; }

	IVirtualPad Create(PadKind kind);

	// Whether this kind can be created now (a backend may serve some kinds and not others).
	bool IsAvailableFor(PadKind kind) => IsAvailable;
}

// Game audio from a virtual DualSense: interleaved 16-bit PCM at 48 kHz.
public delegate void GameAudio(ReadOnlySpan<short> samples, int channels);

// A virtual DualSense: besides input, it hands over what the game sends to it. OutputBlock is the 47-byte common
// output block (triggers, lights, audio control, ...); Haptics is the rear voice-coil pair (2 channels); Speaker is
// the 4-channel speaker stream (channels 1-2 speaker). Lost: the virtual device went away. Raised on background threads.
// Its microphone plays what WriteMicrophone is given.
public interface IDualSensePad : IVirtualPad
{
	event Action? Lost;

	// Lost already happened (possibly before anyone subscribed); stays true.
	bool IsLost { get; }

	event Action<byte[]>? OutputBlock;

	event GameAudio? Haptics;

	event GameAudio? Speaker;

	// True while the host records from the pad's microphone (raised when that changes).
	event Action<bool>? MicrophoneOpened;

	bool IsMicrophoneOpen { get; }

	// Interleaved 16-bit stereo at 48 kHz for the pad's microphone.
	void WriteMicrophone(ReadOnlySpan<short> stereo);

	// Diagnostics: microphone frames sent as silence (ran short), dropped (buffer full), and waiting now.
	(long ShortFrames, long DroppedFrames, int BufferedFrames) MicrophoneStats => default;
}

// Routes each pad kind to the backend that can create it: DualSense to the USB/IP one, the others to ViGEm.
public sealed class CompositePadBackend(IVirtualPadBackend classic, IVirtualPadBackend dualSense) : IVirtualPadBackend, IDisposable
{
	// The classic backend's availability, as the Emulation page reports it (ViGEmBus).
	public bool IsAvailable => classic.IsAvailable;

	public string? UnavailableReason => classic.UnavailableReason;

	public IVirtualPad Create(PadKind kind) => kind == PadKind.DualSense ? dualSense.Create(kind) : classic.Create(kind);

	public bool IsAvailableFor(PadKind kind) => kind == PadKind.DualSense ? dualSense.IsAvailableFor(kind) : classic.IsAvailableFor(kind);

	public void Dispose()
	{
		(classic as IDisposable)?.Dispose();
		(dualSense as IDisposable)?.Dispose();
	}
}
