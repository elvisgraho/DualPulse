using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DualPulse.Emulation;

public interface IMicrophone
{
	// Whether Windows' default recording device is muted; null when there is none.
	bool? IsMuted { get; }

	// Flips the mute state; false when there is no recording device.
	bool Toggle();

	// Why the last read or toggle failed, for the log; null when it worked.
	string? LastError => null;

	// Raised when the mute state changes, from any source (Windows, another app, a headset button).
	event Action? Changed;
}

// The default recording device's mute through Windows Core Audio. The device is looked up again on every toggle, so a
// changed default is picked up at the next press. COM failures (no device, device unplugged) leave IsMuted null.
[SupportedOSPlatform("windows")]
public sealed class WindowsMicrophone : IMicrophone, IDisposable
{
	private const int ECapture = 1;

	private const int EConsole = 0;

	private const int ClsCtxAll = 0x17;

	private readonly object gate = new();

	private readonly VolumeCallback callback;

	private IAudioEndpointVolume? volume;

	private string? deviceId;

	public WindowsMicrophone()
	{
		callback = new VolumeCallback(this);
		lock (gate)
		{
			Refresh();
		}
	}

	public bool? IsMuted { get; private set; }

	public string? LastError { get; private set; }

	public event Action? Changed;

	public bool Toggle()
	{
		bool toggled = false;
		lock (gate)
		{
			Refresh();
			if (volume != null && IsMuted is { } muted)
			{
				try
				{
					Guid context = Guid.Empty;
					Marshal.ThrowExceptionForHR(volume.SetMute(!muted, ref context));
					IsMuted = !muted;
					toggled = true;
				}
				catch (Exception error)
				{
					LastError = error.Message;
					Release();
				}
			}
		}
		Changed?.Invoke();
		return toggled;
	}

	public void Dispose()
	{
		lock (gate)
		{
			Release();
		}
	}

	// Caller holds gate. Binds to the current default recording device (if it changed) and reads its mute state.
	private void Refresh()
	{
		try
		{
			// Created by class id and used through its interface only: Windows shares one enumerator object per process, and
			// once NAudio has wrapped it, casting it to a class of our own fails.
			IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(EnumeratorType)!;
			try
			{
				if (enumerator.GetDefaultAudioEndpoint(ECapture, EConsole, out IMMDevice device) != 0)
				{
					Release();
					IsMuted = null;
					return;
				}
				device.GetId(out string id);
				if (id != deviceId || volume == null)
				{
					Release();
					Guid iid = typeof(IAudioEndpointVolume).GUID;
					Marshal.ThrowExceptionForHR(device.Activate(ref iid, ClsCtxAll, 0, out object activated));
					volume = (IAudioEndpointVolume)activated;
					volume.RegisterControlChangeNotify(callback);
					deviceId = id;
				}
				Marshal.ReleaseComObject(device);
			}
			finally
			{
				Marshal.ReleaseComObject(enumerator);
			}
			Marshal.ThrowExceptionForHR(volume!.GetMute(out bool muted));
			IsMuted = muted;
			LastError = null;
		}
		catch (Exception error)
		{
			LastError = $"{error.GetType().Name}: {error.Message}";
			Release();
			IsMuted = null;
		}
	}

	// Caller holds gate.
	private void Release()
	{
		if (volume != null)
		{
			try
			{
				volume.UnregisterControlChangeNotify(callback);
				Marshal.ReleaseComObject(volume);
			}
			catch (Exception)
			{
			}
		}
		volume = null;
		deviceId = null;
	}

	private void OnNotify(bool muted)
	{
		lock (gate)
		{
			if (IsMuted == muted)
			{
				return;
			}
			IsMuted = muted;
		}
		Changed?.Invoke();
	}

	// Windows calls this on its own thread whenever the device's volume or mute changes.
	private sealed class VolumeCallback(WindowsMicrophone owner) : IAudioEndpointVolumeCallback
	{
		public int OnNotify(nint data)
		{
			try
			{
				// AUDIO_VOLUME_NOTIFICATION_DATA: a GUID (16 bytes), then BOOL bMuted.
				owner.OnNotify(Marshal.ReadInt32(data, 16) != 0);
			}
			catch (Exception)
			{
			}
			return 0;
		}
	}

	private static readonly Type EnumeratorType = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;

	[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDeviceEnumerator
	{
		[PreserveSig]
		int EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);

		[PreserveSig]
		int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
	}

	[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IMMDevice
	{
		[PreserveSig]
		int Activate(ref Guid iid, int clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

		[PreserveSig]
		int OpenPropertyStore(int access, out nint properties);

		[PreserveSig]
		int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
	}

	[ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioEndpointVolumeCallback
	{
		[PreserveSig]
		int OnNotify(nint data);
	}

	[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioEndpointVolume
	{
		[PreserveSig]
		int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

		[PreserveSig]
		int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

		[PreserveSig]
		int GetChannelCount(out uint count);

		[PreserveSig]
		int SetMasterVolumeLevel(float level, ref Guid context);

		[PreserveSig]
		int SetMasterVolumeLevelScalar(float level, ref Guid context);

		[PreserveSig]
		int GetMasterVolumeLevel(out float level);

		[PreserveSig]
		int GetMasterVolumeLevelScalar(out float level);

		[PreserveSig]
		int SetChannelVolumeLevel(uint channel, float level, ref Guid context);

		[PreserveSig]
		int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

		[PreserveSig]
		int GetChannelVolumeLevel(uint channel, out float level);

		[PreserveSig]
		int GetChannelVolumeLevelScalar(uint channel, out float level);

		[PreserveSig]
		int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

		[PreserveSig]
		int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
	}
}
