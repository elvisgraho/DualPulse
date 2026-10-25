using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using DualPulse.Core.Usb;
using DualPulse.Core.UsbIp;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class UsbIpServerTests : IDisposable
{
	private readonly VirtualDualSense device = new("1-1");

	private readonly UsbIpServer server = new();

	public UsbIpServerTests()
	{
		server.Add(device);
		server.Start();
	}

	public void Dispose() => server.Dispose();

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private async Task<NetworkStream> ConnectAsync()
	{
		TcpClient client = new();
		await client.ConnectAsync("127.0.0.1", server.Port, Token);
		return client.GetStream();
	}

	private static byte[] Operation(ushort code)
	{
		byte[] header = new byte[8];
		BinaryPrimitives.WriteUInt16BigEndian(header, 0x0111);
		BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), code);
		return header;
	}

	private static async Task<byte[]> ReadAsync(NetworkStream stream, int length)
	{
		byte[] buffer = new byte[length];
		await stream.ReadExactlyAsync(buffer, Token);
		return buffer;
	}

	private async Task<NetworkStream> ImportAsync(string bus = "1-1")
	{
		NetworkStream stream = await ConnectAsync();
		byte[] busId = new byte[32];
		Encoding.ASCII.GetBytes(bus).CopyTo(busId, 0);
		await stream.WriteAsync(Operation(0x8003), Token);
		await stream.WriteAsync(busId, Token);
		byte[] reply = await ReadAsync(stream, 8 + 312);
		Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(4)));
		return stream;
	}

	private static byte[] Submit(uint seqnum, bool @in, uint endpoint, int length, byte[]? setup = null)
	{
		byte[] header = new byte[48];
		BinaryPrimitives.WriteUInt32BigEndian(header, 1);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), seqnum);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 0x00010001);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), @in ? 1u : 0u);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), endpoint);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(24), length);
		setup?.CopyTo(header, 40);
		return header;
	}

	[Fact]
	public async Task DeviceList_ListsTheDualSense()
	{
		using NetworkStream stream = await ConnectAsync();
		await stream.WriteAsync(Operation(0x8005), Token);

		byte[] reply = await ReadAsync(stream, 8 + 4 + 312 + 16);

		Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(8)));
		Assert.Equal("1-1", Encoding.ASCII.GetString(reply, 12 + 256, 3));
	}

	[Fact]
	public async Task Import_ThenAControlTransfer_ReturnsTheDeviceDescriptor()
	{
		using NetworkStream stream = await ImportAsync();

		await stream.WriteAsync(Submit(5, true, 0, 18, [0x80, 0x06, 0x00, 0x01, 0x00, 0x00, 0x12, 0x00]), Token);

		byte[] ret = await ReadAsync(stream, 48 + 18);
		Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(ret));
		Assert.Equal(5u, BinaryPrimitives.ReadUInt32BigEndian(ret.AsSpan(4)));
		Assert.Equal(DualSenseUsbDescriptors.Device.ToArray(), ret[48..]);
	}

	[Fact]
	public async Task InterruptIn_CompletesWhenAnInputReportArrives()
	{
		using NetworkStream stream = await ImportAsync();
		await stream.WriteAsync(Submit(6, true, 4, 64), Token);
		await Task.Delay(50, Token);

		byte[] report = new byte[64];
		report[0] = 0x01;
		report[1] = 99;
		device.SetInput(report);

		byte[] ret = await ReadAsync(stream, 48 + 64);
		Assert.Equal(6u, BinaryPrimitives.ReadUInt32BigEndian(ret.AsSpan(4)));
		Assert.Equal(99, ret[48 + 1]);
	}

	[Fact]
	public async Task Unlink_OfAPendingTransfer_AnswersConnectionReset()
	{
		using NetworkStream stream = await ImportAsync();
		await stream.WriteAsync(Submit(7, true, 4, 64), Token);
		byte[] unlink = new byte[48];
		BinaryPrimitives.WriteUInt32BigEndian(unlink, 2);
		BinaryPrimitives.WriteUInt32BigEndian(unlink.AsSpan(4), 8);
		BinaryPrimitives.WriteUInt32BigEndian(unlink.AsSpan(20), 7);
		await Task.Delay(50, Token);

		await stream.WriteAsync(unlink, Token);

		byte[] ret = await ReadAsync(stream, 48);
		Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(ret));
		Assert.Equal(8u, BinaryPrimitives.ReadUInt32BigEndian(ret.AsSpan(4)));
		Assert.Equal(-104, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(20)));
	}

	[Fact]
	public async Task Import_UnknownBusId_IsRefused()
	{
		using NetworkStream stream = await ConnectAsync();
		byte[] busId = new byte[32];
		Encoding.ASCII.GetBytes("9-9").CopyTo(busId, 0);
		await stream.WriteAsync(Operation(0x8003), Token);
		await stream.WriteAsync(busId, Token);

		byte[] reply = await ReadAsync(stream, 8);

		Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(4)));
	}

	[Fact]
	public async Task Unlink_RacingACompletion_NeverAnswersBothOrOutOfOrder()
	{
		// The device has already claimed the transfer (Cancel says no) but its completion hasn't been written yet when
		// the unlink arrives. The host must get exactly one answer: a RET_SUBMIT for an unlinked seqnum (or after a
		// RET_UNLINK saying it completed) breaks the connection.
		ClaimingDevice claiming = new();
		server.Add(claiming);
		using NetworkStream stream = await ImportAsync("2-1");
		await stream.WriteAsync(Submit(7, true, 4, 64), Token);
		await claiming.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
		byte[] unlink = new byte[48];
		BinaryPrimitives.WriteUInt32BigEndian(unlink, 2);
		BinaryPrimitives.WriteUInt32BigEndian(unlink.AsSpan(4), 8);
		BinaryPrimitives.WriteUInt32BigEndian(unlink.AsSpan(20), 7);

		await stream.WriteAsync(unlink, Token);
		byte[] ret = await ReadAsync(stream, 48);
		claiming.CompleteNow();

		Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(ret));
		Assert.Equal(-104, BinaryPrimitives.ReadInt32BigEndian(ret.AsSpan(20)));
		using CancellationTokenSource quiet = new(TimeSpan.FromMilliseconds(300));
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stream.ReadExactlyAsync(new byte[1], quiet.Token));
	}

	// Holds every submit and refuses unlinks, like a device whose completion is already on its way.
	private sealed class ClaimingDevice : IUsbIpDevice
	{
		private Action<UrbResult>? pending;

		public TaskCompletionSource Submitted { get; } = new();

		public UsbIpExport Export { get; } = new("/test/2-1", "2-1", 2, 1, UsbIpSpeed.Full, 0x1234, 0x5678, 0x0100, 0, 0, 0, 1, 1, [(3, 0, 0)]);

		public void Submit(UrbSubmit urb, Action<UrbResult> complete)
		{
			pending = complete;
			Submitted.TrySetResult();
		}

		public bool Cancel(uint seqnum) => false;

		public void Detached()
		{
		}

		public void CompleteNow() => pending?.Invoke(UrbResult.Ok(new byte[64]));
	}
}
