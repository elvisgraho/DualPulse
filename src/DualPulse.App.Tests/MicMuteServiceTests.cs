using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class MicMuteServiceTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new();

	private readonly FakeMicrophone mic = new();

	private readonly ControllerService controller;

	private readonly MicMuteService micMute;

	private FakeConnection? connection;

	public MicMuteServiceTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		micMute = new MicMuteService(controller, settings, mic);
	}

	public void Dispose()
	{
		micMute.Dispose();
		controller.Dispose();
	}

	private async Task ConnectAsync()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		connection = device.Latest;
	}

	// Sends a USB report with Mute held or not and waits until the session has read it.
	private void Report(bool mute)
	{
		byte[] report = TestReports.Fixture("idle-usb");
		report[1 + 9] = (byte)((report[1 + 9] & ~0x04) | (mute ? 0x04 : 0));
		report[1 + 27] = (byte)(report[1 + 27] + 1);
		connection!.Feed(report);
		TestReports.WaitUntil(() => connection.PendingReports == 0 && controller.Session!.Latest?.Buttons.HasFlag(Buttons.Mute) == mute);
		Thread.Sleep(20);
	}

	[Fact]
	public async Task Press_TogglesTheMicrophoneOnce_EvenWhenHeld()
	{
		await ConnectAsync();

		Report(true);
		Report(true);
		Report(false);

		Assert.Equal(1, mic.Toggles);
		Assert.True(micMute.IsMicMuted);
	}

	[Fact]
	public async Task SecondPress_Unmutes()
	{
		await ConnectAsync();

		Report(true);
		Report(false);
		Report(true);

		Assert.Equal(2, mic.Toggles);
		Assert.False(micMute.IsMicMuted);
	}

	[Fact]
	public async Task SettingOff_LeavesTheMicrophoneAlone()
	{
		settings.Update(s => s with { Lighting = new LightingSettings(MuteButtonMutesMic: false) });
		await ConnectAsync();

		Report(true);

		Assert.Equal(0, mic.Toggles);
	}

	[Fact]
	public void Changed_ForwardsWindowsChanges()
	{
		int raised = 0;
		micMute.Changed += () => raised++;

		mic.SetFromWindows(true);

		Assert.Equal(1, raised);
		Assert.True(micMute.IsMicMuted);
	}
}
