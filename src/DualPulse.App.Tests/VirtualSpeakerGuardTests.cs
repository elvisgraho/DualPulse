using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Audio;
using DualPulse.Core.Tests;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class VirtualSpeakerGuardTests : IDisposable
{
	private static readonly AudioDevice Speakers = new("speakers", "Speakers (Realtek)");

	private static readonly AudioDevice Headphones = new("headphones", "Headphones (USB)");

	private static readonly AudioDevice VirtualPad = new("pad", "Speakers (DualSense Wireless Controller)");

	private readonly FakeDefaultOutput output = new() { Current = Speakers };

	private readonly FakeSettingsService settings = new(new AppSettings(Emulation: PadKind.DualSense));

	private readonly FakeEmulationService emulation = new() { Status = EmulationStatus.Active };

	private readonly ListLogger<VirtualSpeakerGuard> logger = new();

	private VirtualSpeakerGuard? guard;

	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

	private ControllerService? controller;

	public void Dispose()
	{
		guard?.Dispose();
		controller?.Dispose();
	}

	private VirtualSpeakerGuard Start()
	{
		controller = new ControllerService(transport, time, settings, NullLogger<ControllerManager>.Instance);
		return guard = new VirtualSpeakerGuard(output, settings, emulation, logger, controller, time);
	}

	// Native mode, the PC's speakers as the output: the plain case of plugging the controller in.
	private VirtualSpeakerGuard StartNative()
	{
		settings.Update(s => s with { Emulation = null });
		emulation.Status = EmulationStatus.Off;
		return Start();
	}

	private async Task PlugInAsync(string path)
	{
		transport.Add(path);
		await controller!.StartAsync(TestContext.Current.CancellationToken);
		TestReports.WaitUntil(() => controller.Session != null);
	}

	[Fact]
	public async Task PluggingInByCable_WindowsMovingTheSoundToTheController_IsUndone()
	{
		StartNative();

		await PlugInAsync(FakeHidTransport.UsbPath);
		time.Advance(TimeSpan.FromSeconds(2));
		output.ChangeTo(VirtualPad);

		Assert.Equal(["speakers"], output.SetCalls);
	}

	[Fact]
	public async Task SoundMovedJustBeforeTheControllerConnects_IsUndoneWhenItConnects()
	{
		// The sound device can show up before the controller itself does.
		StartNative();
		output.ChangeTo(VirtualPad);
		time.Advance(TimeSpan.FromSeconds(2));

		await PlugInAsync(FakeHidTransport.UsbPath);

		TestReports.WaitUntil(() => output.SetCalls.Count == 1);
		Assert.Equal("speakers", output.SetCalls[0]);
	}

	[Fact]
	public async Task ChoosingTheControllerLater_IsLeftAlone()
	{
		StartNative();
		await PlugInAsync(FakeHidTransport.UsbPath);
		time.Advance(TimeSpan.FromSeconds(30));

		output.ChangeTo(VirtualPad);

		Assert.Empty(output.SetCalls);
	}

	[Fact]
	public async Task ChoosingTheControllerRightAfterBeingSwitchedBack_IsLeftAlone()
	{
		StartNative();
		await PlugInAsync(FakeHidTransport.UsbPath);
		output.ChangeTo(VirtualPad);

		time.Advance(TimeSpan.FromSeconds(1));
		output.ChangeTo(VirtualPad);

		Assert.Equal(["speakers"], output.SetCalls);
	}

	[Fact]
	public async Task ConnectingOverBluetooth_IsLeftAlone()
	{
		StartNative();
		await PlugInAsync(FakeHidTransport.BluetoothPath);

		output.ChangeTo(VirtualPad);

		Assert.Empty(output.SetCalls);
	}

	[Fact]
	public void WindowsMakingTheVirtualDualSenseTheOutput_IsUndone_WhileGameSoundStaysOnThePc()
	{
		// The virtual pad's speaker device carries the game's haptics too, so it can't go; but with game sound off, the
		// game's sound played there was lost and the user had to switch back to their speakers by hand.
		Start();

		output.ChangeTo(VirtualPad);

		Assert.Equal(["speakers"], output.SetCalls);
		Assert.Contains(logger.Entries, e => e.Message.Contains("Speakers (Realtek)", StringComparison.Ordinal));
	}

	[Fact]
	public void SwitchesBackToTheLastOutputTheUserHad()
	{
		Start();
		output.ChangeTo(Headphones);

		output.ChangeTo(VirtualPad);

		Assert.Equal(["headphones"], output.SetCalls);
	}

	[Fact]
	public void LeavesItAlone_WhenGameSoundOnTheControllerIsWanted()
	{
		settings.Update(s => s with { GameSoundOnController = true });
		Start();

		output.ChangeTo(VirtualPad);

		Assert.Empty(output.SetCalls);
	}

	[Fact]
	public void LeavesItAlone_WhenNoVirtualDualSenseIsRunning()
	{
		// A real DualSense on USB is a sound device of its own; choosing it is the user's business.
		emulation.Status = EmulationStatus.Off;
		Start();

		output.ChangeTo(VirtualPad);

		Assert.Empty(output.SetCalls);
	}

	[Fact]
	public void AlreadyTheOutputWhenThePadStarts_IsUndoneThen()
	{
		emulation.Status = EmulationStatus.Off;
		Start();
		output.ChangeTo(VirtualPad);

		emulation.Status = EmulationStatus.Active;
		emulation.RaiseStatusChanged();

		TestReports.WaitUntil(() => output.SetCalls.Count == 1);
		Assert.Equal("speakers", output.SetCalls[0]);
	}

	[Fact]
	public void NothingToGoBackTo_DoesNothing()
	{
		output.Current = VirtualPad;
		Start();

		output.ChangeTo(VirtualPad);

		Assert.Empty(output.SetCalls);
	}

	private sealed class FakeDefaultOutput : IDefaultAudioOutput
	{
		public AudioDevice? Current { get; set; }

		public List<string> SetCalls { get; } = [];

		public event Action? Changed;

		public bool Set(string deviceId)
		{
			lock (SetCalls)
			{
				SetCalls.Add(deviceId);
			}
			Current = deviceId switch
			{
				"speakers" => Speakers,
				"headphones" => Headphones,
				_ => Current
			};
			Changed?.Invoke();
			return true;
		}

		public void ChangeTo(AudioDevice device)
		{
			Current = device;
			Changed?.Invoke();
		}
	}
}
