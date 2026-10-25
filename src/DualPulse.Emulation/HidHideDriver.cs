using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DualPulse.Emulation;

// HidHide's control device (\\.\HidHide). Control codes are CTL_CODE(32769, 2048-2053, METHOD_BUFFERED,
// FILE_READ_DATA), from HidHide's public IOCTL contract (MIT). The driver allows one handle at a time, so each call
// opens and closes its own. No elevation is needed.
[SupportedOSPlatform("windows")]
public sealed partial class HidHideDriver : IHidHideDriver
{
	private const string DevicePath = @"\\.\HidHide";

	private const uint GetWhitelist = (32769u << 16) | (1u << 14) | (2048u << 2);

	private const uint SetWhitelist = (32769u << 16) | (1u << 14) | (2049u << 2);

	private const uint GetBlacklist = (32769u << 16) | (1u << 14) | (2050u << 2);

	private const uint SetBlacklist = (32769u << 16) | (1u << 14) | (2051u << 2);

	private const uint GetActiveCode = (32769u << 16) | (1u << 14) | (2052u << 2);

	private const uint SetActiveCode = (32769u << 16) | (1u << 14) | (2053u << 2);

	// The HIDClass device setup class, where HidHide registers itself as an upper filter.
	private const string HidClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{745a17a0-74d3-11d0-b6fe-00a0c90f57da}";

	private const int ErrorFileNotFound = 2;

	private const int ErrorInsufficientBuffer = 122;

	private const int ErrorMoreData = 234;

	public bool IsInstalled
	{
		get
		{
			using SafeFileHandle handle = Open();
			return !handle.IsInvalid || Marshal.GetLastPInvokeError() != ErrorFileNotFound;
		}
	}

	public bool IsAttached
	{
		get
		{
			using Microsoft.Win32.RegistryKey? hidClass = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(HidClassKey);
			return hidClass?.GetValue("UpperFilters") is string[] filters && filters.Contains("HidHide", StringComparer.OrdinalIgnoreCase);
		}
	}

	public IReadOnlyList<string> GetAllowedApps() => HidHideEncoding.FromMultiSz(Get(GetWhitelist));

	public void SetAllowedApps(IReadOnlyList<string> apps) => Set(SetWhitelist, HidHideEncoding.ToMultiSz(apps));

	public IReadOnlyList<string> GetHiddenDevices() => HidHideEncoding.FromMultiSz(Get(GetBlacklist));

	public void SetHiddenDevices(IReadOnlyList<string> devices) => Set(SetBlacklist, HidHideEncoding.ToMultiSz(devices));

	// The driver expects an output buffer of exactly one BOOLEAN here.
	public bool GetActive()
	{
		using SafeFileHandle handle = OpenOrThrow();
		byte[] buffer = new byte[1];
		if (!DeviceIoControl(handle, GetActiveCode, null, 0, buffer, buffer.Length, out _, IntPtr.Zero))
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "HidHide request failed");
		}
		return buffer[0] != 0;
	}

	public void SetActive(bool active) => Set(SetActiveCode, [active ? (byte)1 : (byte)0]);

	// The device instance id ("HID\VID_054C&PID_0CE6&MI_03\...") of a HID device interface path, as HidHide expects.
	public static string? InstanceIdOf(string interfacePath)
	{
		DevPropKey key = new() { FormatId = new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), PropertyId = 256 };
		byte[] buffer = new byte[1024];
		uint size = (uint)buffer.Length;
		int result = CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, buffer, ref size, 0);
		return result == 0 ? HidHideEncoding.FromMultiSz(buffer.AsSpan(0, (int)size)).FirstOrDefault() : null;
	}

	// \Device\HarddiskVolumeN\... for a drive-letter path.
	public static string? DosDeviceNameOf(string fullPath)
	{
		return HidHideEncoding.ToDosDeviceName(fullPath, drive =>
		{
			char[] target = new char[1024];
			int length = QueryDosDeviceW(drive, target, target.Length);
			if (length == 0)
			{
				return null;
			}
			int end = Array.IndexOf(target, '\0');
			return new string(target, 0, end >= 0 ? end : length);
		});
	}

	private static SafeFileHandle Open()
	{
		const uint GenericRead = 0x80000000;
		const uint ShareAll = 0x1 | 0x2 | 0x4;
		const uint OpenExisting = 3;
		return CreateFileW(DevicePath, GenericRead, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
	}

	private static SafeFileHandle OpenOrThrow()
	{
		SafeFileHandle handle = Open();
		if (handle.IsInvalid)
		{
			int error = Marshal.GetLastPInvokeError();
			handle.Dispose();
			throw new Win32Exception(error, "Can't open the HidHide driver");
		}
		return handle;
	}

	// Lists are read in two steps, as the driver requires: a call without a buffer reports the size, then a buffer of
	// exactly that size is filled. (A larger buffer is rejected once the list is non-empty.)
	private static byte[] Get(uint code)
	{
		using SafeFileHandle handle = OpenOrThrow();
		if (!DeviceIoControl(handle, code, null, 0, null, 0, out int required, IntPtr.Zero))
		{
			int error = Marshal.GetLastPInvokeError();
			if (error != ErrorInsufficientBuffer && error != ErrorMoreData)
			{
				throw new Win32Exception(error, "HidHide size request failed");
			}
		}
		if (required <= 0)
		{
			return [];
		}
		byte[] buffer = new byte[required];
		if (!DeviceIoControl(handle, code, null, 0, buffer, buffer.Length, out int returned, IntPtr.Zero))
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "HidHide request failed");
		}
		return buffer[..returned];
	}

	private static void Set(uint code, byte[] input)
	{
		using SafeFileHandle handle = OpenOrThrow();
		if (!DeviceIoControl(handle, code, input, input.Length, null, 0, out _, IntPtr.Zero))
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "HidHide update failed");
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DevPropKey
	{
		public Guid FormatId;

		public uint PropertyId;
	}

	[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	private static partial SafeFileHandle CreateFileW(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputSize, byte[]? output, int outputSize, out int returned, IntPtr overlapped);

	[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	private static partial int QueryDosDeviceW(string deviceName, [Out] char[] targetPath, int max);

	[LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int CM_Get_Device_Interface_PropertyW(string deviceInterface, ref DevPropKey key, out uint propertyType, [Out] byte[] buffer, ref uint size, uint flags);
}
