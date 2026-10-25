using System.Buffers.Binary;
using System.Text;

namespace DualPulse.Core.UsbIp;

public enum UsbIpSpeed : uint
{
	Low = 1,
	Full = 2,
	High = 3
}

// A device as USB/IP describes it in device lists and import replies. Interfaces: class, subclass, protocol.
public sealed record UsbIpExport(string Path, string BusId, uint BusNumber, uint DeviceNumber, UsbIpSpeed Speed, ushort VendorId, ushort ProductId,
	ushort BcdDevice, byte DeviceClass, byte DeviceSubClass, byte DeviceProtocol, byte ConfigurationValue, byte ConfigurationCount,
	IReadOnlyList<(byte Class, byte SubClass, byte Protocol)> Interfaces);

public readonly record struct UsbIpOperation(ushort Version, ushort Code, uint Status);

public abstract record UrbCommand(uint Seqnum);

// One transfer the host submits. In: device to host. Setup is the 8-byte control setup (endpoint 0). Data is what
// the host sends (OUT). IsoPackets: offset and length of each isochronous packet within the buffer.
public sealed record UrbSubmit(uint Seqnum, uint DeviceId, bool In, int Endpoint, uint TransferFlags, int Length, int StartFrame,
	int NumberOfPackets, int Interval, byte[] Setup, byte[] Data, (int Offset, int Length)[] IsoPackets) : UrbCommand(Seqnum)
{
	public bool IsIsochronous => IsoPackets.Length > 0;
}

public sealed record UrbUnlink(uint Seqnum, uint Target) : UrbCommand(Seqnum);

// How a transfer ends: status 0 with the data sent back (IN), or an error such as a stall (-EPIPE). StartFrame: the
// frame an isochronous transfer began on (the host schedules the next one from it); null echoes the request's.
public sealed record UrbResult(int Status, byte[] Data)
{
	public int? StartFrame { get; init; }

	public static readonly UrbResult Stall = new(-32, []);

	public static UrbResult Ok(byte[] data) => new(0, data);
}

// The USB/IP wire protocol (Linux kernel documentation, usbip_protocol.rst), all big-endian:
// - Operations (before import): version 0x0111, code, status; device list and import carry 312-byte device records.
// - After import, URBs: 48-byte headers (command, seqnum, devid, direction, endpoint, then submit or unlink fields),
//   OUT data after a submit, IN data after its return, and 16-byte isochronous packet descriptors after both.
public static class UsbIpProtocol
{
	public const ushort Version = 0x0111;

	public const ushort RequestDeviceList = 0x8005;

	public const ushort ReplyDeviceList = 0x0005;

	public const ushort RequestImport = 0x8003;

	public const ushort ReplyImport = 0x0003;

	public const int BusIdLength = 32;

	public const int Econnreset = -104;

	private const uint CommandSubmit = 1;

	private const uint CommandUnlink = 2;

	private const uint ReturnSubmit = 3;

	private const uint ReturnUnlink = 4;

	private const int HeaderLength = 48;

	private const int DeviceRecordLength = 312;

	private const int PathLength = 256;

	public static async ValueTask<UsbIpOperation?> ReadOperationAsync(Stream stream, CancellationToken cancellationToken)
	{
		byte[] header = new byte[8];
		if (!await ReadFullyAsync(stream, header, cancellationToken).ConfigureAwait(false))
		{
			return null;
		}
		return new UsbIpOperation(BinaryPrimitives.ReadUInt16BigEndian(header), BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2)),
			BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)));
	}

	public static async ValueTask<string?> ReadBusIdAsync(Stream stream, CancellationToken cancellationToken)
	{
		byte[] busId = new byte[BusIdLength];
		return await ReadFullyAsync(stream, busId, cancellationToken).ConfigureAwait(false) ? Encoding.ASCII.GetString(busId).TrimEnd('\0') : null;
	}

	public static void WriteDeviceList(Stream stream, IReadOnlyList<UsbIpExport> devices)
	{
		Span<byte> count = stackalloc byte[4];
		WriteOperation(stream, ReplyDeviceList, 0);
		BinaryPrimitives.WriteUInt32BigEndian(count, (uint)devices.Count);
		stream.Write(count);
		foreach (UsbIpExport device in devices)
		{
			WriteDevice(stream, device);
			foreach ((byte @class, byte subClass, byte protocol) in device.Interfaces)
			{
				stream.Write([@class, subClass, protocol, 0]);
			}
		}
	}

	// A null device answers an unknown bus id with an error status.
	public static void WriteImportReply(Stream stream, UsbIpExport? device)
	{
		WriteOperation(stream, ReplyImport, device == null ? 1u : 0u);
		if (device != null)
		{
			WriteDevice(stream, device);
		}
	}

	// The next submit or unlink, or null when the host has closed the connection.
	public static async ValueTask<UrbCommand?> ReadCommandAsync(Stream stream, CancellationToken cancellationToken)
	{
		byte[] header = new byte[HeaderLength];
		if (!await ReadFullyAsync(stream, header, cancellationToken).ConfigureAwait(false))
		{
			return null;
		}
		uint command = BinaryPrimitives.ReadUInt32BigEndian(header);
		uint seqnum = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
		if (command == CommandUnlink)
		{
			return new UrbUnlink(seqnum, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20)));
		}
		if (command != CommandSubmit)
		{
			throw new InvalidDataException($"Unknown USB/IP command {command}.");
		}
		uint deviceId = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
		bool @in = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12)) == 1;
		int endpoint = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16));
		uint flags = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20));
		int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(24));
		int startFrame = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(28));
		int packets = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(32));
		int interval = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(36));
		byte[] setup = header[40..48];
		byte[] data = [];
		if (!@in && length > 0)
		{
			data = new byte[length];
			if (!await ReadFullyAsync(stream, data, cancellationToken).ConfigureAwait(false))
			{
				return null;
			}
		}
		// Non-isochronous transfers carry 0 (or -1) packets.
		(int, int)[] iso = [];
		if (packets > 0)
		{
			byte[] descriptors = new byte[packets * 16];
			if (!await ReadFullyAsync(stream, descriptors, cancellationToken).ConfigureAwait(false))
			{
				return null;
			}
			iso = new (int, int)[packets];
			for (int i = 0; i < packets; i++)
			{
				iso[i] = (BinaryPrimitives.ReadInt32BigEndian(descriptors.AsSpan(i * 16)), BinaryPrimitives.ReadInt32BigEndian(descriptors.AsSpan(i * 16 + 4)));
			}
		}
		return new UrbSubmit(seqnum, deviceId, @in, endpoint, flags, length, startFrame, Math.Max(packets, 0), interval, setup, data, iso);
	}

	// Completes a submit. Isochronous: every packet is reported done in full (OUT: accepted; IN: result.Data holds
	// the whole buffer, packet after packet).
	public static void WriteReturnSubmit(Stream stream, UrbSubmit submit, UrbResult result)
	{
		byte[] data = submit.In ? result.Data : [];
		int actual = submit.IsIsochronous
			? (result.Status == 0 ? submit.IsoPackets.Sum(p => p.Length) : 0)
			: (submit.In ? data.Length : (result.Status == 0 ? submit.Data.Length : 0));
		byte[] header = new byte[HeaderLength];
		BinaryPrimitives.WriteUInt32BigEndian(header, ReturnSubmit);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), submit.Seqnum);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), result.Status);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(24), actual);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(28), result.StartFrame ?? submit.StartFrame);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(32), submit.IsIsochronous ? submit.IsoPackets.Length : 0);
		stream.Write(header);
		if (submit.In && data.Length > 0 && result.Status == 0)
		{
			stream.Write(data);
		}
		if (submit.IsIsochronous)
		{
			byte[] descriptors = new byte[submit.IsoPackets.Length * 16];
			for (int i = 0; i < submit.IsoPackets.Length; i++)
			{
				(int offset, int length) = submit.IsoPackets[i];
				BinaryPrimitives.WriteInt32BigEndian(descriptors.AsSpan(i * 16), offset);
				BinaryPrimitives.WriteInt32BigEndian(descriptors.AsSpan(i * 16 + 4), length);
				BinaryPrimitives.WriteInt32BigEndian(descriptors.AsSpan(i * 16 + 8), result.Status == 0 ? length : 0);
			}
			stream.Write(descriptors);
		}
	}

	public static void WriteReturnUnlink(Stream stream, uint seqnum, int status)
	{
		byte[] header = new byte[HeaderLength];
		BinaryPrimitives.WriteUInt32BigEndian(header, ReturnUnlink);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), seqnum);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), status);
		stream.Write(header);
	}

	private static void WriteOperation(Stream stream, ushort code, uint status)
	{
		Span<byte> header = stackalloc byte[8];
		BinaryPrimitives.WriteUInt16BigEndian(header, Version);
		BinaryPrimitives.WriteUInt16BigEndian(header[2..], code);
		BinaryPrimitives.WriteUInt32BigEndian(header[4..], status);
		stream.Write(header);
	}

	private static void WriteDevice(Stream stream, UsbIpExport device)
	{
		byte[] record = new byte[DeviceRecordLength];
		Encoding.ASCII.GetBytes(device.Path, record.AsSpan(0, Math.Min(device.Path.Length, PathLength - 1)));
		Encoding.ASCII.GetBytes(device.BusId, record.AsSpan(PathLength, Math.Min(device.BusId.Length, BusIdLength - 1)));
		Span<byte> rest = record.AsSpan(PathLength + BusIdLength);
		BinaryPrimitives.WriteUInt32BigEndian(rest, device.BusNumber);
		BinaryPrimitives.WriteUInt32BigEndian(rest[4..], device.DeviceNumber);
		BinaryPrimitives.WriteUInt32BigEndian(rest[8..], (uint)device.Speed);
		BinaryPrimitives.WriteUInt16BigEndian(rest[12..], device.VendorId);
		BinaryPrimitives.WriteUInt16BigEndian(rest[14..], device.ProductId);
		BinaryPrimitives.WriteUInt16BigEndian(rest[16..], device.BcdDevice);
		rest[18] = device.DeviceClass;
		rest[19] = device.DeviceSubClass;
		rest[20] = device.DeviceProtocol;
		rest[21] = device.ConfigurationValue;
		rest[22] = device.ConfigurationCount;
		rest[23] = (byte)device.Interfaces.Count;
		stream.Write(record);
	}

	private static async ValueTask<bool> ReadFullyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
	{
		int read = 0;
		while (read < buffer.Length)
		{
			int got = await stream.ReadAsync(buffer[read..], cancellationToken).ConfigureAwait(false);
			if (got == 0)
			{
				return false;
			}
			read += got;
		}
		return true;
	}
}
