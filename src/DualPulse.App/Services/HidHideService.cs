using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DualPulse.Core.Devices;
using DualPulse.Emulation;

namespace DualPulse.App.Services;

public enum HidHideStatus
{
	NotInstalled,
	// The driver is present but not filtering HID devices, so hiding has no effect.
	NotAttached,
	Off,
	Hidden,
	Error
}

public interface IHidHideService
{
	HidHideStatus Status { get; }

	event Action? StatusChanged;
}

// Hides the DualSense from other apps and games through HidHide, so they only see the virtual controller. DualPulse
// only ever adds its own entries: itself to the allowed apps (always before hiding anything, so it never loses the
// controller) and the controller's device ids to the hidden devices. Turning hiding off removes exactly the devices it
// recorded in the settings; on every start the driver is brought in line with the setting, which also restores
// devices left hidden if DualPulse was killed.
public sealed class HidHideService : IHidHideService, IHostedService, IDisposable
{
	private readonly IHidHideDriver driver;

	private readonly IHidTransport transport;

	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly Func<string, string?> instanceIdOf;

	private readonly string appImageName;

	private readonly ILogger<HidHideService> logger;

	private readonly object gate = new();

	private bool applying;

	public HidHideService(IHidHideDriver driver, IHidTransport transport, IControllerService controller, ISettingsService settings,
		Func<string, string?> instanceIdOf, string appImageName, ILogger<HidHideService> logger)
	{
		this.driver = driver;
		this.transport = transport;
		this.controller = controller;
		this.settings = settings;
		this.instanceIdOf = instanceIdOf;
		this.appImageName = appImageName;
		this.logger = logger;
		settings.Changed += OnSettingsChanged;
		controller.SessionStarted += OnSessionStarted;
		// Devices come and go (the virtual pad's HID appears a moment after it is plugged in): hiding runs again.
		transport.DevicesChanged += OnDevicesChanged;
	}

	public HidHideStatus Status { get; private set; } = HidHideStatus.Off;

	public event Action? StatusChanged;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		Apply();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		settings.Changed -= OnSettingsChanged;
		controller.SessionStarted -= OnSessionStarted;
		transport.DevicesChanged -= OnDevicesChanged;
	}

	// Brings HidHide in line with the HideController setting.
	public void Apply()
	{
		HidHideStatus status;
		lock (gate)
		{
			if (applying)
			{
				return;
			}
			applying = true;
			try
			{
				if (!driver.IsInstalled)
				{
					status = HidHideStatus.NotInstalled;
				}
				else if (!driver.IsAttached)
				{
					// Hiding would have no effect; still clean up entries DualPulse made earlier.
					if (!settings.Current.HideController)
					{
						Unhide();
					}
					status = HidHideStatus.NotAttached;
				}
				else
				{
					status = settings.Current.HideController ? Hide() : Unhide();
				}
			}
			catch (Exception exception)
			{
				logger.LogWarning("HidHide configuration failed: {Error}", exception.Message);
				status = HidHideStatus.Error;
			}
			finally
			{
				applying = false;
			}
		}
		SetStatus(status);
	}

	// Caller holds gate.
	private HidHideStatus Hide()
	{
		// DualPulse's own virtual DualSense has the same ids; it is what games are meant to see. An earlier version hid
		// it too, so it is taken off the list if it is there.
		IReadOnlyList<HidDeviceInfo> devices = transport.Enumerate();
		List<string> ids = devices.Where(d => !ControllerDiscovery.IsOwnVirtual(d)).Select(d => instanceIdOf(d.Path)).OfType<string>().ToList();
		List<string> own = devices.Where(ControllerDiscovery.IsOwnVirtual).Select(d => instanceIdOf(d.Path)).OfType<string>()
			.Where(id => !ids.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
		IReadOnlyList<string> allowed = driver.GetAllowedApps();
		if (!allowed.Contains(appImageName, StringComparer.OrdinalIgnoreCase))
		{
			driver.SetAllowedApps([.. allowed, appImageName]);
			logger.LogInformation("HidHide: allowed {App}", appImageName);
		}
		IReadOnlyList<string> hidden = driver.GetHiddenDevices();
		List<string> unhidden = hidden.Where(id => own.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
		List<string> missing = ids.Where(id => !hidden.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
		if (missing.Count > 0 || unhidden.Count > 0)
		{
			driver.SetHiddenDevices([.. hidden.Except(unhidden, StringComparer.OrdinalIgnoreCase), .. missing]);
			if (unhidden.Count > 0)
			{
				logger.LogInformation("HidHide: unhid DualPulse's own virtual DualSense {Devices}", string.Join(", ", unhidden));
			}
			if (missing.Count > 0)
			{
				logger.LogInformation("HidHide: hid {Devices}", string.Join(", ", missing));
			}
		}
		if (!driver.GetActive())
		{
			driver.SetActive(true);
		}
		List<string> recorded = (settings.Current.HiddenDeviceIds ?? []).Union(ids, StringComparer.OrdinalIgnoreCase)
			.Except(own, StringComparer.OrdinalIgnoreCase).ToList();
		if (!recorded.SequenceEqual(settings.Current.HiddenDeviceIds ?? []))
		{
			settings.Update(s => s with { HiddenDeviceIds = recorded });
		}
		return HidHideStatus.Hidden;
	}

	// Caller holds gate.
	private HidHideStatus Unhide()
	{
		IReadOnlyList<string> ours = settings.Current.HiddenDeviceIds ?? [];
		if (ours.Count == 0)
		{
			return HidHideStatus.Off;
		}
		IReadOnlyList<string> hidden = driver.GetHiddenDevices();
		List<string> kept = hidden.Where(id => !ours.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
		if (kept.Count != hidden.Count)
		{
			driver.SetHiddenDevices(kept);
			logger.LogInformation("HidHide: unhid {Devices}", string.Join(", ", ours));
		}
		settings.Update(s => s with { HiddenDeviceIds = [] });
		return HidHideStatus.Off;
	}

	private void OnSettingsChanged(AppSettings current)
	{
		Apply();
	}

	private void OnDevicesChanged(object? sender, EventArgs e)
	{
		if (settings.Current.HideController)
		{
			Task.Run(Apply);
		}
	}

	private void OnSessionStarted(ControllerSession session)
	{
		if (settings.Current.HideController)
		{
			Apply();
		}
	}

	private void SetStatus(HidHideStatus status)
	{
		if (Status != status)
		{
			Status = status;
			StatusChanged?.Invoke();
		}
	}
}
