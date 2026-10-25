using System.ComponentModel;
using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using Xunit;

namespace DualPulse.App.Tests;

// Settings can change on a background thread (e.g. HidHide recording devices when a controller connects); pages must
// raise their property changes on the UI thread only.
public sealed class UiThreadTests
{
	private sealed class RecordingContext : SynchronizationContext
	{
		private readonly Queue<(SendOrPostCallback Callback, object? State)> pending = new();

		public override void Post(SendOrPostCallback d, object? state)
		{
			lock (pending)
			{
				pending.Enqueue((d, state));
			}
		}

		public void RunPending()
		{
			while (true)
			{
				(SendOrPostCallback Callback, object? State) item;
				lock (pending)
				{
					if (pending.Count == 0)
					{
						return;
					}
					item = pending.Dequeue();
				}
				item.Callback(item.State);
			}
		}
	}

	[Fact]
	public void Pages_SettingsChangedOnAnotherThread_RaiseChangesOnlyOnTheUiContext()
	{
		FakeSettingsService settings = new();
		FakeControllerService controller = new();
		RecordingContext ui = new();
		SynchronizationContext? previous = SynchronizationContext.Current;
		List<INotifyPropertyChanged> pages;
		SynchronizationContext.SetSynchronizationContext(ui);
		try
		{
			pages =
			[
				new LightingViewModel(settings, controller),
				new TriggersViewModel(settings, controller),
				new RumbleViewModel(settings, controller, new FakeRumbleService()),
				new EmulationViewModel(settings, new FakeEmulationService(), new FakePadBackend(), new FakeHidHideService(), () => null, _ => { }),
				new HomeViewModel(controller, new FakeEmulationService(), settings, new FakePowerService())
			];
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(previous);
		}
		int raised = 0;
		pages.ForEach(p => p.PropertyChanged += (_, _) => Interlocked.Increment(ref raised));

		Thread background = new(() => settings.Update(s => s with { HideController = true, Lightbar = new Core.Protocol.Rgb(1, 2, 3) }));
		background.Start();
		background.Join();

		Assert.Equal(0, raised);
		ui.RunPending();
		Assert.True(raised > 0);
	}
}
