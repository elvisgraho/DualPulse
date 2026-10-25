using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using DualPulse.Core.Audio;

namespace DualPulse.Emulation;

// The Windows default render endpoint (multimedia role) and its changes. Setting it goes through IPolicyConfig, the
// interface Windows' own sound settings use (not in the public SDK; its layout is stable since Windows 7), for the
// console and multimedia roles; the communications default is left as the user set it.
[SupportedOSPlatform("windows")]
public sealed class WindowsDefaultAudioOutput : IDefaultAudioOutput, IDisposable
{
	private readonly MMDeviceEnumerator enumerator = new();

	private readonly Notifications notifications;

	public WindowsDefaultAudioOutput()
	{
		notifications = new Notifications(this);
		try
		{
			enumerator.RegisterEndpointNotificationCallback(notifications);
		}
		catch (COMException)
		{
			// No change notifications: Current and Set still work.
		}
	}

	public event Action? Changed;

	public AudioDevice? Current
	{
		get
		{
			try
			{
				using MMDevice device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
				return new AudioDevice(device.ID, device.FriendlyName);
			}
			catch (COMException)
			{
				// No output device at all.
				return null;
			}
		}
	}

	public bool Set(string deviceId)
	{
		object? client = null;
		try
		{
			client = new PolicyConfigClient();
			IPolicyConfig policy = (IPolicyConfig)client;
			Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, Role.Console));
			Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, Role.Multimedia));
			return true;
		}
		catch (Exception error) when (error is COMException or InvalidCastException)
		{
			// The device went away meanwhile, or the interface isn't there; the user can still switch by hand.
			return false;
		}
		finally
		{
			if (client != null)
			{
				Marshal.ReleaseComObject(client);
			}
		}
	}

	public void Dispose()
	{
		try
		{
			enumerator.UnregisterEndpointNotificationCallback(notifications);
		}
		catch (COMException)
		{
		}
		enumerator.Dispose();
	}

	// Windows calls these on its own threads and must not be blocked (setting the default from here can deadlock), so
	// Changed is raised from the thread pool.
	private sealed class Notifications(WindowsDefaultAudioOutput owner) : IMMNotificationClient
	{
		public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
		{
			if (flow == DataFlow.Render && role == Role.Multimedia)
			{
				Task.Run(() => owner.Changed?.Invoke());
			}
		}

		public void OnDeviceStateChanged(string deviceId, DeviceState newState)
		{
		}

		public void OnDeviceAdded(string pwstrDeviceId)
		{
		}

		public void OnDeviceRemoved(string deviceId)
		{
		}

		public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
		{
		}
	}

	[ComImport]
	[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
	private sealed class PolicyConfigClient;

	// Only SetDefaultEndpoint is used; the methods before it keep the vtable order.
	[ComImport]
	[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPolicyConfig
	{
		[PreserveSig]
		int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);

		[PreserveSig]
		int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int useDefault, IntPtr format);

		[PreserveSig]
		int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

		[PreserveSig]
		int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);

		[PreserveSig]
		int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int useDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);

		[PreserveSig]
		int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);

		[PreserveSig]
		int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

		[PreserveSig]
		int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

		[PreserveSig]
		int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int store, IntPtr key, IntPtr value);

		[PreserveSig]
		int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int store, IntPtr key, IntPtr value);

		[PreserveSig]
		int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, Role role);

		[PreserveSig]
		int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
	}
}
