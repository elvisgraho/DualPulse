using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using DualPulse.App.Services;
using DualPulse.Core.Audio;
using DualPulse.Core.Mapping;
using DualPulse.Core.Power;
using DualPulse.Emulation;

namespace DualPulse.App.Platform;

// The OS-specific services, picked once at startup. Windows has real implementations; any other OS gets the
// Unsupported ones until it has its own, so the rest of the app runs unchanged (native controller features only).
internal static class PlatformServices
{
	public static IServiceCollection AddPlatformServices(this IServiceCollection services)
	{
		return OperatingSystem.IsWindows() ? services.AddWindowsPlatform() : services.AddUnsupportedPlatform();
	}

	[SupportedOSPlatform("windows")]
	public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
	{
		// ViGEm for Xbox 360 / DS4; the virtual DualSense is DualPulse's own, plugged in through usbip-win2.
		services.AddSingleton<IVirtualPadBackend>(s => new CompositePadBackend(new ViGEmBackend(), new UsbIpBackend(new UsbIpWinAttacher(), DualSenseLogger(s))));
		services.AddSingleton<IHidHideDriver, HidHideDriver>();
		services.AddSingleton<IForegroundWatcher, ForegroundWatcher>();
		services.AddSingleton<IRunningWindows, RunningWindows>();
		services.AddSingleton<IPcOutput, SendInputOutput>();
		services.AddSingleton<IMicrophone, WindowsMicrophone>();
		services.AddSingleton<ISystemAudioSource, WasapiLoopbackSource>();
		services.AddSingleton<ISystemAudioSourceFactory, WasapiLoopbackSourceFactory>();
		services.AddSingleton<IDefaultAudioOutput, WindowsDefaultAudioOutput>();
		services.AddSingleton<IControllerUsbAudio, WindowsControllerUsbAudio>();
		services.AddSingleton<DualPulse.Core.Steam.ISteamClient, WindowsSteamClient>();
		services.AddSingleton<IRunRegistry, RunRegistry>();
		services.AddSingleton<IBluetoothRadio, WindowsBluetoothRadio>();
		return services;
	}

	public static IServiceCollection AddUnsupportedPlatform(this IServiceCollection services)
	{
		// No virtual pads yet. The DualSense one would only need an attacher for the vhci-hcd driver's usbip tool.
		services.AddSingleton<IVirtualPadBackend>(new CompositePadBackend(new Unsupported.VirtualPadBackend(), new Unsupported.VirtualPadBackend()));
		services.AddSingleton<IHidHideDriver, Unsupported.HidHideDriver>();
		services.AddSingleton<IForegroundWatcher, Unsupported.ForegroundWatcher>();
		services.AddSingleton<IRunningWindows, Unsupported.RunningWindows>();
		services.AddSingleton<IPcOutput, Unsupported.PcOutput>();
		services.AddSingleton<IMicrophone, Unsupported.Microphone>();
		services.AddSingleton<ISystemAudioSource, Unsupported.SystemAudioSource>();
		services.AddSingleton<ISystemAudioSourceFactory, Unsupported.SystemAudioSourceFactory>();
		services.AddSingleton<IDefaultAudioOutput, Unsupported.DefaultAudioOutput>();
		services.AddSingleton<IControllerUsbAudio, Unsupported.ControllerUsbAudio>();
		services.AddSingleton<DualPulse.Core.Steam.ISteamClient, Unsupported.SteamClient>();
		services.AddSingleton<IRunRegistry, Unsupported.RunRegistry>();
		services.AddSingleton<IBluetoothRadio, Unsupported.BluetoothRadio>();
		return services;
	}

	private static Microsoft.Extensions.Logging.ILogger? DualSenseLogger(IServiceProvider services) =>
		services.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("VirtualDualSense");

	// The device instance id HidHide hides a controller by; null where there is no HidHide.
	public static string? HidHideInstanceIdOf(string interfacePath)
	{
		return OperatingSystem.IsWindows() ? HidHideDriver.InstanceIdOf(interfacePath) : null;
	}

	// DualPulse's own path as HidHide names allowed apps (\Device\HarddiskVolumeN\...); the plain path elsewhere.
	public static string AppImageName()
	{
		string path = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DualPulse.exe");
		return OperatingSystem.IsWindows() ? HidHideDriver.DosDeviceNameOf(path) ?? path : path;
	}

	// Whether the Steam client is running (its Steam Input can take the controller from games).
	public static bool SteamRunning()
	{
		System.Diagnostics.Process[] steam = System.Diagnostics.Process.GetProcessesByName("steam");
		foreach (System.Diagnostics.Process process in steam)
		{
			process.Dispose();
		}
		return steam.Length > 0;
	}

	// The installed ViGEmBus driver's version, shown on the emulation page; null when it is missing or off Windows.
	public static string? ViGEmBusVersion()
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}
		string driver = Path.Combine(Environment.SystemDirectory, "drivers", "ViGEmBus.sys");
		return File.Exists(driver) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(driver).FileVersion : null;
	}
}
