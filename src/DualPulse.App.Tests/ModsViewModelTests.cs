using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ModsViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakeModServer server = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private ModsViewModel CreateViewModel() => new(settings, server, time);

	[Fact]
	public void Toggles_AndPort_Persist()
	{
		ModsViewModel viewModel = CreateViewModel();

		viewModel.Enabled = true;
		viewModel.PortText = "7000";
		viewModel.WritePortFiles = false;

		Assert.Equal(new ModServerSettings(Enabled: true, Port: 7000, WritePortFiles: false), settings.Current.EffectiveModServer);
		Assert.False(viewModel.PortInvalid);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("70000")]
	[InlineData("abc")]
	public void Port_InvalidIsFlagged_AndNotSaved(string text)
	{
		ModsViewModel viewModel = CreateViewModel();

		viewModel.PortText = text;

		Assert.True(viewModel.PortInvalid);
		Assert.Equal(6969, settings.Current.EffectiveModServer.Port);
	}

	[Theory]
	[InlineData(ModServerState.Off, "Off")]
	[InlineData(ModServerState.Listening, "Ready for mods (port 6969)")]
	[InlineData(ModServerState.PortInUse, "Port 6969 is used by another program")]
	[InlineData(ModServerState.Error, "Couldn't start. See the log.")]
	public void StatusText(ModServerState state, string text)
	{
		server.State = state;
		server.BoundPort = 6969;
		ModsViewModel viewModel = CreateViewModel();

		viewModel.Refresh();

		Assert.Equal(text, viewModel.StatusText);
	}

	[Fact]
	public void Activity_ShowsLastPacketAndRecentInstructions()
	{
		ModsViewModel viewModel = CreateViewModel();
		viewModel.Refresh();
		Assert.Equal("No mod commands yet", viewModel.LastPacketText);

		server.LastPacketAt = time.GetUtcNow() - TimeSpan.FromSeconds(5);
		server.PacketsReceived = 12;
		server.RejectedInstructions = 1;
		server.RecentInstructions = ["R2: Feedback (1, 8)", "Lightbar #00FF00"];
		server.ModActive = true;
		viewModel.Refresh();

		Assert.Equal("Last mod command 5 s ago", viewModel.LastPacketText);
		Assert.Equal("12 packets, 1 rejected", viewModel.CountsText);
		Assert.Equal(["R2: Feedback (1, 8)", "Lightbar #00FF00"], viewModel.RecentInstructions);
		Assert.True(viewModel.ModActive);
	}

	[Fact]
	public void GiveControlBack_CallsTheServer()
	{
		server.ModActive = true;
		ModsViewModel viewModel = CreateViewModel();

		viewModel.GiveControlBackCommand.Execute(null);

		Assert.Equal(1, server.GiveBackCalls);
	}
}
