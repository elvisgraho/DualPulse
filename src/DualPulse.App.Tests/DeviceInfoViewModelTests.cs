using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.Core.Devices;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class DeviceInfoViewModelTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private readonly ControllerService controller;

	private string? copied;

	public DeviceInfoViewModelTests()
	{
		controller = new ControllerService(transport, time, new FakeSettingsService(), NullLogger<ControllerManager>.Instance);
	}

	public void Dispose()
	{
		controller.Dispose();
	}

	private DeviceInfoViewModel Create() => new(controller, time, text =>
	{
		copied = text;
		return Task.CompletedTask;
	});

	private static string Value(DeviceInfoViewModel info, string label) => info.Rows.Single(r => r.Label == label).Value;

	[Theory]
	[InlineData(20, "under a minute")]
	[InlineData(60, "1 min")]
	[InlineData(12 * 60 + 59, "12 min")]
	[InlineData(72 * 60, "1 h 12 min")]
	[InlineData(26 * 3600 + 5 * 60, "1 d 2 h")]
	public void Duration_ReadsPlainly(int seconds, string expected)
	{
		Assert.Equal(expected, DeviceInfoViewModel.Duration(TimeSpan.FromSeconds(seconds)));
	}

	[Fact]
	public void NoController_ShowsNoRows()
	{
		DeviceInfoViewModel info = Create();
		info.Refresh();

		Assert.Empty(info.Rows);
		Assert.False(info.CopyCommand.CanExecute(null));
	}

	[Fact]
	public async Task BluetoothController_ShowsEverything()
	{
		transport.Add(FakeHidTransport.BluetoothPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		time.Advance(TimeSpan.FromMinutes(72));

		DeviceInfoViewModel info = Create();
		await info.LoadAsync();

		Assert.Equal("Bluetooth", Value(info, "Connection"));
		Assert.Equal("12:34:56:78:9A:BC", Value(info, "Bluetooth address"));
		Assert.Equal("06.30", Value(info, "Firmware"));
		Assert.Equal("Jul 4 2025 10:38:40", Value(info, "Firmware build"));
		Assert.Equal("0x0811", Value(info, "Hardware"));
		Assert.Equal("1 h 12 min", Value(info, "Connected for"));
		Assert.Contains(info.Rows, r => r.Label == "Battery");
	}

	[Fact]
	public async Task UsbController_ShowsTheAddressToo()
	{
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		DeviceInfoViewModel info = Create();
		await info.LoadAsync();

		Assert.Equal("USB", Value(info, "Connection"));
		Assert.Equal("12:34:56:78:9A:BC", Value(info, "Bluetooth address"));
	}

	[Fact]
	public async Task Copy_PutsOneLinePerRow()
	{
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		DeviceInfoViewModel info = Create();
		await info.LoadAsync();

		await info.CopyCommand.ExecuteAsync(null);

		Assert.NotNull(copied);
		Assert.StartsWith("Connection: USB" + Environment.NewLine, copied);
		Assert.Contains("Bluetooth address: 12:34:56:78:9A:BC", copied);
		Assert.Equal(info.Rows.Count, copied!.Split(Environment.NewLine).Length);
	}
}
