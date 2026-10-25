using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DualPulse.Core.Devices;
using DualPulse.Core.Mods;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Services;

public enum ModServerState
{
	Off,
	Listening,
	PortInUse,
	Error
}

public interface IModServer
{
	ModServerState State { get; }

	int BoundPort { get; }

	int PacketsReceived { get; }

	int RejectedInstructions { get; }

	DateTimeOffset? LastPacketAt { get; }

	IReadOnlyList<string> RecentInstructions { get; }

	// Whether a mod currently overrides any of the user's settings.
	bool ModActive { get; }

	event Action? Changed;

	void GiveControlBack();
}

// The DSX-compatible mod server: game mods send UDP packets to 127.0.0.1 (this PC only) to drive the triggers and
// LEDs. Their instructions override the user's settings until a reset instruction arrives, the user gives control
// back, or the server is turned off. Every packet is answered with a status reply. While listening, the port number
// is written where DSX v3 and v2 mods look for it (optional).
public sealed class ModServerService : IModServer, IHostedService, IDisposable
{
	private const int MaxRecent = 10;

	// Stops Windows reporting "port unreachable" from a mod that already closed as an error on the next receive.
	private const int SioUdpConnReset = -1744830452;

	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly ModOverride mods;

	private readonly TimeProvider time;

	private readonly ILogger<ModServerService> logger;

	private readonly string? v3PortFile;

	private readonly string? v2PortFile;

	private readonly object gate = new();

	private readonly List<string> recent = [];

	private UdpClient? client;

	private CancellationTokenSource? listening;

	private Task? loop;

	private ModServerSettings? active;

	private int packets;

	private int rejected;

	public ModServerService(IControllerService controller, ISettingsService settings, ModOverride mods, TimeProvider time,
		ILogger<ModServerService> logger, string? v3PortFile, string? v2PortFile)
	{
		this.controller = controller;
		this.settings = settings;
		this.mods = mods;
		this.time = time;
		this.logger = logger;
		this.v3PortFile = v3PortFile;
		this.v2PortFile = v2PortFile;
		settings.Changed += OnSettingsChanged;
		controller.SessionStarted += OnSessionStarted;
	}

	public ModServerState State { get; private set; } = ModServerState.Off;

	public int BoundPort { get; private set; }

	public int PacketsReceived => Volatile.Read(ref packets);

	public int RejectedInstructions => Volatile.Read(ref rejected);

	public DateTimeOffset? LastPacketAt { get; private set; }

	public bool ModActive => mods.IsActive;

	// Newest first.
	public IReadOnlyList<string> RecentInstructions
	{
		get
		{
			lock (recent)
			{
				return recent.ToList();
			}
		}
	}

	// Raised after state changes and after each packet (on a background thread).
	public event Action? Changed;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		Reconfigure();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		settings.Changed -= OnSettingsChanged;
		controller.SessionStarted -= OnSessionStarted;
		lock (gate)
		{
			StopListening();
		}
	}

	public void GiveControlBack()
	{
		mods.Clear();
		Changed?.Invoke();
	}

	private void OnSettingsChanged(AppSettings current)
	{
		Reconfigure();
	}

	private void OnSessionStarted(ControllerSession session)
	{
		mods.ApplyTo(session);
	}

	private void Reconfigure()
	{
		ModServerSettings desired = settings.Current.EffectiveModServer;
		bool changed;
		lock (gate)
		{
			if (desired == active && (State == ModServerState.Listening || !desired.Enabled))
			{
				return;
			}
			bool wasEnabled = active?.Enabled == true;
			StopListening();
			active = desired;
			if (desired.Enabled)
			{
				StartListening(desired);
			}
			else
			{
				State = ModServerState.Off;
				if (wasEnabled)
				{
					logger.LogInformation("Mod server off");
				}
			}
			changed = true;
		}
		if (!desired.Enabled)
		{
			mods.Clear();
		}
		if (changed)
		{
			Changed?.Invoke();
		}
	}

	// Caller holds gate.
	private void StartListening(ModServerSettings options)
	{
		UdpClient udp;
		try
		{
			udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, options.Port));
		}
		catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
		{
			logger.LogWarning("Mod server: port {Port} is already in use", options.Port);
			State = ModServerState.PortInUse;
			return;
		}
		catch (SocketException exception)
		{
			logger.LogWarning("Mod server could not start: {Error}", exception.Message);
			State = ModServerState.Error;
			return;
		}
		try
		{
			udp.Client.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
		}
		catch (SocketException)
		{
		}
		client = udp;
		BoundPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
		listening = new CancellationTokenSource();
		CancellationToken token = listening.Token;
		loop = Task.Run(() => ReceiveLoop(udp, token));
		State = ModServerState.Listening;
		logger.LogInformation("Mod server listening on 127.0.0.1:{Port}", BoundPort);
		if (options.WritePortFiles)
		{
			WritePortFile(v3PortFile);
			WritePortFile(v2PortFile);
		}
	}

	// Caller holds gate.
	private void StopListening()
	{
		listening?.Cancel();
		client?.Dispose();
		try
		{
			loop?.Wait(TimeSpan.FromSeconds(1));
		}
		catch (AggregateException)
		{
		}
		listening?.Dispose();
		(client, listening, loop) = (null, null, null);
	}

	private async Task ReceiveLoop(UdpClient udp, CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			UdpReceiveResult datagram;
			try
			{
				datagram = await udp.ReceiveAsync(token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (ObjectDisposedException)
			{
				return;
			}
			catch (SocketException exception)
			{
				logger.LogDebug("Mod server receive failed: {Error}", exception.Message);
				continue;
			}
			if (!IPAddress.IsLoopback(datagram.RemoteEndPoint.Address))
			{
				continue;
			}
			try
			{
				Handle(datagram.Buffer);
				byte[] reply = DsxProtocol.StatusReply(Status());
				await udp.SendAsync(reply, datagram.RemoteEndPoint, token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
			{
				logger.LogDebug("Mod server reply failed: {Error}", exception.Message);
			}
			Changed?.Invoke();
		}
	}

	private void Handle(byte[] packet)
	{
		IReadOnlyList<ModInstruction> instructions = DsxProtocol.Parse(packet, out int bad);
		Interlocked.Increment(ref packets);
		Interlocked.Add(ref rejected, bad);
		LastPacketAt = time.GetUtcNow();
		ControllerSession? session = controller.Session;
		bool applied = false;
		foreach (ModInstruction instruction in instructions)
		{
			switch (instruction)
			{
			case ModReset:
				mods.Clear();
				Remember("Reset to your settings");
				break;
			case ModStatusRequest:
				break;
			case ModTriggerThreshold threshold:
				// Accepted for compatibility; Xbox 360 triggers are analog, so there is no pressed point to move.
				Remember($"Trigger threshold {threshold.Threshold} (no effect)");
				break;
			default:
				applied |= mods.Set(instruction);
				Remember(Describe(instruction));
				break;
			}
		}
		if (applied && session != null)
		{
			mods.ApplyTo(session);
		}
	}

	private DsxStatus Status()
	{
		ControllerSession? session = controller.Session;
		if (session == null)
		{
			return new DsxStatus(false, 0, null, false, time.GetLocalNow().DateTime);
		}
		bool bluetooth = session.Transport == Transport.Bluetooth;
		return new DsxStatus(true, session.Battery.Level, null, bluetooth, time.GetLocalNow().DateTime);
	}

	private static string Describe(ModInstruction instruction)
	{
		return instruction switch
		{
			ModTriggerEffect { Trigger: ModTrigger.Left } effect => "L2: " + effect.Description,
			ModTriggerEffect { Trigger: ModTrigger.Right } effect => "R2: " + effect.Description,
			ModTriggerEffect effect => "L2 and R2: " + effect.Description,
			ModLightbar colour => $"Lightbar #{colour.Colour.R:X2}{colour.Colour.G:X2}{colour.Colour.B:X2}",
			ModPlayerLeds leds => $"Player LEDs {Convert.ToString(leds.Bits, 2).PadLeft(5, '0')}",
			ModMicLed mic => $"Mic LED {mic.MicLed}",
			_ => instruction.GetType().Name
		};
	}

	private void Remember(string text)
	{
		lock (recent)
		{
			recent.Insert(0, text);
			if (recent.Count > MaxRecent)
			{
				recent.RemoveAt(recent.Count - 1);
			}
		}
	}

	private void WritePortFile(string? path)
	{
		if (path == null)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, BoundPort.ToString());
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning("Could not write the mod port file {Path}: {Error}", path, exception.Message);
		}
	}
}
