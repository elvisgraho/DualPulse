namespace DualPulse.App.ViewModels;

// Runs view-model updates on the UI thread: inline when already there (or when there is no UI, as in tests), posted
// otherwise. Settings and service events can arrive on background threads, and bound properties must change on the
// UI thread. Create it on the UI thread, i.e. in the view model's constructor.
internal sealed class UiContext
{
	private readonly SynchronizationContext? context = SynchronizationContext.Current;

	public void Run(Action action)
	{
		if (context == null || SynchronizationContext.Current == context)
		{
			action();
		}
		else
		{
			context.Post(_ => action(), null);
		}
	}
}
