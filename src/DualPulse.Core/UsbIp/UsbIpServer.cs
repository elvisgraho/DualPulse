using System.Net;
using System.Net.Sockets;
using DualPulse.Core.Usb;

namespace DualPulse.Core.UsbIp;

// A USB/IP server on localhost for DualPulse's virtual devices (the usbip-win2 driver, or Linux's vhci, attaches them).
// Each connection either asks for the device list and closes, or imports one device and then carries its URBs:
// submits go to the device, completions (from any thread) are written back one at a time, unlinks cancel pending
// transfers. Port 0 picks a free port.
public sealed class UsbIpServer(int port = 0) : IDisposable
{
	private readonly TcpListener listener = new(IPAddress.Loopback, port);

	private readonly Dictionary<string, IUsbIpDevice> devices = [];

	private readonly CancellationTokenSource stopping = new();

	private readonly object gate = new();

	public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

	// Raised when a host imports or lets go of a device (bus id).
	public event Action<string>? Imported;

	public event Action<string>? Released;

	public void Add(IUsbIpDevice device)
	{
		lock (gate)
		{
			devices[device.Export.BusId] = device;
		}
	}

	public void Remove(string busId)
	{
		lock (gate)
		{
			devices.Remove(busId);
		}
	}

	public void Start()
	{
		listener.Start();
		_ = Task.Run(AcceptLoopAsync);
	}

	public void Dispose()
	{
		stopping.Cancel();
		listener.Stop();
	}

	private async Task AcceptLoopAsync()
	{
		while (!stopping.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await listener.AcceptTcpClientAsync(stopping.Token).ConfigureAwait(false);
			}
			catch (Exception error) when (error is OperationCanceledException or SocketException or ObjectDisposedException)
			{
				return;
			}
			_ = Task.Run(() => ServeAsync(client));
		}
	}

	private async Task ServeAsync(TcpClient client)
	{
		using (client)
		{
			client.NoDelay = true;
			NetworkStream stream = client.GetStream();
			try
			{
				UsbIpOperation? operation = await UsbIpProtocol.ReadOperationAsync(stream, stopping.Token).ConfigureAwait(false);
				if (operation?.Code == UsbIpProtocol.RequestDeviceList)
				{
					IUsbIpDevice[] listed;
					lock (gate)
					{
						listed = [.. devices.Values];
					}
					UsbIpProtocol.WriteDeviceList(stream, [.. listed.Select(d => d.Export)]);
					return;
				}
				if (operation?.Code != UsbIpProtocol.RequestImport)
				{
					return;
				}
				string? busId = await UsbIpProtocol.ReadBusIdAsync(stream, stopping.Token).ConfigureAwait(false);
				IUsbIpDevice? device = null;
				lock (gate)
				{
					if (busId != null)
					{
						devices.TryGetValue(busId, out device);
					}
				}
				UsbIpProtocol.WriteImportReply(stream, device?.Export);
				if (device == null)
				{
					return;
				}
				Imported?.Invoke(busId!);
				try
				{
					await CarryUrbsAsync(stream, device).ConfigureAwait(false);
				}
				finally
				{
					device.Detached();
					Released?.Invoke(busId!);
				}
			}
			catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException or OperationCanceledException or InvalidDataException)
			{
			}
		}
	}

	// The connection decides which answer wins, under its write lock: a completion is written only while its transfer is
	// still in flight here, and an unlink takes a transfer out of flight before cancelling it in the device. So the host
	// gets exactly one answer per transfer (a late completion of an unlinked one is dropped), never out of order.
	private async Task CarryUrbsAsync(NetworkStream stream, IUsbIpDevice device)
	{
		object writeGate = new();
		HashSet<uint> inFlight = [];
		while (true)
		{
			UrbCommand? command = await UsbIpProtocol.ReadCommandAsync(stream, stopping.Token).ConfigureAwait(false);
			switch (command)
			{
			case null:
				return;
			case UrbSubmit submit:
				lock (writeGate)
				{
					inFlight.Add(submit.Seqnum);
				}
				device.Submit(submit, result =>
				{
					lock (writeGate)
					{
						if (!inFlight.Remove(submit.Seqnum))
						{
							return;
						}
						try
						{
							UsbIpProtocol.WriteReturnSubmit(stream, submit, result);
						}
						catch (Exception error) when (error is IOException or ObjectDisposedException)
						{
						}
					}
				});
				break;
			case UrbUnlink unlink:
				lock (writeGate)
				{
					bool pending = inFlight.Remove(unlink.Target);
					if (pending)
					{
						device.Cancel(unlink.Target);
					}
					// Already answered: the unlink found nothing to cancel.
					UsbIpProtocol.WriteReturnUnlink(stream, unlink.Seqnum, pending ? UsbIpProtocol.Econnreset : 0);
				}
				break;
			}
		}
	}
}
