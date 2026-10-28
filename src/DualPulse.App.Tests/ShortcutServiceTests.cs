using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ShortcutServiceTests : IDisposable
{
	private static readonly GameProfile Racing = new("Racing", GameProfile.NamedPrefix + "Racing");

	private static readonly GameProfile Shooter = new("Shooter", GameProfile.NamedPrefix + "Shooter");

	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

	private readonly FakeSettingsService settings = new(new AppSettings(Profiles: [Racing, Shooter]));

	private readonly FakeNotifier notifier = new();

	private readonly List<int> shownNumbers = [];

	private readonly PulseCounter rumble = new();

	private readonly ControllerService controller;

	private readonly ShortcutService shortcuts;

	private FakeConnection? connection;

	public ShortcutServiceTests()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		shortcuts = new ShortcutService(controller, settings, notifier, shownNumbers.Add, rumble);
	}

	public void Dispose()
	{
		shortcuts.Dispose();
		controller.Dispose();
	}

	private async Task ConnectAsync()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);
		TestReports.WaitUntil(() => controller.Session != null);
		connection = device.Latest;
	}

	private int reportNumber;

	// A USB input report with these buttons held (the D-pad is a hat value in the low nibble of byte 8).
	private void Report(Buttons buttons)
	{
		byte[] report = TestReports.Fixture("idle-usb");
		uint bits = (uint)buttons;
		int hat = (buttons & Buttons.DpadUp) != 0 ? 0 : (buttons & Buttons.DpadRight) != 0 ? 2 : (buttons & Buttons.DpadDown) != 0 ? 4
			: (buttons & Buttons.DpadLeft) != 0 ? 6 : 8;
		report[1 + 7] = (byte)(((bits & 0x0F) << 4) | (uint)hat);
		report[1 + 8] = (byte)((bits >> 4) & 0xFF);
		report[1 + 9] = (byte)((uint)(report[1 + 9] & 0xF8) | ((bits >> 12) & 0x07));
		report[1 + 27] = (byte)++reportNumber;
		connection!.Feed(report);
		TestReports.WaitUntil(() => connection.PendingReports == 0 && controller.Session!.Latest?.Buttons == buttons);
		Thread.Sleep(20);
	}

	private void Press(Buttons buttons)
	{
		Report(Buttons.Ps);
		Report(Buttons.Ps | buttons);
		Report(Buttons.None);
	}

	[Fact]
	public async Task PsAndRight_GoesThroughDefaultAndTheNamedProfiles()
	{
		await ConnectAsync();

		Press(Buttons.DpadRight);
		Assert.Equal(Racing.Exe, settings.Current.ChosenProfile);
		Press(Buttons.DpadRight);
		Assert.Equal(Shooter.Exe, settings.Current.ChosenProfile);
		Press(Buttons.DpadRight);
		Assert.Null(settings.Current.ChosenProfile);

		Assert.Equal([2, 3, 1], shownNumbers);
		Assert.Equal(["Profile: Racing", "Profile: Shooter", "Profile: Default"], notifier.Shown.Select(s => s.Title));
	}

	[Fact]
	public async Task PsAndLeft_GoesBack()
	{
		await ConnectAsync();

		Press(Buttons.DpadLeft);

		Assert.Equal(Shooter.Exe, settings.Current.ChosenProfile);
	}

	[Fact]
	public async Task WithoutNamedProfiles_ProfileShortcutsDoNothing()
	{
		settings.Update(s => s with { Profiles = [] });
		await ConnectAsync();

		Press(Buttons.DpadRight);

		Assert.Null(settings.Current.ChosenProfile);
		Assert.Empty(notifier.Shown);
	}

	[Fact]
	public async Task PsAndUpDown_SwitchTheTriggerPreset()
	{
		await ConnectAsync();

		Press(Buttons.DpadUp);
		Assert.Equal(TriggerPreset.Feedback, settings.Current.TriggerPreset);
		Assert.Equal(TriggerSettings.FromPreset(TriggerPreset.Feedback), settings.Current.Triggers);
		Press(Buttons.DpadDown);
		Press(Buttons.DpadDown);
		Assert.Equal(TriggerPreset.Vibration, settings.Current.TriggerPreset);

		Assert.Equal([2, 1, 4], shownNumbers);
		Assert.Equal("Triggers: Vibration", notifier.Shown[^1].Title);
	}

	[Fact]
	public async Task CustomTriggers_NextIsTheFirstPreset()
	{
		settings.Update(s => s with { TriggerPreset = TriggerPreset.Custom });
		await ConnectAsync();

		Press(Buttons.DpadUp);

		Assert.Equal(TriggerPreset.Off, settings.Current.TriggerPreset);
	}

	[Fact]
	public async Task SwitchedOff_NothingHappens()
	{
		settings.Update(s => s with { ControllerShortcuts = false });
		await ConnectAsync();

		Press(Buttons.DpadRight);
		Press(Buttons.DpadUp);

		Assert.Null(settings.Current.ChosenProfile);
		Assert.Equal(TriggerPreset.Off, settings.Current.TriggerPreset);
	}

	[Fact]
	public async Task OnTheCable_NoPulse()
	{
		// Classic rumble on the cable switches the controller's haptics off until it is power-cycled.
		await ConnectAsync();

		Press(Buttons.DpadRight);

		Assert.Equal(0, rumble.Pulses);
	}

	private sealed class PulseCounter : IRumbleService
	{
		public int Pulses { get; private set; }

		public (byte Left, byte Right) Levels => default;

		public void SetGameRumble(byte large, byte small)
		{
		}

		public void TestMotors(bool left, bool right)
		{
		}

		public void Pulse() => Pulses++;
	}
}
