using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Core.Devices;
using DualPulse.Core.Tests;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

// "Fix double input": a game that opened the controller before it was hidden keeps seeing it; reconnecting it closes
// every connection, and afterwards only the virtual controller is visible.
public sealed class ReconnectTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new(new AppSettings(Emulation: PadKind.DualSense, HideController: true));

	private readonly FakePowerService power = new();

	private readonly ControllerService controller;

	private readonly EmulationViewModel page;

	public ReconnectTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		page = new EmulationViewModel(settings, new FakeEmulationService(), new FakePadBackend(), new FakeHidHideService(), () => null,
			controller: controller, power: power);
	}

	public void Dispose() => controller.Dispose();

	private async Task ConnectAsync(string path)
	{
		transport.Add(path);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		TestReports.WaitUntil(() => controller.Session != null);
	}

	[Fact]
	public void Shown_OnlyWhileTheControllerIsHidden()
	{
		Assert.True(page.ShowReconnect);

		settings.Update(s => s with { HideController = false });

		Assert.False(page.ShowReconnect);
	}

	[Fact]
	public async Task OverBluetooth_TurnsItOff_AndSaysToPressPs()
	{
		await ConnectAsync(FakeHidTransport.BluetoothPath);

		await page.ReconnectCommand.ExecuteAsync(null);

		Assert.Single(power.Requests);
		Assert.Equal("Turned off. Press PS to turn it back on.", page.ReconnectStatus);
	}

	[Fact]
	public async Task OnTheCable_AsksToReplug()
	{
		await ConnectAsync(FakeHidTransport.UsbPath);

		await page.ReconnectCommand.ExecuteAsync(null);

		Assert.Empty(power.Requests);
		Assert.Equal("Unplug the cable and plug it back in.", page.ReconnectStatus);
	}

	[Fact]
	public async Task WithoutAController_SaysSo()
	{
		await page.ReconnectCommand.ExecuteAsync(null);

		Assert.Empty(power.Requests);
		Assert.Equal("Connect your controller first.", page.ReconnectStatus);
	}
}
