using System.Buffers.Binary;

namespace DualPulse.Core.Protocol;

// Parses DualSense input reports: USB 0x01 (64 bytes, data from byte 1) and Bluetooth 0x31 (78 bytes, data from
// byte 2). Field layout per docs/bluetooth-report-audit.md, verified against reports captured from hardware.
public static class DualSenseInputParser
{
	public const byte UsbReportId = 0x01;

	public const byte BluetoothReportId = 0x31;

	// D-pad hat values 0-7, clockwise from up; 8 means released.
	private static readonly Buttons[] HatDirections =
	[
		Buttons.DpadUp,
		Buttons.DpadUp | Buttons.DpadRight,
		Buttons.DpadRight,
		Buttons.DpadDown | Buttons.DpadRight,
		Buttons.DpadDown,
		Buttons.DpadDown | Buttons.DpadLeft,
		Buttons.DpadLeft,
		Buttons.DpadUp | Buttons.DpadLeft
	];

	public static bool TryParse(ReadOnlySpan<byte> report, out InputSnapshot snapshot)
	{
		snapshot = default;
		Transport transport;
		if (report.Length >= 78 && report[0] == BluetoothReportId)
		{
			transport = Transport.Bluetooth;
		}
		else if (report.Length >= 64 && report[0] == UsbReportId)
		{
			transport = Transport.Usb;
		}
		else
		{
			return false;
		}
		ReadOnlySpan<byte> d = report[(transport == Transport.Bluetooth ? 2 : 1)..];
		snapshot = new InputSnapshot(
			transport,
			LeftX: d[0],
			LeftY: d[1],
			RightX: d[2],
			RightY: d[3],
			L2: d[4],
			R2: d[5],
			Buttons: ParseButtons(d[7], d[8], d[9]),
			GyroX: BinaryPrimitives.ReadInt16LittleEndian(d[15..]),
			GyroY: BinaryPrimitives.ReadInt16LittleEndian(d[17..]),
			GyroZ: BinaryPrimitives.ReadInt16LittleEndian(d[19..]),
			AccelX: BinaryPrimitives.ReadInt16LittleEndian(d[21..]),
			AccelY: BinaryPrimitives.ReadInt16LittleEndian(d[23..]),
			AccelZ: BinaryPrimitives.ReadInt16LittleEndian(d[25..]),
			SensorTimestamp: BinaryPrimitives.ReadUInt32LittleEndian(d[27..]),
			Touch0: ParseTouch(d.Slice(32, 4)),
			Touch1: ParseTouch(d.Slice(36, 4)),
			BatteryStatus: d[52],
			BluetoothSequence: transport == Transport.Bluetooth ? report[1] >> 4 : null,
			HeadphonesPlugged: (d[53] & 0x01) != 0);
		return true;
	}

	private static Buttons ParseButtons(byte b0, byte b1, byte b2)
	{
		Buttons buttons = Buttons.None;
		int hat = b0 & 0x0F;
		if (hat < HatDirections.Length)
		{
			buttons |= HatDirections[hat];
		}
		// High nibble of b0: Square, Cross, Circle, Triangle -> bits 0-3 of Buttons.
		buttons |= (Buttons)((b0 >> 4) & 0x0F);
		// b1: L1, R1, L2, R2, Create, Options, L3, R3 -> bits 4-11.
		buttons |= (Buttons)((uint)b1 << 4);
		// b2 bits 0-2: Ps, Touchpad, Mute -> bits 12-14.
		buttons |= (Buttons)((uint)(b2 & 0x07) << 12);
		// b2 bits 4-7: Fn1, Fn2, LeftPaddle, RightPaddle (DualSense Edge) -> bits 19-22.
		buttons |= (Buttons)((uint)(b2 >> 4) << 19);
		return buttons;
	}

	private static TouchPoint ParseTouch(ReadOnlySpan<byte> t)
	{
		return new TouchPoint(
			Active: (t[0] & 0x80) == 0,
			Id: (byte)(t[0] & 0x7F),
			X: (ushort)(((t[2] & 0x0F) << 8) | t[1]),
			Y: (ushort)((t[3] << 4) | (t[2] >> 4)));
	}
}
