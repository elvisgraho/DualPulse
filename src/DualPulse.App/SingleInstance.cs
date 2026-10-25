namespace DualPulse.App;

// One DualPulse per user session: a second launch would open the controller again and create a second virtual
// pad. The second instance signals the first to show its window, then exits.
internal sealed class SingleInstance : IDisposable
{
	private readonly Mutex mutex;

	private readonly EventWaitHandle showSignal;

	private readonly RegisteredWaitHandle? registration;

	public SingleInstance(string name)
	{
		mutex = new Mutex(initiallyOwned: true, @"Local\" + name, out bool createdNew);
		IsFirst = createdNew;
		showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".show");
		if (IsFirst)
		{
			registration = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => ShowRequested?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
		}
	}

	public bool IsFirst { get; }

	// Raised on a thread-pool thread when another launch asks this instance to show itself.
	public event Action? ShowRequested;

	public void SignalFirst()
	{
		showSignal.Set();
	}

	public void Dispose()
	{
		registration?.Unregister(null);
		showSignal.Dispose();
		if (IsFirst)
		{
			mutex.ReleaseMutex();
		}
		mutex.Dispose();
	}
}
