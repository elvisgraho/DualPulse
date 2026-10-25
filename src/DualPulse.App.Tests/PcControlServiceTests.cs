using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class PcControlServiceTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	// Starts in Xbox 360 mode, which these tests use (new installs start in Native).
	private readonly FakeSettingsService settings = new(new AppSettings(Emulation: DualPulse.Emulation.PadKind.Xbox360));

	private readonly FakePcOutput output = new();

	private readonly ControllerService controller;

	private readonly PcControlService pcControl;

	private FakeConnection? connection;

	public PcControlServiceTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		pcControl = new PcControlService(controller, settings, output, time);
	}

	public void Dispose()
	{
		pcControl.Dispose();
		controller.Dispose();
	}

	private async Task ConnectAsync()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		connection = device.Latest;
	}

	// Sends a USB report with these buttons held and waits until the session has read it.
	private void Report(Buttons buttons)
	{
		byte[] report = TestReports.Fixture("idle-usb");
		uint bits = (uint)buttons;
		// Face buttons sit in the high nibble of byte 8 (hat in the low nibble), then two more button bytes.
		report[1 + 7] = (byte)((uint)(report[1 + 7] & 0x0F) | ((bits & 0x0F) << 4));
		report[1 + 8] = (byte)((bits >> 4) & 0xFF);
		report[1 + 9] = (byte)((uint)(report[1 + 9] & 0xF8) | ((bits >> 12) & 0x07));
		report[1 + 27] = (byte)(report[1 + 27] + 1);
		connection!.Feed(report);
		TestReports.WaitUntil(() => connection.PendingReports == 0 && controller.Session!.Latest?.Buttons == buttons);
		Thread.Sleep(20);
	}

	[Fact]
	public async Task Off_SendsNothing()
	{
		await ConnectAsync();

		Report(Buttons.Cross);

		Assert.Empty(output.Events);
	}

	[Fact]
	public async Task PsPlusOptions_SwitchesOn_ThenButtonsSendKeys()
	{
		await ConnectAsync();
		bool changed = false;
		pcControl.ActiveChanged += () => changed = true;

		Report(Buttons.Ps | Buttons.Options);
		Report(Buttons.None);
		Report(Buttons.Cross);
		Report(Buttons.None);

		Assert.True(pcControl.IsActive);
		Assert.True(changed);
		Assert.Equal(["Enter down", "Enter up"], output.Events);
	}

	[Fact]
	public async Task SwitchingOff_ReleasesHeldKeys()
	{
		await ConnectAsync();
		pcControl.IsActive = true;
		Report(Buttons.Cross);

		pcControl.IsActive = false;

		Assert.Equal(["Enter down", "Enter up"], output.Events);
	}

	[Fact]
	public async Task CustomButtonMapping_FromSettings()
	{
		await ConnectAsync();
		settings.Update(s => s with { Mapping = new MappingSettings(Buttons: new Dictionary<string, PcAction> { ["Cross"] = PcAction.PlayPause }) });
		pcControl.IsActive = true;

		Report(Buttons.Cross);

		Assert.Equal(["PlayPause down"], output.Events);
	}

	[Fact]
	public async Task EmulationPauses_WhilePcControlIsOn()
	{
		FakePadBackend backend = new();
		using RumbleService rumble = new(controller, settings, time);
		using EmulationService emulation = new(controller, backend, settings, rumble, NullLogger<EmulationService>.Instance, pcControl, padWorker: work => work());
		await ConnectAsync();
		Report(Buttons.Cross);
		FakePad pad = backend.Created[0];
		int beforeSwitch = pad.Submitted;

		pcControl.IsActive = true;
		Report(Buttons.Circle);
		Report(Buttons.Square);

		// One neutral report releases whatever the game saw held, then nothing more.
		Assert.Equal(beforeSwitch + 1, pad.Submitted);
	}

	[Fact]
	public async Task Mute_GoesToTheMicrophone_NotVolumeMute()
	{
		await ConnectAsync();
		Report(Buttons.Ps | Buttons.Options);
		Report(Buttons.None);

		Report(Buttons.Mute);
		Report(Buttons.None);

		Assert.Empty(output.Events);
	}

	[Fact]
	public async Task Mute_IsVolumeMute_WhenTheMicSettingIsOff()
	{
		settings.Update(s => s with { Lighting = new LightingSettings(MuteButtonMutesMic: false) });
		await ConnectAsync();
		Report(Buttons.Ps | Buttons.Options);
		Report(Buttons.None);

		Report(Buttons.Mute);
		Report(Buttons.None);

		Assert.Equal(["VolumeMute down", "VolumeMute up"], output.Events);
	}
}
