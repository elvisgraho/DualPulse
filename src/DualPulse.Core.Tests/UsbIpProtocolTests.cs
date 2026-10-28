using System.Buffers.Binary;
using DualPulse.Core.UsbIp;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class UsbIpProtocolTests
{
	private static readonly UsbIpExport Export = new("/dualpulse/1-1", "1-1", 1, 1, UsbIpSpeed.Full, 0x054C, 0x0CE6, 0x0100, 0, 0, 0, 1, 1,
		[(1, 1, 0), (1, 2, 0), (1, 2, 0), (3, 0, 0)]);

	[Fact]
	public async Task OperationHeader_IsVersionCodeStatus_BigEndian()
	{
		MemoryStream stream = new([0x01, 0x11, 0x80, 0x05, 0, 0, 0, 0]);

		UsbIpOperation op = (await UsbIpProtocol.ReadOperationAsync(stream, TestContext.Current.CancellationToken))!.Value;

		Assert.Equal(UsbIpProtocol.RequestDeviceList, op.Code);
	}

	[Fact]
	public void ImportReply_CarriesTheDevice()
	{
		MemoryStream stream = new();

		UsbIpProtocol.WriteImportReply(stream, Export);

		byte[] reply = stream.ToArray();
		Assert.Equal(8 + 312, reply.Length);
		Assert.Equal(new byte[] { 0x01, 0x11, 0x00, 0x03, 0, 0, 0, 0 }, reply[..8]);
		Assert.Equal("1-1", System.Text.Encoding.ASCII.GetString(reply, 8 + 256, 3));
		Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(8 + 256 + 32 + 8)));
		Assert.Equal(0x054C, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(8 + 256 + 32 + 12)));
		Assert.Equal(0x0CE6, BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(8 + 256 + 32 + 14)));
		Assert.Equal(4, reply[8 + 311]);
	}

	[Fact]
	public void ImportReply_UnknownBusId_IsAnErrorStatus()
	{
		MemoryStream stream = new();

		UsbIpProtocol.WriteImportReply(stream, null);

		Assert.Equal(new byte[] { 0x01, 0x11, 0x00, 0x03, 0, 0, 0, 1 }, stream.ToArray());
	}

	[Fact]
	public void DeviceList_CarriesDevicesAndTheirInterfaces()
	{
		MemoryStream stream = new();

		UsbIpProtocol.WriteDeviceList(stream, [Export]);

		byte[] reply = stream.ToArray();
		Assert.Equal(8 + 4 + 312 + 4 * 4, reply.Length);
		Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(8)));
		Assert.Equal(new byte[] { 3, 0, 0, 0 }, reply[^4..]);
	}

	private static byte[] SubmitHeader(uint seqnum, uint direction, uint endpoint, int length, int packets = 0, byte[]? setup = null)
	{
		byte[] header = new byte[48];
		BinaryPrimitives.WriteUInt32BigEndian(header, 1);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), seqnum);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 0x00010001);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), direction);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), endpoint);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(24), length);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(32), packets);
		setup?.CopyTo(header, 40);
		return header;
	}

	[Fact]
	public async Task Command_ControlSubmit_WithSetup()
	{
		byte[] setup = [0x80, 0x06, 0x00, 0x01, 0x00, 0x00, 0x12, 0x00];
		MemoryStream stream = new(SubmitHeader(7, 1, 0, 18, setup: setup));

		UrbSubmit submit = Assert.IsType<UrbSubmit>(await UsbIpProtocol.ReadCommandAsync(stream, CancellationToken.None));

		Assert.Equal(7u, submit.Seqnum);
		Assert.True(submit.In);
		Assert.Equal(0, submit.Endpoint);
		Assert.Equal(18, submit.Length);
		Assert.Equal(setup, submit.Setup);
		Assert.Empty(submit.Data);
	}

	[Fact]
	public async Task Command_IsochronousOut_ReadsDataAndPacketDescriptors()
	{
		byte[] header = SubmitHeader(9, 0, 1, 6, packets: 2);
		byte[] data = [1, 2, 3, 4, 5, 6];
		byte[] descriptors = new byte[32];
		BinaryPrimitives.WriteInt32BigEndian(descriptors.AsSpan(0), 0);
		BinaryPrimitives.WriteInt32BigEndian(descriptors.AsSpan(4), 4);
		BinaryPrimitives.WriteInt32BigEndian(descriptors.AsSpan(16), 4);
		BinaryPrimitives.WriteInt32BigEndian(descriptors.AsSpan(20), 2);
		MemoryStream stream = new([.. header, .. data, .. descriptors]);

		UrbSubmit submit = Assert.IsType<UrbSubmit>(await UsbIpProtocol.ReadCommandAsync(stream, CancellationToken.None));

		Assert.False(submit.In);
		Assert.Equal(data, submit.Data);
		Assert.Equal([(0, 4), (4, 2)], submit.IsoPackets);
	}

	[Fact]
	public async Task Command_Unlink()
	{
		byte[] header = new byte[48];
		BinaryPrimitives.WriteUInt32BigEndian(header, 2);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 11);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), 7);

		UrbUnlink unlink = Assert.IsType<UrbUnlink>(await UsbIpProtocol.ReadCommandAsync(new MemoryStream(header), CancellationToken.None));

		Assert.Equal(11u, unlink.Seqnum);
		Assert.Equal(7u, unlink.Target);
	}

	[Fact]
	public void ReturnSubmit_In_CarriesTheData()
	{
		MemoryStream stream = new();

		UsbIpProtocol.WriteReturnSubmit(stream, new UrbSubmit(7, 0x00010001, true, 0, 0, 18, 0, 0, 0, new byte[8], [], []), UrbResult.Ok([0x12, 0x01]));

		byte[] ret = stream.ToArray();
		Assert.Equal(48 + 2, ret.Length);
		Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(ret));
		Assert.Equal(7u, BinaryPrimitives.ReadUInt32BigEndian(ret.AsSpan(4)));
		Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(20)));
		Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(24)));
		Assert.Equal(new byte[] { 0x12, 0x01 }, ret[48..]);
	}

	[Fact]
	public void ReturnSubmit_Stall_IsEpipe_WithNoData()
	{
		MemoryStream stream = new();

		UsbIpProtocol.WriteReturnSubmit(stream, new UrbSubmit(8, 0, true, 0, 0, 64, 0, 0, 0, new byte[8], [], []), UrbResult.Stall);

		byte[] ret = stream.ToArray();
		Assert.Equal(48, ret.Length);
		Assert.Equal(-32, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(20)));
	}

	[Fact]
	public void ReturnSubmit_IsochronousOut_ReportsEveryPacketAccepted()
	{
		MemoryStream stream = new();
		UrbSubmit submit = new(9, 0, false, 1, 0, 6, 100, 2, 1, new byte[8], [1, 2, 3, 4, 5, 6], [(0, 4), (4, 2)]);

		UsbIpProtocol.WriteReturnSubmit(stream, submit, UrbResult.Ok([]));

		byte[] ret = stream.ToArray();
		Assert.Equal(48 + 32, ret.Length);
		Assert.Equal(6, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(24)));
		Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(32)));
		Assert.Equal(4, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(48 + 8)));
		Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(48 + 16 + 8)));
	}

	[Fact]
	public void ReturnSubmit_IsochronousIn_ShortPackets_ReportTheirLengths_DataPacked()
	{
		MemoryStream stream = new();
		UrbSubmit submit = new(9, 0, true, 2, 0, 8, 100, 2, 1, new byte[8], [], [(0, 4), (4, 4)]);

		UsbIpProtocol.WriteReturnSubmit(stream, submit, UrbResult.Ok([1, 2, 3, 4, 5]) with { IsoActualLengths = [3, 2] });

		byte[] ret = stream.ToArray();
		Assert.Equal(48 + 5 + 32, ret.Length);
		Assert.Equal(5, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(24)));
		Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, ret[48..53]);
		Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(53)));
		Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(53 + 8)));
		Assert.Equal(4, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(53 + 16)));
		Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(53 + 16 + 8)));
	}

	[Fact]
	public void ReturnSubmit_Isochronous_CarriesTheStartFrameTheDeviceChose()
	{
		// The host schedules its next transfer from the frame the device reports, so it must not just echo the request.
		MemoryStream stream = new();
		UrbSubmit submit = new(10, 0, false, 1, 0, 4, 0, 1, 1, new byte[8], [1, 2, 3, 4], [(0, 4)]);

		UsbIpProtocol.WriteReturnSubmit(stream, submit, UrbResult.Ok([]) with { StartFrame = 1234 });

		Assert.Equal(1234, BinaryPrimitives.ReadInt32BigEndian(stream.ToArray().AsSpan(28)));
	}

	[Fact]
	public void ReturnUnlink_StatusOnly()
	{
		MemoryStream stream = new();

		UsbIpProtocol.WriteReturnUnlink(stream, 11, -104);

		byte[] ret = stream.ToArray();
		Assert.Equal(48, ret.Length);
		Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(ret));
		Assert.Equal(-104, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(20)));
	}
}
