using DualPulse.App.Services;
using DualPulse.Core.Audio;
using DualPulse.Core.Mapping;
using DualPulse.Core.Power;
using DualPulse.Emulation;

namespace DualPulse.App.Platform;

// Stand-ins for the OS services a platform doesn't have yet: each reports itself absent or does nothing, the same way
// the Windows ones behave when their driver or device is missing, so the services above them need no special case.
internal static class Unsupported
{
	public sealed class VirtualPadBackend : IVirtualPadBackend
	{
		public bool IsAvailable => false;

		public string? UnavailableReason => "Virtual controllers aren't supported on this operating system yet.";

		public IVirtualPad Create(PadKind kind) => throw new PlatformNotSupportedException(UnavailableReason);
	}

	public sealed class HidHideDriver : IHidHideDriver
	{
		public bool IsInstalled => false;

		public bool IsAttached => false;

		public IReadOnlyList<string> GetAllowedApps() => [];

		public void SetAllowedApps(IReadOnlyList<string> apps)
		{
		}

		public IReadOnlyList<string> GetHiddenDevices() => [];

		public void SetHiddenDevices(IReadOnlyList<string> devices)
		{
		}

		public bool GetActive() => false;

		public void SetActive(bool active)
		{
		}
	}

	public sealed class ForegroundWatcher : IForegroundWatcher
	{
		public string? ForegroundExe => null;

		// Never raised: per-game profiles stay on the default.
		public event Action<string?>? Changed
		{
			add { }
			remove { }
		}
	}

	public sealed class RunningWindows : IRunningWindows
	{
		public IReadOnlyList<RunningWindow> List(int excludePid) => [];
	}

	public sealed class PcOutput : IPcOutput
	{
		public void MoveMouse(int dx, int dy)
		{
		}

		public void MouseButton(MouseButton button, bool down)
		{
		}

		public void Wheel(int ticks)
		{
		}

		public void Key(PcKey key, bool down)
		{
		}
	}

	public sealed class Microphone : IMicrophone
	{
		public bool? IsMuted => null;

		public bool Toggle() => false;

		public event Action? Changed
		{
			add { }
			remove { }
		}
	}

	public sealed class SystemAudioSource : ISystemAudioSource
	{
		public int SampleRate => 0;

		public int Channels => 0;

		public event AudioBlock? Blocks
		{
			add { }
			remove { }
		}

		public bool Start() => false;

		public void Stop()
		{
		}

		public void Dispose()
		{
		}
	}

	// No output devices to capture, so controller audio has no source.
	public sealed class SteamClient : DualPulse.Core.Steam.ISteamClient
	{
		public bool IsRunning() => false;

		public bool InputActive() => false;

		public DualPulse.Core.Steam.SteamApp? ForegroundGame() => null;
	}

	public sealed class SystemAudioSourceFactory : ISystemAudioSourceFactory
	{
		public IReadOnlyList<AudioDevice> OutputDevices() => [];

		public ISystemAudioSource Create(string? deviceId) => new SystemAudioSource();
	}

	public sealed class ControllerUsbAudio : IControllerUsbAudio
	{
		public IControllerAudioStream? Open() => null;
	}

	public sealed class DefaultAudioOutput : IDefaultAudioOutput
	{
		public AudioDevice? Current => null;

		public bool Set(string deviceId) => false;

		public event Action? Changed
		{
			add { }
			remove { }
		}
	}

	public sealed class RunRegistry : IRunRegistry
	{
		public string? Get(string name) => null;

		public void Set(string name, string value)
		{
		}

		public void Delete(string name)
		{
		}
	}

	public sealed class BluetoothRadio : IBluetoothRadio
	{
		public bool Disconnect(long mac) => false;
	}
}
