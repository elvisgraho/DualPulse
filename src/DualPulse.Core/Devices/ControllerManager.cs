using Microsoft.Extensions.Logging;
using DualPulse.Core.Protocol;

namespace DualPulse.Core.Devices;

// Keeps one controller session bound to the best available device. Rules verified on hardware: prefer USB over
// Bluetooth; rebind when the bound device vanishes, a preferred one appears, or the reader died; wait 1 s after any
// session ends before connecting again. Device-change events trigger a poll; a 250 ms timer is the fallback.
// Polls run one at a time, including their events, so SessionStarted/SessionEnded always arrive in order. A working
// session is only replaced once the preferred device has actually opened; a device that failed to open (e.g. held
// exclusively by another app) can't replace a working session for 30 s.
public sealed class ControllerManager : IDisposable
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

	private static readonly TimeSpan ReconnectBackoff = TimeSpan.FromSeconds(1);

	private static readonly TimeSpan FailedPathCooldown = TimeSpan.FromSeconds(30);

	private readonly IHidTransport transport;

	private readonly TimeProvider time;

	private readonly ILogger<ControllerManager> logger;

	private readonly BluetoothLayout layout;

	private readonly object gate = new();

	private readonly Dictionary<string, DateTimeOffset> failedUntil = [];

	private string? lastFailedPath;

	private string? lastPresence;

	private ITimer? timer;

	private DateTimeOffset reconnectNotBefore = DateTimeOffset.MinValue;

	private bool disposed;

	public ControllerManager(IHidTransport transport, TimeProvider time, ILogger<ControllerManager> logger, BluetoothLayout layout = BluetoothLayout.Sequenced)
	{
		this.transport = transport;
		this.time = time;
		this.logger = logger;
		this.layout = layout;
	}

	public ControllerSession? Current { get; private set; }

	// Why the last attempt to open a controller failed; null once a session is running.
	public string? OpenError { get; private set; }

	public event Action<ControllerSession>? SessionStarted;

	public event Action<ControllerSession, string>? SessionEnded;

	public void Start()
	{
		transport.DevicesChanged += OnDevicesChanged;
		timer = time.CreateTimer(_ => Poll(), null, PollInterval, PollInterval);
		Poll();
	}

	public void Poll()
	{
		// The whole poll, events included, holds the lock: overlapping polls (timer and device-change threads) can't
		// deliver SessionEnded for a session before its SessionStarted. Handlers must not block on another poll.
		lock (gate)
		{
			if (disposed)
			{
				return;
			}
			DateTimeOffset now = time.GetUtcNow();
			// DualPulse's own virtual DualSense is never a candidate.
			IReadOnlyList<HidDeviceInfo> devices = [.. transport.Enumerate().Where(d => !ControllerDiscovery.IsOwnVirtual(d))];
			LogPresenceChange(devices);
			string? preferredPath = ChooseCandidate(devices, now, skipRecentlyFailed: Current != null);
			if (Current != null)
			{
				string? reason = RebindReason(Current, devices, preferredPath);
				if (reason == null)
				{
					return;
				}
				ControllerSession old = Current;
				// Switching to a preferred device: open it first, so a device that won't open never costs the working one.
				ControllerSession? replacement = reason.StartsWith("preferred", StringComparison.Ordinal) ? TryStart(devices, preferredPath!, now) : null;
				if (reason.StartsWith("preferred", StringComparison.Ordinal) && replacement == null)
				{
					return;
				}
				logger.LogInformation("Ending session {Path}: {Reason}", old.Device.Path, reason);
				old.Stop(reason);
				Current = replacement;
				if (replacement == null)
				{
					reconnectNotBefore = now + ReconnectBackoff;
				}
				Raise(() => SessionEnded?.Invoke(old, reason));
				if (replacement != null)
				{
					Raise(() => SessionStarted?.Invoke(replacement));
				}
				return;
			}
			if (preferredPath != null && now >= reconnectNotBefore)
			{
				ControllerSession? session = TryStart(devices, preferredPath, now);
				if (session == null)
				{
					reconnectNotBefore = now + ReconnectBackoff;
					return;
				}
				Current = session;
				Raise(() => SessionStarted?.Invoke(session));
			}
		}
	}

	public void Dispose()
	{
		ControllerSession? session;
		lock (gate)
		{
			disposed = true;
			session = Current;
			Current = null;
		}
		transport.DevicesChanged -= OnDevicesChanged;
		timer?.Dispose();
		session?.Stop("manager disposed");
	}

	private static string? RebindReason(ControllerSession current, IReadOnlyList<HidDeviceInfo> devices, string? preferredPath)
	{
		if (!current.IsReading)
		{
			return "reader stopped";
		}
		if (devices.All(d => d.Path != current.Device.Path))
		{
			return "device disconnected";
		}
		// With several controllers the bound one stays as long as it is as good as the preferred choice.
		bool currentIsUsb = !ControllerDiscovery.IsBluetooth(current.Device.Path);
		bool preferredIsUsb = preferredPath != null && !ControllerDiscovery.IsBluetooth(preferredPath);
		if (!currentIsUsb && preferredIsUsb)
		{
			return "preferred device changed to " + preferredPath;
		}
		return null;
	}

	// While a session works, devices that failed to open in the last 30 s don't count as preferred (so they can't
	// end it); with no session every device is a candidate again after the 1 s backoff.
	private string? ChooseCandidate(IReadOnlyList<HidDeviceInfo> devices, DateTimeOffset now, bool skipRecentlyFailed)
	{
		IEnumerable<string> paths = devices.Select(d => d.Path);
		if (skipRecentlyFailed)
		{
			paths = paths.Where(p => !failedUntil.TryGetValue(p, out DateTimeOffset until) || now >= until);
		}
		return ControllerDiscovery.ChoosePreferredPath(paths);
	}

	// Caller holds gate.
	private ControllerSession? TryStart(IReadOnlyList<HidDeviceInfo> devices, string path, DateTimeOffset now)
	{
		HidDeviceInfo device = devices.First(d => d.Path == path);
		ControllerSession session = new(device, transport, layout, logger, time);
		try
		{
			session.Start();
		}
		catch (Exception exception)
		{
			session.Dispose();
			failedUntil[path] = now + FailedPathCooldown;
			OpenError = exception.Message;
			// One warning per device until it opens; repeats only at debug level.
			if (lastFailedPath != path)
			{
				lastFailedPath = path;
				logger.LogWarning("Could not open {Path}: {Error}", path, exception.Message);
			}
			else
			{
				logger.LogDebug("Could not open {Path}: {Error}", path, exception.Message);
			}
			return null;
		}
		failedUntil.Remove(path);
		lastFailedPath = null;
		OpenError = null;
		logger.LogInformation("Connected {Path}", path);
		return session;
	}

	// One line whenever the set of connected controllers changes, so the log shows when Windows reported a device
	// arriving or leaving relative to the session events.
	private void LogPresenceChange(IReadOnlyList<HidDeviceInfo> devices)
	{
		string presence = devices.Count == 0
			? "none"
			: string.Join(", ", devices.Select(d => ControllerDiscovery.IsBluetooth(d.Path) ? "Bluetooth" : "USB").OrderBy(t => t == "USB" ? 0 : 1));
		if (presence == lastPresence || (lastPresence == null && devices.Count == 0))
		{
			lastPresence ??= presence;
			return;
		}
		lastPresence = presence;
		logger.LogInformation("Controllers present: {Presence}", presence);
	}

	private void Raise(Action raise)
	{
		try
		{
			raise();
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Session event handler failed");
		}
	}

	private void OnDevicesChanged(object? sender, EventArgs e)
	{
		Poll();
	}
}
