using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Core.Tests;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class ControllerServiceTests
{
	[Fact]
	public async Task Controller_AppliesTriggerPresetOnStartAndOnChange()
	{
		FakeHidTransport transport = new();
		FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
		FakeSettingsService settings = new(new AppSettings(TriggerPreset: TriggerPreset.Feedback));
		using ControllerService controller = new(transport, time, settings, NullLogger<ControllerManager>.Instance);
		int changes = 0;
		controller.SessionChanged += () => changes++;
		transport.Add(FakeHidTransport.UsbPath);

		await controller.StartAsync(TestContext.Current.CancellationToken);

		OutputState output = controller.Session!.Output;
		Assert.Equal(1, changes);
		Assert.True(output.LeftTrigger.Bytes.SequenceEqual(TriggerEffects.Feedback(3, 6).Bytes));
		Assert.True(output.RightTrigger.Bytes.SequenceEqual(TriggerEffects.Feedback(3, 6).Bytes));

		settings.Update(s => s with { TriggerPreset = TriggerPreset.Weapon });

		output = controller.Session.Output;
		Assert.True(output.LeftTrigger.Bytes.SequenceEqual(TriggerEffects.Weapon(2, 6, 8).Bytes));
		Assert.True(output.RightTrigger.Bytes.SequenceEqual(TriggerEffects.Weapon(2, 6, 8).Bytes));

		settings.Update(s => s with { TriggerPreset = TriggerPreset.Vibration });
		Assert.True(controller.Session.Output.RightTrigger.Bytes.SequenceEqual(TriggerEffects.Vibration(2, 6, 30).Bytes));
	}

	[Fact]
	public async Task Controller_UnlinkedTriggers_EachGetsItsOwnEffect()
	{
		FakeHidTransport transport = new();
		FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
		TriggerSettings triggers = new(Linked: false,
			Left: new TriggerEffectSettings(TriggerKind.Resistance, Start: 1, Strength: 4),
			Right: new TriggerEffectSettings(TriggerKind.Vibration, Start: 5, Strength: 7, Frequency: 60));
		FakeSettingsService settings = new(new AppSettings(TriggerPreset: TriggerPreset.Custom, Triggers: triggers));
		using ControllerService controller = new(transport, time, settings, NullLogger<ControllerManager>.Instance);
		transport.Add(FakeHidTransport.UsbPath);

		await controller.StartAsync(TestContext.Current.CancellationToken);

		OutputState output = controller.Session!.Output;
		Assert.True(output.LeftTrigger.Bytes.SequenceEqual(TriggerEffects.Feedback(1, 4).Bytes));
		Assert.True(output.RightTrigger.Bytes.SequenceEqual(TriggerEffects.Vibration(5, 7, 60).Bytes));
	}

	[Fact]
	public async Task Controller_ActiveProfileTriggers_AppliedWhileTheGameHasFocus()
	{
		FakeHidTransport transport = new();
		FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
		GameProfile game = new("Game", "game.exe", Triggers: TriggerSettings.FromPreset(TriggerPreset.Feedback));
		FakeSettingsService settings = new(new AppSettings(Profiles: [game]));
		FakeForegroundWatcher watcher = new();
		using ProfileService profiles = new(settings, watcher, "DualPulse.exe");
		using ControllerService controller = new(transport, time, settings, NullLogger<ControllerManager>.Instance, profiles: profiles);
		transport.Add(FakeHidTransport.UsbPath);
		await controller.StartAsync(TestContext.Current.CancellationToken);

		watcher.Focus("game.exe");
		Assert.True(controller.Session!.Output.LeftTrigger.Bytes.SequenceEqual(TriggerEffects.Feedback(3, 6).Bytes));

		watcher.Focus("explorer.exe");
		Assert.True(controller.Session!.Output.LeftTrigger.Bytes.SequenceEqual(TriggerEffect.Off.Bytes));
	}
}
