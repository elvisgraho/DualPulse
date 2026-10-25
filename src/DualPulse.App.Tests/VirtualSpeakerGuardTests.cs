using DualPulse.App.Services;
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

	public void Dispose() => guard?.Dispose();

	private VirtualSpeakerGuard Start() => guard = new VirtualSpeakerGuard(output, settings, emulation, logger);

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
