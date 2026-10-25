using System.Buffers.Binary;

namespace DualPulse.Core.Protocol;

// Writes a snapshot as a DualSense USB input report 0x01 (64 bytes, data from byte 1), the inverse of
// DualSenseInputParser: what a virtual DualSense reports to games. Counter is the report sequence byte.
public static class UsbInputReport
{
	public const int Length = 64;

	public static int Build(in InputSnapshot snapshot, byte counter, Span<byte> destination)
	{
		if (destination.Length < Length)
		{
			throw new ArgumentException($"Destination must be at least {Length} bytes.", nameof(destination));
		}
		Span<byte> report = destination[..Length];
		report.Clear();
		report[0] = DualSenseInputParser.UsbReportId;
		Span<byte> d = report[1..];
		d[0] = snapshot.LeftX;
		d[1] = snapshot.LeftY;
		d[2] = snapshot.RightX;
		d[3] = snapshot.RightY;
		d[4] = snapshot.L2;
		d[5] = snapshot.R2;
		d[6] = counter;
		uint buttons = (uint)snapshot.Buttons;
		d[7] = (byte)((uint)Hat(snapshot.Buttons) | ((buttons & 0x0F) << 4));
		d[8] = (byte)(buttons >> 4);
		d[9] = (byte)(((buttons >> 12) & 0x07) | (((buttons >> 19) & 0x0F) << 4));
		BinaryPrimitives.WriteInt16LittleEndian(d[15..], snapshot.GyroX);
		BinaryPrimitives.WriteInt16LittleEndian(d[17..], snapshot.GyroY);
		BinaryPrimitives.WriteInt16LittleEndian(d[19..], snapshot.GyroZ);
		BinaryPrimitives.WriteInt16LittleEndian(d[21..], snapshot.AccelX);
		BinaryPrimitives.WriteInt16LittleEndian(d[23..], snapshot.AccelY);
		BinaryPrimitives.WriteInt16LittleEndian(d[25..], snapshot.AccelZ);
		BinaryPrimitives.WriteUInt32LittleEndian(d[27..], snapshot.SensorTimestamp);
		WriteTouch(snapshot.Touch0, d.Slice(32, 4));
		WriteTouch(snapshot.Touch1, d.Slice(36, 4));
		d[52] = snapshot.BatteryStatus;
		d[53] = snapshot.HeadphonesPlugged ? (byte)0x01 : (byte)0;
		return Length;
	}

	// D-pad hat: 0-7 clockwise from up, 8 released.
	private static int Hat(Buttons buttons)
	{
		bool up = buttons.HasFlag(Buttons.DpadUp);
		bool down = buttons.HasFlag(Buttons.DpadDown);
		bool left = buttons.HasFlag(Buttons.DpadLeft);
		bool right = buttons.HasFlag(Buttons.DpadRight);
		return (up, right, down, left) switch
		{
			(true, false, false, false) => 0,
			(true, true, false, false) => 1,
			(false, true, false, false) => 2,
			(false, true, true, false) => 3,
			(false, false, true, false) => 4,
			(false, false, true, true) => 5,
			(false, false, false, true) => 6,
			(true, false, false, true) => 7,
			_ => 8
		};
	}

	private static void WriteTouch(TouchPoint touch, Span<byte> t)
	{
		t[0] = (byte)((touch.Active ? 0 : 0x80) | (touch.Id & 0x7F));
		t[1] = (byte)touch.X;
		t[2] = (byte)(((touch.X >> 8) & 0x0F) | ((touch.Y & 0x0F) << 4));
		t[3] = (byte)(touch.Y >> 4);
	}
}
