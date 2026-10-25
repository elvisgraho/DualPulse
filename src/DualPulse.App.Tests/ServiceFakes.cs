using DualPulse.App.Services;
using DualPulse.Core.Protocol;
using DualPulse.Emulation;

namespace DualPulse.App.Tests;

internal sealed class FakeSettingsService(AppSettings? initial = null) : ISettingsService
{
	public AppSettings Current { get; private set; } = initial ?? new AppSettings();

	public event Action<AppSettings>? Changed;

	public void Update(Func<AppSettings, AppSettings> change)
	{
		Current = change(Current);
		Changed?.Invoke(Current);
	}
}

internal sealed class FakeControllerService : IControllerService
{
	public DualPulse.Core.Devices.ControllerSession? Session { get; set; }

	public string? OpenError { get; set; }

	public event Action? SessionChanged;

	public event Action<DualPulse.Core.Devices.ControllerSession>? SessionStarted;

	public event Action<DualPulse.Core.Devices.ControllerSession>? SessionEnded;

	public void RaiseStarted(DualPulse.Core.Devices.ControllerSession session)
	{
		Session = session;
		SessionStarted?.Invoke(session);
		SessionChanged?.Invoke();
	}

	public void RaiseEnded(DualPulse.Core.Devices.ControllerSession session)
	{
		Session = null;
		SessionEnded?.Invoke(session);
		SessionChanged?.Invoke();
	}
}

internal sealed class FakePcControlService : IPcControlService
{
	private bool active;

	public bool IsActive
	{
		get => active;
		set
		{
			active = value;
			ActiveChanged?.Invoke();
		}
	}

	public event Action? ActiveChanged;
}

internal sealed class FakePcOutput : DualPulse.Core.Mapping.IPcOutput
{
	private readonly object gate = new();

	private readonly List<string> events = [];

	public List<string> Events
	{
		get
		{
			lock (gate)
			{
				return events.ToList();
			}
		}
	}

	private int movedX;

	private int movedY;

	public (int X, int Y) Moved => (Volatile.Read(ref movedX), Volatile.Read(ref movedY));

	public void MoveMouse(int dx, int dy)
	{
		Interlocked.Add(ref movedX, dx);
		Interlocked.Add(ref movedY, dy);
	}

	public void MouseButton(DualPulse.Core.Mapping.MouseButton button, bool down) => Add($"{button} {(down ? "down" : "up")}");

	public void Wheel(int ticks)
	{
	}

	public void Key(DualPulse.Core.Mapping.PcKey key, bool down) => Add($"{key} {(down ? "down" : "up")}");

	private void Add(string text)
	{
		lock (gate)
		{
			events.Add(text);
		}
	}
}

internal sealed class FakeNotifier : INotifier
{
	public List<(string Title, string Message)> Shown { get; } = [];

	public int Count
	{
		get
		{
			lock (Shown)
			{
				return Shown.Count;
			}
		}
	}

	public TimeSpan? LastDuration { get; private set; }

	public void Show(string title, string message, TimeSpan? duration = null)
	{
		lock (Shown)
		{
			Shown.Add((title, message));
			LastDuration = duration;
		}
	}
}

internal sealed class FakeRunRegistry : IRunRegistry
{
	public Dictionary<string, string> Values { get; } = [];

	public bool Fail { get; set; }

	public string? Get(string name) => Check(() => Values.TryGetValue(name, out string? value) ? value : null);

	public void Set(string name, string value) => Check(() => Values[name] = value);

	public void Delete(string name) => Check(() => Values.Remove(name));

	private T Check<T>(Func<T> action) => Fail ? throw new UnauthorizedAccessException("Registry access denied") : action();
}

internal sealed class FakeModServer : IModServer
{
	public ModServerState State { get; set; }

	public int BoundPort { get; set; }

	public int PacketsReceived { get; set; }

	public int RejectedInstructions { get; set; }

	public DateTimeOffset? LastPacketAt { get; set; }

	public IReadOnlyList<string> RecentInstructions { get; set; } = [];

	public bool ModActive { get; set; }

	public int GiveBackCalls { get; private set; }

	public event Action? Changed;

	public void GiveControlBack() => GiveBackCalls++;

	public void RaiseChanged() => Changed?.Invoke();
}

internal sealed class FakeHidHideService : IHidHideService
{
	public HidHideStatus Status { get; set; }

	public event Action? StatusChanged;

	public void RaiseStatusChanged() => StatusChanged?.Invoke();
}

internal sealed class FakeHidHideDriver : DualPulse.Emulation.IHidHideDriver
{
	public bool Installed { get; set; } = true;

	public bool Attached { get; set; } = true;

	public bool Fail { get; set; }

	public List<string> AllowedApps { get; set; } = [];

	public List<string> HiddenDevices { get; set; } = [];

	public bool Active { get; set; }

	public List<string> Writes { get; } = [];

	public bool IsInstalled => Installed;

	public bool IsAttached => Attached;

	public IReadOnlyList<string> GetAllowedApps() => Check(AllowedApps.ToList());

	public void SetAllowedApps(IReadOnlyList<string> apps)
	{
		Check(0);
		Writes.Add("SetAllowedApps");
		AllowedApps = apps.ToList();
	}

	public IReadOnlyList<string> GetHiddenDevices() => Check(HiddenDevices.ToList());

	public void SetHiddenDevices(IReadOnlyList<string> devices)
	{
		Check(0);
		Writes.Add("SetHiddenDevices");
		HiddenDevices = devices.ToList();
	}

	public bool GetActive() => Check(Active);

	public void SetActive(bool active)
	{
		Check(0);
		Writes.Add("SetActive");
		Active = active;
	}

	private T Check<T>(T value) => Fail ? throw new IOException("HidHide driver error") : value;
}

internal sealed class FakeRumbleService : IRumbleService
{
	public List<(bool Left, bool Right)> Tests { get; } = [];

	public (byte Left, byte Right) Levels { get; set; }

	public void SetGameRumble(byte large, byte small)
	{
	}

	public void TestMotors(bool left, bool right)
	{
		Tests.Add((left, right));
	}
}

internal sealed class FakeEmulationService : IEmulationService
{
	public EmulationStatus Status { get; set; }

	public event Action? StatusChanged;

	public void RaiseStatusChanged() => StatusChanged?.Invoke();
}

internal sealed class FakePowerService : IPowerService
{
	public List<string> Requests { get; } = [];

	public bool PowerOffNow(string reason)
	{
		Requests.Add(reason);
		return true;
	}
}

internal sealed class FakePadBackend : IVirtualPadBackend
{
	public bool IsAvailable { get; set; } = true;

	public string? UnavailableReason => IsAvailable ? null : "ViGEmBus driver not installed";

	// The virtual DualSense has its own backend (usbip-win2), available or not apart from ViGEm.
	public bool DualSenseAvailable { get; set; } = true;

	public bool IsAvailableFor(PadKind kind) => IsAvailable && (kind != PadKind.DualSense || DualSenseAvailable);

	public List<FakePad> Created { get; } = [];

	public List<FakeDualSensePad> CreatedDualSense { get; } = [];

	// The host drops the next DualSense pads before anyone can subscribe to Lost.
	public bool CreateLost { get; set; }

	public IVirtualPad Create(PadKind kind)
	{
		if (!IsAvailable)
		{
			throw new InvalidOperationException(UnavailableReason);
		}
		if (kind == PadKind.DualSense)
		{
			FakeDualSensePad dualSense = new();
			if (CreateLost)
			{
				dualSense.Lose();
			}
			lock (CreatedDualSense)
			{
				CreatedDualSense.Add(dualSense);
			}
			return dualSense;
		}
		FakePad pad = new(kind);
		lock (Created)
		{
			Created.Add(pad);
		}
		return pad;
	}
}

internal sealed class FakePad(PadKind kind) : IVirtualPad
{
	private int submitted;

	public PadKind Kind { get; } = kind;

	public int Submitted => Volatile.Read(ref submitted);

	public bool Disposed { get; private set; }

	public event Action<RumbleFeedback>? Feedback;

	public InputSnapshot? LastSubmitted { get; private set; }

	public void Submit(in InputSnapshot input)
	{
		LastSubmitted = input;
		Interlocked.Increment(ref submitted);
	}

	public void Raise(RumbleFeedback feedback)
	{
		Feedback?.Invoke(feedback);
	}

	public void Dispose()
	{
		Disposed = true;
	}
}

internal sealed class FakeGyroService : IGyroService
{
	public int CalibrationsStarted { get; private set; }

	public GyroCalibrationState CalibrationState { get; private set; }

	public event Action? CalibrationChanged;

	public DualPulse.Core.Protocol.InputSnapshot ApplyToPad(DualPulse.Core.Protocol.InputSnapshot input) => input;

	public void StartCalibration()
	{
		CalibrationsStarted++;
		CalibrationState = GyroCalibrationState.Calibrating;
		CalibrationChanged?.Invoke();
	}

	public void Finish()
	{
		CalibrationState = GyroCalibrationState.Done;
		CalibrationChanged?.Invoke();
	}
}

internal sealed class FakeForegroundWatcher : DualPulse.Emulation.IForegroundWatcher
{
	public string? ForegroundExe { get; private set; }

	public event Action<string?>? Changed;

	public void Focus(string? exe)
	{
		ForegroundExe = exe;
		Changed?.Invoke(exe);
	}
}

internal sealed class FakeRunningWindows : DualPulse.Emulation.IRunningWindows
{
	public IReadOnlyList<DualPulse.Emulation.RunningWindow> Windows { get; set; } = [];

	public IReadOnlyList<DualPulse.Emulation.RunningWindow> List(int excludePid) => Windows;
}

internal sealed class FakeMicrophone : DualPulse.Emulation.IMicrophone
{
	public bool? IsMuted { get; set; } = false;

	public int Toggles { get; private set; }

	public event Action? Changed;

	public bool Toggle()
	{
		if (IsMuted is not { } muted)
		{
			return false;
		}
		Toggles++;
		IsMuted = !muted;
		Changed?.Invoke();
		return true;
	}

	public void SetFromWindows(bool? muted)
	{
		IsMuted = muted;
		Changed?.Invoke();
	}
}

internal sealed class FakeAudioSource : DualPulse.Core.Audio.ISystemAudioSource
{
	public bool Running { get; private set; }

	public int Starts { get; private set; }

	public int SampleRate => 48000;

	public int Channels => 2;

	public event DualPulse.Core.Audio.AudioBlock? Blocks;

	public bool Start()
	{
		Running = true;
		Starts++;
		return true;
	}

	public void Stop() => Running = false;

	public void Dispose() => Stop();

	public void Play(float[] interleaved) => Blocks?.Invoke(interleaved);
}

internal sealed class FakeAudioSourceFactory : DualPulse.Core.Audio.ISystemAudioSourceFactory
{
	public List<(string? DeviceId, FakeAudioSource Source)> Created { get; } = [];

	public IReadOnlyList<DualPulse.Core.Audio.AudioDevice> Devices { get; set; } = [new("dev-a", "Speakers"), new("dev-b", "Playback 7/8")];

	public IReadOnlyList<DualPulse.Core.Audio.AudioDevice> OutputDevices() => Devices;

	public DualPulse.Core.Audio.ISystemAudioSource Create(string? deviceId)
	{
		FakeAudioSource source = new();
		Created.Add((deviceId, source));
		return source;
	}
}

internal sealed class FakeDualSensePad : IDualSensePad
{
	private int submitted;

	public PadKind Kind => PadKind.DualSense;

	public int Submitted => Volatile.Read(ref submitted);

	public bool Disposed { get; private set; }

	public event Action<RumbleFeedback>? Feedback
	{
		add { }
		remove { }
	}

	public event Action? Lost;

	public event Action<byte[]>? OutputBlock;

	public event GameAudio? Haptics;

	public event GameAudio? Speaker;

	public bool IsLost { get; private set; }

	public void Lose()
	{
		IsLost = true;
		Lost?.Invoke();
	}

	public DualPulse.Core.Protocol.InputSnapshot? LastSubmitted { get; private set; }

	public void Submit(in DualPulse.Core.Protocol.InputSnapshot input)
	{
		LastSubmitted = input;
		Interlocked.Increment(ref submitted);
	}

	public void SendOutput(byte[] block) => OutputBlock?.Invoke(block);

	public void SendHaptics(short[] samples) => Haptics?.Invoke(samples, 2);

	public void SendSpeaker(short[] samples) => Speaker?.Invoke(samples, 4);

	public void Dispose() => Disposed = true;
}

