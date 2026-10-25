using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DualPulse.Core.Power;

namespace DualPulse.Emulation;

// Drops the link with IOCTL_BTH_DISCONNECT_DEVICE, trying each local Bluetooth radio until one accepts it.
[SupportedOSPlatform("windows")]
public sealed partial class WindowsBluetoothRadio : IBluetoothRadio
{
	private const uint IoctlBthDisconnectDevice = 0x41000C;

	public bool Disconnect(long mac)
	{
		FindRadioParams findParams = new() { Size = Marshal.SizeOf<FindRadioParams>() };
		IntPtr find = BluetoothFindFirstRadio(ref findParams, out IntPtr radio);
		if (find == IntPtr.Zero)
		{
			return false;
		}
		try
		{
			do
			{
				long address = mac;
				bool disconnected = DeviceIoControl(radio, IoctlBthDisconnectDevice, ref address, sizeof(long), IntPtr.Zero, 0, out _, IntPtr.Zero);
				CloseHandle(radio);
				if (disconnected)
				{
					return true;
				}
			}
			while (BluetoothFindNextRadio(find, out radio));
			return false;
		}
		finally
		{
			BluetoothFindRadioClose(find);
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct FindRadioParams
	{
		public int Size;
	}

	[LibraryImport("bthprops.cpl", SetLastError = true)]
	private static partial IntPtr BluetoothFindFirstRadio(ref FindRadioParams parameters, out IntPtr radio);

	[LibraryImport("bthprops.cpl", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool BluetoothFindNextRadio(IntPtr find, out IntPtr radio);

	[LibraryImport("bthprops.cpl")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool BluetoothFindRadioClose(IntPtr find);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool DeviceIoControl(IntPtr device, uint controlCode, ref long input, int inputSize, IntPtr output, int outputSize, out int bytesReturned, IntPtr overlapped);

	[LibraryImport("kernel32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool CloseHandle(IntPtr handle);
}
