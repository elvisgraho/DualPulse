using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class RumbleViewModelTests
{
	private readonly FakeSettingsService settings = new();

	private readonly FakeControllerService controller = new();

	private readonly FakeRumbleService rumble = new();

	private RumbleViewModel CreateViewModel() => new(settings, controller, rumble);

	[Fact]
	public void Rumble_ModeChoices()
	{
		Assert.Equal(["Automatic", "Classic", "Improved"], CreateViewModel().ModeChoices.Select(c => c.Label));
	}

	[Fact]
	public void Rumble_Changes_Persist()
	{
		RumbleViewModel viewModel = CreateViewModel();

		viewModel.Strength = 150;
		viewModel.Muted = true;
		viewModel.SelectedMode = viewModel.ModeChoices[1];

		Assert.Equal(new RumbleSettings(Strength: 150, Muted: true, Mode: RumbleMode.Classic), settings.Current.EffectiveRumble);
	}

	[Fact]
	public void Rumble_StrengthClampedTo0To200()
	{
		RumbleViewModel viewModel = CreateViewModel();

		viewModel.Strength = 500;

		Assert.Equal(200, settings.Current.EffectiveRumble.Strength);
	}

	[Fact]
	public void Rumble_TestButtons_CallTheService()
	{
		RumbleViewModel viewModel = CreateViewModel();

		viewModel.TestLeftCommand.Execute(null);
		viewModel.TestRightCommand.Execute(null);
		viewModel.TestBothCommand.Execute(null);

		Assert.Equal([(true, false), (false, true), (true, true)], rumble.Tests);
	}

	[Fact]
	public void Rumble_Meters_ZeroWithoutController()
	{
		RumbleViewModel viewModel = CreateViewModel();

		viewModel.RefreshMeters();

		Assert.Equal(0, viewModel.LeftLevel);
		Assert.Equal(0, viewModel.RightLevel);
		Assert.False(viewModel.CanTest);
	}

	[Fact]
	public void Rumble_BluetoothHapticsSwitch_Persists()
	{
		RumbleViewModel viewModel = CreateViewModel();
		Assert.True(viewModel.BluetoothHaptics);

		viewModel.BluetoothHaptics = false;

		Assert.False(settings.Current.EffectiveRumble.BluetoothHaptics);
	}

	[Fact]
	public void Rumble_Meters_FollowWhatIsPlaying()
	{
		RumbleViewModel viewModel = CreateViewModel();
		DualPulse.Core.Tests.FakeHidTransport transport = new();
		DualPulse.Core.Tests.FakeDevice device = transport.Add(DualPulse.Core.Tests.FakeHidTransport.BluetoothPath);
		using DualPulse.Core.Devices.ControllerSession session = new(device.Info, transport, DualPulse.Core.Protocol.BluetoothLayout.Sequenced,
			Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
		controller.Session = session;
		rumble.Levels = (255, 51);

		viewModel.RefreshMeters();

		Assert.Equal(1.0, viewModel.LeftLevel, 3);
		Assert.Equal(0.2, viewModel.RightLevel, 3);
	}
}
