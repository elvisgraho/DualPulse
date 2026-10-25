using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using DualPulse.Core.Protocol;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class UsbIpDualSensePadTests : IDisposable
{
	private readonly HostAttacher attacher = new();

	public void Dispose() => attacher.Dispose();

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Create_PlugsInTheServedDualSense()
	{
		using UsbIpDualSensePad pad = UsbIpDualSensePad.Create(attacher);

		Assert.Equal(PadKind.DualSense, pad.Kind);
		Assert.Equal("1-1", attacher.BusId);
		Assert.Equal(0x054C, BinaryPrimitives.ReadUInt16BigEndian(attacher.ImportedDevice.AsSpan(256 + 32 + 12)));
		Assert.Equal(0x0CE6, BinaryPrimitives.ReadUInt16BigEndian(attacher.ImportedDevice.AsSpan(256 + 32 + 14)));
	}

	[Fact]
	public async Task Input_CompletesTheHostsInterruptTransfer()
	{
		using UsbIpDualSensePad pad = UsbIpDualSensePad.Create(attacher);
		await attacher.Host!.WriteAsync(Submit(1, true, 4, 64), Token);

		pad.Submit(new InputSnapshot { LeftX = 0xFF });

		byte[] ret = await ReadAsync(48 + 64);
		Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(ret));
		Assert.Equal(0x01, ret[48]);
		Assert.Equal(0xFF, ret[48 + 1]);
	}

	[Fact]
	public async Task GameOutput_IsRaisedAsTheCommonBlock()
	{
		using UsbIpDualSensePad pad = UsbIpDualSensePad.Create(attacher);
		TaskCompletionSource<byte[]> block = new();
		pad.OutputBlock += b => block.TrySetResult(b);
		byte[] report = new byte[48];
		report[0] = 0x02;
		report[1] = 0x0C;

		await attacher.Host!.WriteAsync((byte[])[.. Submit(2, false, 3, 48), .. report], Token);

		byte[] received = await block.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
		Assert.Equal(OutputState.CommonBlockLength, received.Length);
		Assert.Equal(0x0C, received[0]);
	}

	[Fact]
	public async Task HostLettingGo_RaisesLost()
	{
		using UsbIpDualSensePad pad = UsbIpDualSensePad.Create(attacher);
		TaskCompletionSource lost = new();
		pad.Lost += () => lost.TrySetResult();

		attacher.Host!.Socket.Close();

		await lost.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
	}

	[Fact]
	public void Dispose_UnplugsTheDevice_WithoutRaisingLost()
	{
		UsbIpDualSensePad pad = UsbIpDualSensePad.Create(attacher);
		bool lost = false;
		pad.Lost += () => lost = true;

		pad.Dispose();

		Assert.Equal([7], attacher.Detached);
		Assert.False(lost);
	}

	[Fact]
	public void FailedAttach_Throws_AndStopsServing()
	{
		attacher.Fail = true;

		UsbIpException error = Assert.Throws<UsbIpException>(() => UsbIpDualSensePad.Create(attacher));

		Assert.Contains("refused", error.Message, StringComparison.Ordinal);
		Assert.Throws<SocketException>(() => new TcpClient().Connect("127.0.0.1", attacher.Port));
	}

	[Fact]
	public void HostDroppingTheDevice_RightAfterAttaching_FailsTheCreation()
	{
		// A drop before the pad existed was missed: the pad looked plugged in with nothing behind it.
		attacher.DropRightAway = true;

		Assert.Throws<UsbIpException>(() => UsbIpDualSensePad.Create(attacher));
		Assert.Equal([7], attacher.Detached);
	}

	[Fact]
	public void Backend_IsUnavailable_WithoutTheAttacher()
	{
		attacher.Available = false;
		UsbIpBackend backend = new(attacher);

		Assert.False(backend.IsAvailableFor(PadKind.DualSense));
		Assert.Equal("usbip missing", backend.UnavailableReason);
		Assert.Throws<NotSupportedException>(() => backend.Create(PadKind.Xbox360));
	}

	private async Task<byte[]> ReadAsync(int length)
	{
		byte[] buffer = new byte[length];
		await attacher.Host!.ReadExactlyAsync(buffer, Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5), Token);
		return buffer;
	}

	private static byte[] Submit(uint seqnum, bool @in, uint endpoint, int length)
	{
		byte[] header = new byte[48];
		BinaryPrimitives.WriteUInt32BigEndian(header, 1);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), seqnum);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 0x00010001);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), @in ? 1u : 0u);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), endpoint);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(24), length);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(32), -1);
		return header;
	}

	// Plays the host: imports the device from the pad's server as usbip attach would, and keeps the connection.
	private sealed class HostAttacher : IUsbIpAttacher, IDisposable
	{
		public bool Available { get; set; } = true;

		public bool Fail { get; set; }

		public bool DropRightAway { get; set; }

		public int Port { get; private set; }

		public string? BusId { get; private set; }

		public byte[] ImportedDevice { get; private set; } = [];

		public NetworkStream? Host { get; private set; }

		public List<int> Detached { get; } = [];

		public bool IsAvailable => Available;

		public string? UnavailableReason => Available ? null : "usbip missing";

		public int Attach(int port, string busId)
		{
			Port = port;
			BusId = busId;
			if (Fail)
			{
				throw new UsbIpException("usbip refused the device.");
			}
			TcpClient client = new();
			client.Connect("127.0.0.1", port);
			Host = client.GetStream();
			byte[] request = new byte[8 + 32];
			BinaryPrimitives.WriteUInt16BigEndian(request, 0x0111);
			BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2), 0x8003);
			Encoding.ASCII.GetBytes(busId).CopyTo(request, 8);
			Host.Write(request);
			byte[] reply = new byte[8 + 312];
			Host.ReadExactly(reply);
			ImportedDevice = reply[8..];
			if (DropRightAway)
			{
				Host.Socket.Close();
				Thread.Sleep(300);
			}
			return 7;
		}

		public void Detach(int handle)
		{
			Detached.Add(handle);
			Host?.Socket.Close();
		}

		public void Dispose() => Host?.Dispose();
	}
}
