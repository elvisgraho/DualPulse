using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Features;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ModServerTests : IDisposable
{
	private readonly string folder = Path.Combine(Path.GetTempPath(), "dualpulse-mods-" + Guid.NewGuid().ToString("N"));

	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new(new AppSettings(TriggerPreset: TriggerPreset.Weapon, Triggers: TriggerSettings.FromPreset(TriggerPreset.Weapon)));

	private readonly ModOverride mods = new();

	private readonly ControllerService controller;

	private readonly LightingService lighting;

	private readonly ModServerService server;

	private FakeDevice? usb;

	public ModServerTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance, mods);
		lighting = new LightingService(controller, settings, time, mods);
		server = new ModServerService(controller, settings, mods, time, NullLogger<ModServerService>.Instance,
			Path.Combine(folder, "DSX", "DSX_UDP_PortNumber.txt"), Path.Combine(folder, "Temp", "DualSenseX_PortNumber.txt"));
	}

	public void Dispose()
	{
		server.Dispose();
		lighting.Dispose();
		controller.Dispose();
		if (Directory.Exists(folder))
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	private async Task<ControllerSession> StartAsync(int port = 0)
	{
		usb = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		settings.Update(s => s with { ModServer = new ModServerSettings(Enabled: true, Port: port) });
		Assert.Equal(ModServerState.Listening, server.State);
		return controller.Session!;
	}

	// Sends a packet as a mod would and returns the reply.
	private async Task<JsonDocument> SendAsync(string json)
	{
		using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
		byte[] data = Encoding.UTF8.GetBytes(json);
		await client.SendAsync(data, new IPEndPoint(IPAddress.Loopback, server.BoundPort), TestContext.Current.CancellationToken);
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		timeout.CancelAfter(TimeSpan.FromSeconds(3));
		UdpReceiveResult reply = await client.ReceiveAsync(timeout.Token);
		return JsonDocument.Parse(reply.Buffer);
	}

	private static bool Same(TriggerEffect a, TriggerEffect b) => a.Bytes.SequenceEqual(b.Bytes);

	[Fact]
	public async Task TriggerPacket_IsAppliedAndAnswered()
	{
		ControllerSession session = await StartAsync();

		using JsonDocument reply = await SendAsync("""{"instructions":[{"type":1,"parameters":[0,2,21,1,8]}]}""");

		Assert.True(reply.RootElement.GetProperty("isControllerConnected").GetBoolean());
		Assert.True(Same(TriggerEffects.Feedback(1, 8), session.Output.RightTrigger));
		Assert.True(Same(TriggerEffects.Weapon(2, 6, 8), session.Output.LeftTrigger));
		Assert.Equal(1, server.PacketsReceived);
		Assert.Contains("Feedback", server.RecentInstructions[0]);
	}

	[Fact]
	public async Task ModOverride_KeepsUserSettingsAway_UntilReset()
	{
		ControllerSession session = await StartAsync();
		using (await SendAsync("""{"instructions":[{"type":1,"parameters":[0,2,21,1,8]},{"type":2,"parameters":[0,0,255,0]}]}""")) { }

		settings.Update(s => s with { Lightbar = new Rgb(255, 0, 0), Lighting = new LightingSettings(Mode: LightbarMode.Breathing) });
		time.Advance(TimeSpan.FromSeconds(2));
		Assert.True(Same(TriggerEffects.Feedback(1, 8), session.Output.RightTrigger));
		Assert.Equal(new Rgb(0, 255, 0), session.Output.Lightbar);

		using (await SendAsync("""{"instructions":[{"type":7,"parameters":[0]}]}""")) { }

		Assert.True(Same(TriggerEffects.Weapon(2, 6, 8), session.Output.RightTrigger));
		Assert.NotEqual(new Rgb(0, 255, 0), session.Output.Lightbar);
		Assert.False(mods.IsActive);
	}

	[Fact]
	public async Task PlayerAndMicLeds_AreApplied()
	{
		ControllerSession session = await StartAsync();

		using (await SendAsync("""{"instructions":[{"type":6,"parameters":[0,4]},{"type":5,"parameters":[0,1]}]}""")) { }

		Assert.Equal(0b11111, session.Output.PlayerLeds);
		Assert.Equal(MicLed.Pulse, session.Output.MicLed);
	}

	[Fact]
	public async Task Disabling_StopsListening_AndGivesControlBack()
	{
		ControllerSession session = await StartAsync();
		using (await SendAsync("""{"instructions":[{"type":1,"parameters":[0,2,21,1,8]}]}""")) { }

		settings.Update(s => s with { ModServer = new ModServerSettings(Enabled: false) });

		Assert.Equal(ModServerState.Off, server.State);
		Assert.True(Same(TriggerEffects.Weapon(2, 6, 8), session.Output.RightTrigger));
	}

	[Fact]
	public async Task GiveControlBack_RestoresUserSettings()
	{
		ControllerSession session = await StartAsync();
		using (await SendAsync("""{"instructions":[{"type":1,"parameters":[0,1,20]}]}""")) { }

		server.GiveControlBack();

		Assert.True(Same(TriggerEffects.Weapon(2, 6, 8), session.Output.LeftTrigger));
	}

	[Fact]
	public async Task NewSession_GetsTheModStateAgain()
	{
		await StartAsync();
		using (await SendAsync("""{"instructions":[{"type":1,"parameters":[0,2,21,1,8]}]}""")) { }

		transport.Add(FakeHidTransport.BluetoothPath);
		transport.Remove(usb!);
		time.Advance(TimeSpan.FromSeconds(1));
		ControllerSession? reconnected = controller.Session;

		Assert.NotNull(reconnected);
		Assert.True(Same(TriggerEffects.Feedback(1, 8), reconnected!.Output.RightTrigger));
	}

	[Fact]
	public async Task PortFiles_WrittenWhenListening()
	{
		await StartAsync();

		Assert.Equal(server.BoundPort.ToString(), File.ReadAllText(Path.Combine(folder, "DSX", "DSX_UDP_PortNumber.txt")));
		Assert.Equal(server.BoundPort.ToString(), File.ReadAllText(Path.Combine(folder, "Temp", "DualSenseX_PortNumber.txt")));
	}

	[Fact]
	public async Task MalformedPackets_AreCounted_AndStillAnswered()
	{
		await StartAsync();

		using JsonDocument reply = await SendAsync("{not json");

		Assert.Equal(1, server.RejectedInstructions);
		Assert.True(reply.RootElement.TryGetProperty("Status", out _));
	}

	[Fact]
	public async Task PortInUse_ReportsError()
	{
		using UdpClient occupier = new(new IPEndPoint(IPAddress.Loopback, 0));
		int taken = ((IPEndPoint)occupier.Client.LocalEndPoint!).Port;
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		settings.Update(s => s with { ModServer = new ModServerSettings(Enabled: true, Port: taken) });

		Assert.Equal(ModServerState.PortInUse, server.State);
	}
}
