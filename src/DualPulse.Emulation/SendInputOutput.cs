using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DualPulse.Core.Mapping;

namespace DualPulse.Emulation;

// Mouse and keyboard input through Windows' SendInput. Windows doesn't let it reach windows of programs running as
// administrator (UIPI) unless DualPulse runs elevated too.
[SupportedOSPlatform("windows")]
public sealed partial class SendInputOutput : IPcOutput
{
	private const uint InputMouse = 0;

	private const uint InputKeyboard = 1;

	private const uint MouseMove = 0x0001;

	private const uint MouseLeftDown = 0x0002;

	private const uint MouseLeftUp = 0x0004;

	private const uint MouseRightDown = 0x0008;

	private const uint MouseRightUp = 0x0010;

	private const uint MouseMiddleDown = 0x0020;

	private const uint MouseMiddleUp = 0x0040;

	private const uint MouseWheel = 0x0800;

	private const int WheelDelta = 120;

	private const uint KeyExtended = 0x0001;

	private const uint KeyUp = 0x0002;

	public void MoveMouse(int dx, int dy)
	{
		Send(Mouse(dx, dy, 0, MouseMove));
	}

	public void MouseButton(MouseButton button, bool down)
	{
		uint flags = button switch
		{
			Core.Mapping.MouseButton.Right => down ? MouseRightDown : MouseRightUp,
			Core.Mapping.MouseButton.Middle => down ? MouseMiddleDown : MouseMiddleUp,
			_ => down ? MouseLeftDown : MouseLeftUp
		};
		Send(Mouse(0, 0, 0, flags));
	}

	public void Wheel(int ticks)
	{
		Send(Mouse(0, 0, unchecked((uint)(ticks * WheelDelta)), MouseWheel));
	}

	public void Key(PcKey key, bool down)
	{
		(ushort code, bool extended) = VirtualKey(key);
		uint flags = (extended ? KeyExtended : 0) | (down ? 0 : KeyUp);
		Send(new Input { Type = InputKeyboard, Data = new InputData { Keyboard = new KeyboardInput { VirtualKey = code, Flags = flags } } });
	}

	// Windows virtual-key code, and whether it is an extended key (arrows, navigation block, Windows key).
	public static (ushort Code, bool Extended) VirtualKey(PcKey key)
	{
		return key switch
		{
			PcKey.Enter => (0x0D, false),
			PcKey.Escape => (0x1B, false),
			PcKey.Space => (0x20, false),
			PcKey.Tab => (0x09, false),
			PcKey.Backspace => (0x08, false),
			PcKey.Delete => (0x2E, true),
			PcKey.Up => (0x26, true),
			PcKey.Down => (0x28, true),
			PcKey.Left => (0x25, true),
			PcKey.Right => (0x27, true),
			PcKey.PageUp => (0x21, true),
			PcKey.PageDown => (0x22, true),
			PcKey.Home => (0x24, true),
			PcKey.End => (0x23, true),
			PcKey.Windows => (0x5B, true),
			PcKey.Alt => (0x12, false),
			PcKey.Shift => (0x10, false),
			PcKey.Control => (0x11, false),
			PcKey.F11 => (0x7A, false),
			PcKey.PlayPause => (0xB3, false),
			PcKey.NextTrack => (0xB0, false),
			PcKey.PreviousTrack => (0xB1, false),
			PcKey.VolumeUp => (0xAF, false),
			PcKey.VolumeDown => (0xAE, false),
			PcKey.VolumeMute => (0xAD, false),
			_ => (0, false)
		};
	}

	private static Input Mouse(int dx, int dy, uint data, uint flags)
	{
		return new Input { Type = InputMouse, Data = new InputData { Mouse = new MouseInput { Dx = dx, Dy = dy, MouseData = data, Flags = flags } } };
	}

	private static void Send(Input input)
	{
		SendInput(1, [input], Marshal.SizeOf<Input>());
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Input
	{
		public uint Type;

		public InputData Data;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct InputData
	{
		[FieldOffset(0)]
		public MouseInput Mouse;

		[FieldOffset(0)]
		public KeyboardInput Keyboard;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MouseInput
	{
		public int Dx;

		public int Dy;

		public uint MouseData;

		public uint Flags;

		public uint Time;

		public IntPtr ExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KeyboardInput
	{
		public ushort VirtualKey;

		public ushort ScanCode;

		public uint Flags;

		public uint Time;

		public IntPtr ExtraInfo;
	}

	[LibraryImport("user32.dll", SetLastError = true)]
	private static partial uint SendInput(uint count, [In] Input[] inputs, int size);
}
