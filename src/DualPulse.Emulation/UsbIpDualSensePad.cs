using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DualPulse.Core.Protocol;
using DualPulse.Core.Usb;
using DualPulse.Core.UsbIp;

namespace DualPulse.Emulation;

public sealed class UsbIpException(string message, Exception? inner = null) : Exception(message, inner);

// Plugs a device served over USB/IP on localhost into this computer and unplugs it again (usbip-win2 on Windows; on
// Linux it would be the vhci-hcd driver's usbip tool). Attach returns a handle for Detach, or throws UsbIpException.
public interface IUsbIpAttacher
{
	bool IsAvailable { get; }

	string? UnavailableReason { get; }

	int Attach(int port, string busId);

	void Detach(int handle);
}

// A virtual USB DualSense made in-house: DualPulse serves VirtualDualSense over USB/IP on a loopback port of its own
// and has the attacher plug it in. Input goes out as USB input reports; the game's output blocks, haptics and speaker
// audio are raised as events. Lost: the host let go of the device. Disposing unplugs it and stops serving.
public sealed class UsbIpDualSensePad : IDualSensePad
{
	private const string BusId = "1-1";

	private readonly IUsbIpAttacher attacher;

	private readonly UsbIpServer server;

	private readonly VirtualDualSense device;

	private readonly ILogger logger;

	private readonly int handle;

	private readonly object gate = new();

	private readonly byte[] inputReport = new byte[UsbInputReport.Length];

	private byte counter;

	private volatile bool disposed;

	private UsbIpDualSensePad(IUsbIpAttacher attacher, UsbIpServer server, VirtualDualSense device, int handle, ILogger logger)
	{
		this.attacher = attacher;
		this.server = server;
		this.device = device;
		this.handle = handle;
		this.logger = logger;
		device.OutputBlock += block => OutputBlock?.Invoke(block);
		device.Speaker += (samples, channels) => Speaker?.Invoke(samples, channels);
		device.Haptics += (samples, channels) => Haptics?.Invoke(samples, channels);
		server.Released += _ =>
		{
			if (!disposed)
			{
				logger.LogWarning("The virtual DualSense was unplugged by the host");
				IsLost = true;
				Lost?.Invoke();
			}
		};
	}

	public PadKind Kind => PadKind.DualSense;

	public event Action<RumbleFeedback>? Feedback
	{
		add { }
		remove { }
	}

	public event Action<byte[]>? OutputBlock;

	public event GameAudio? Haptics;

	public event GameAudio? Speaker;

	public event Action? Lost;

	public bool IsLost { get; private set; }

	// Blocks until the attacher has plugged the device in; throws UsbIpException (serving nothing) when it can't.
	public static UsbIpDualSensePad Create(IUsbIpAttacher attacher, ILogger? logger = null)
	{
		logger ??= NullLogger.Instance;
		VirtualDualSense device = new(BusId);
		UsbIpServer server = new();
		server.Add(device);
		// The host can let go before the pad exists to notice (Lost); such a pad would look plugged in with nothing behind it.
		bool released = false;
		Action<string> watch = _ => released = true;
		server.Released += watch;
		int? handle = null;
		try
		{
			server.Start();
			handle = attacher.Attach(server.Port, BusId);
			UsbIpDualSensePad pad = new(attacher, server, device, handle.Value, logger);
			server.Released -= watch;
			if (released)
			{
				throw new UsbIpException("The host let go of the virtual DualSense right after plugging it in.");
			}
			logger.LogInformation("Virtual DualSense served on port {Port}, attached as {Handle}", server.Port, handle);
			return pad;
		}
		catch (Exception error)
		{
			logger.LogWarning("Creating the virtual DualSense failed: {Error}", error.Message);
			if (handle is { } attached)
			{
				try
				{
					attacher.Detach(attached);
				}
				catch (UsbIpException)
				{
				}
			}
			server.Dispose();
			throw;
		}
	}

	// Read thread of the real controller.
	public void Submit(in InputSnapshot input)
	{
		lock (gate)
		{
			if (disposed)
			{
				return;
			}
			UsbInputReport.Build(input, counter++, inputReport);
			device.SetInput(inputReport);
		}
	}

	public void Dispose()
	{
		lock (gate)
		{
			if (disposed)
			{
				return;
			}
			disposed = true;
		}
		try
		{
			attacher.Detach(handle);
		}
		catch (UsbIpException error)
		{
			logger.LogWarning("Unplugging the virtual DualSense failed: {Error}", error.Message);
		}
		server.Dispose();
	}
}

// Creates in-house virtual DualSense pads; available when the attacher is (usbip-win2 installed).
public sealed class UsbIpBackend(IUsbIpAttacher attacher, ILogger? logger = null) : IVirtualPadBackend
{
	public bool IsAvailable => attacher.IsAvailable;

	public string? UnavailableReason => attacher.UnavailableReason;

	public bool IsAvailableFor(PadKind kind) => kind == PadKind.DualSense && IsAvailable;

	public IVirtualPad Create(PadKind kind)
	{
		if (kind != PadKind.DualSense)
		{
			throw new NotSupportedException($"The USB/IP backend only creates DualSense pads, not {kind}.");
		}
		return UsbIpDualSensePad.Create(attacher, logger);
	}
}
