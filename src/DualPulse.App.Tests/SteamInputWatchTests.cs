using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Core.Devices;
using DualPulse.Core.Steam;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class SteamInputWatchTests : IDisposable
{
	private static readonly SteamApp Dune = new(1605220, "Dune: Spice Wars", "D4X");

	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new();

	private readonly FakeSteam steam = new();

	private readonly FakeNotifier notifier = new();

	private readonly ControllerService controller;

	private readonly SteamInputWatch watch;

	public SteamInputWatchTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		watch = new SteamInputWatch(steam, controller, notifier, time);
	}

	public void Dispose()
	{
		watch.Dispose();
		controller.Dispose();
	}

	private async Task ConnectAsync()
	{
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		TestReports.WaitUntil(() => controller.Session != null);
	}

	private void Tick() => time.Advance(SteamInputWatch.CheckInterval);

	[Fact]
	public async Task SteamInputOnForTheGameInFront_NamesIt_AndSaysSoOnce()
	{
		await ConnectAsync();
		steam.Game = Dune;
		steam.InputOn = true;

		Tick();
		Tick();

		Assert.True(watch.Active);
		Assert.Equal(Dune, watch.Game);
		(string title, string message) = Assert.Single(notifier.Shown);
		Assert.Equal("Steam Input is on for Dune: Spice Wars", title);
		Assert.Equal("It can take your adaptive triggers and lights. Turn it off in Steam: the game's Properties, Controller.", message);
	}

	[Fact]
	public async Task SteamInputGoes_ClearsIt()
	{
		await ConnectAsync();
		steam.Game = Dune;
		steam.InputOn = true;
		Tick();

		steam.InputOn = false;
		Tick();

		Assert.False(watch.Active);
		Assert.Null(watch.Game);
	}

	[Fact]
	public async Task LookingAtAnotherWindow_KeepsTheGame()
	{
		// Switching to DualPulse to read the notice doesn't make the game unknown.
		await ConnectAsync();
		steam.Game = Dune;
		steam.InputOn = true;
		Tick();

		steam.Game = null;
		Tick();

		Assert.Equal(Dune, watch.Game);
	}

	[Fact]
	public async Task GameNotFromTheSteamLibrary_SaysThisGame()
	{
		await ConnectAsync();
		steam.InputOn = true;

		Tick();

		Assert.True(watch.Active);
		Assert.Equal("Steam Input is on for this game", Assert.Single(notifier.Shown).Title);
	}

	[Fact]
	public void WithoutAController_StaysQuiet()
	{
		steam.Game = Dune;
		steam.InputOn = true;

		Tick();

		Assert.False(watch.Active);
		Assert.Empty(notifier.Shown);
	}

	[Fact]
	public async Task SteamClosed_StaysQuiet()
	{
		await ConnectAsync();
		steam.Running = false;
		steam.InputOn = true;

		Tick();

		Assert.False(watch.Active);
	}

	[Fact]
	public async Task Home_ShowsIt_AndOpensTheGameInSteam()
	{
		List<string> opened = [];
		HomeViewModel home = new(controller, new FakeEmulationService(), settings, new FakePowerService(), steamInput: watch, open: opened.Add);
		await ConnectAsync();
		steam.Game = Dune;
		steam.InputOn = true;

		Tick();
		home.Refresh();
		home.OpenInSteamCommand.Execute(null);

		Assert.True(home.ShowSteamInput);
		Assert.Equal("Steam Input is on for Dune: Spice Wars. It can take your triggers and lights.", home.SteamInputText);
		Assert.Equal(["steam://nav/games/details/1605220"], opened);
	}

	private sealed class FakeSteam : ISteamClient
	{
		public bool Running { get; set; } = true;

		public bool InputOn { get; set; }

		public SteamApp? Game { get; set; }

		public bool IsRunning() => Running;

		public bool InputActive() => InputOn;

		public SteamApp? ForegroundGame() => Game;
	}
}
