using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DualPulse.App.Logging;
using DualPulse.App.Platform;
using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.App.Views;
using DualPulse.Core.Devices;
using DualPulse.Core.Mapping;
using DualPulse.Emulation;

namespace DualPulse.App;

// Composition root: every service is a singleton that lives as long as the app.
internal static class Hosting
{
	public static string DataFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DualPulse");

	// Before the rename to DualPulse.
	private static string PreviousDataFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Resonance");

	// This build's version (from the project, or the release tag on GitHub builds).
	private static Version CurrentVersion { get; } = Version.TryParse(
		(typeof(Hosting).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "").Split('+', '-')[0],
		out Version? version) ? version : new Version(0, 0);

	public static string LogsFolder { get; } = Path.Combine(DataFolder, "logs");

	// One provider for the host and for crash logging, so both append to the same file through one lock.
	public static FileLoggerProvider FileLogs { get; } = new(LogsFolder);

	public static IHost Build()
	{
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
		builder.Services.AddLogging(logging =>
		{
			logging.SetMinimumLevel(LogLevel.Information);
			logging.AddProvider(FileLogs);
		});
		builder.Services.AddSingleton(TimeProvider.System);
		builder.Services.AddSingleton<IHidTransport, HidSharpTransport>();
		builder.Services.AddPlatformServices();
		builder.Services.AddSingleton<ISettingsService>(_ => new SettingsService(DataFolder, PreviousDataFolder));
		builder.Services.AddSingleton(s => new ProfileService(s.GetRequiredService<ISettingsService>(), s.GetRequiredService<IForegroundWatcher>(),
			Path.GetFileName(Environment.ProcessPath ?? "DualPulse.exe")));
		builder.Services.AddSingleton<IProfileService>(s => s.GetRequiredService<ProfileService>());
		builder.Services.AddSingleton<ControllerService>();
		builder.Services.AddSingleton<IControllerService>(s => s.GetRequiredService<ControllerService>());
		builder.Services.AddSingleton<VirtualMicrophoneMonitor>();
		builder.Services.AddSingleton(s => new SteamInputWatch(s.GetRequiredService<DualPulse.Core.Steam.ISteamClient>(), s.GetRequiredService<IControllerService>(),
			s.GetRequiredService<INotifier>(), s.GetRequiredService<TimeProvider>()));
		builder.Services.AddSingleton<EmulationService>();
		builder.Services.AddSingleton<IEmulationService>(s => s.GetRequiredService<EmulationService>());
		builder.Services.AddSingleton<ModOverride>();
		builder.Services.AddSingleton<PcControlService>();
		builder.Services.AddSingleton<IPcControlService>(s => s.GetRequiredService<PcControlService>());
		builder.Services.AddSingleton<GyroService>();
		builder.Services.AddSingleton<IGyroService>(s => s.GetRequiredService<GyroService>());
		builder.Services.AddSingleton<MicMuteService>();
		builder.Services.AddSingleton<IMicMuteService>(s => s.GetRequiredService<MicMuteService>());
		builder.Services.AddSingleton<LightingService>();
		builder.Services.AddSingleton(s => new ShortcutService(s.GetRequiredService<IControllerService>(), s.GetRequiredService<ISettingsService>(),
			s.GetRequiredService<INotifier>(), s.GetRequiredService<LightingService>().ShowNumber, s.GetRequiredService<IRumbleService>()));
		builder.Services.AddSingleton<RumbleService>();
		builder.Services.AddSingleton<AudioHapticsService>();
		builder.Services.AddSingleton<ControllerAudioService>();
		builder.Services.AddSingleton<VirtualSpeakerGuard>();
		builder.Services.AddSingleton<IRumbleService>(s => s.GetRequiredService<RumbleService>());
		builder.Services.AddSingleton(s => new HidHideService(s.GetRequiredService<IHidHideDriver>(), s.GetRequiredService<IHidTransport>(),
			s.GetRequiredService<IControllerService>(), s.GetRequiredService<ISettingsService>(), PlatformServices.HidHideInstanceIdOf, PlatformServices.AppImageName(),
			s.GetRequiredService<ILogger<HidHideService>>()));
		builder.Services.AddSingleton<IHidHideService>(s => s.GetRequiredService<HidHideService>());
		builder.Services.AddSingleton(s => new ModServerService(s.GetRequiredService<IControllerService>(), s.GetRequiredService<ISettingsService>(),
			s.GetRequiredService<ModOverride>(), s.GetRequiredService<TimeProvider>(), s.GetRequiredService<ILogger<ModServerService>>(),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSX", "DSX_UDP_PortNumber.txt"),
			@"C:\Temp\DualSenseX\DualSenseX_PortNumber.txt"));
		builder.Services.AddSingleton<IModServer>(s => s.GetRequiredService<ModServerService>());
		builder.Services.AddSingleton<PowerService>();
		builder.Services.AddSingleton<IPowerService>(s => s.GetRequiredService<PowerService>());
		// Hosted services start in this order and stop in reverse: power-off on exit runs while the session is
		// still bound, and the pad is released last.
		builder.Services.AddHostedService(s => s.GetRequiredService<ProfileService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<EmulationService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<MicMuteService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<SteamInputWatch>());
		builder.Services.AddHostedService(s => s.GetRequiredService<LightingService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<ShortcutService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<RumbleService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<AudioHapticsService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<ControllerAudioService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<VirtualSpeakerGuard>());
		builder.Services.AddHostedService(s => s.GetRequiredService<ControllerService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<HidHideService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<ModServerService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<PcControlService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<GyroService>());
		builder.Services.AddSingleton<INotifier, ToastNotifier>();
		builder.Services.AddSingleton<IReleaseFeed>(_ => new GitHubReleaseFeed(CurrentVersion));
		builder.Services.AddSingleton(s => new UpdateService(s.GetRequiredService<ISettingsService>(), s.GetRequiredService<IReleaseFeed>(),
			s.GetRequiredService<INotifier>(), s.GetRequiredService<TimeProvider>(), CurrentVersion));
		builder.Services.AddSingleton<IUpdateService>(s => s.GetRequiredService<UpdateService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<UpdateService>());
		builder.Services.AddSingleton<BatteryAlertService>();
		builder.Services.AddHostedService(s => s.GetRequiredService<BatteryAlertService>());
		builder.Services.AddSingleton(s => new StartupService(s.GetRequiredService<IRunRegistry>(), s.GetRequiredService<ISettingsService>(),
			Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "DualPulse.exe")));
		builder.Services.AddHostedService(s => s.GetRequiredService<StartupService>());
		builder.Services.AddHostedService(s => s.GetRequiredService<PowerService>());
		builder.Services.AddSingleton<ProfileSelection>();
		builder.Services.AddSingleton(s => new FirstStartViewModel(s.GetRequiredService<ISettingsService>(), s.GetRequiredService<IVirtualPadBackend>(),
			s.GetRequiredService<IHidHideService>()));
		builder.Services.AddSingleton(s => new HomeViewModel(s.GetRequiredService<IControllerService>(), s.GetRequiredService<IEmulationService>(),
			s.GetRequiredService<ISettingsService>(), s.GetRequiredService<IPowerService>(), s.GetService<IPcControlService>(), s.GetRequiredService<TimeProvider>(),
			() => new DeviceInfoViewModel(s.GetRequiredService<IControllerService>(), s.GetRequiredService<TimeProvider>(), CopyToClipboard),
			s.GetRequiredService<SteamInputWatch>()));
		builder.Services.AddSingleton<LightingViewModel>();
		builder.Services.AddSingleton<TriggersViewModel>();
		builder.Services.AddSingleton<RumbleViewModel>();
		builder.Services.AddSingleton<GyroViewModel>();
		builder.Services.AddSingleton<SticksViewModel>();
		builder.Services.AddSingleton<AudioViewModel>();
		builder.Services.AddSingleton<ModsViewModel>();
		builder.Services.AddSingleton<MicrophoneViewModel>();
		builder.Services.AddSingleton<ControllerPageViewModel>();
		builder.Services.AddSingleton<MappingViewModel>();
		builder.Services.AddSingleton(s => new EmulationViewModel(s.GetRequiredService<ISettingsService>(), s.GetRequiredService<IEmulationService>(),
			s.GetRequiredService<IVirtualPadBackend>(), s.GetRequiredService<IHidHideService>(), PlatformServices.ViGEmBusVersion,
			steamRunning: PlatformServices.SteamRunning));
		builder.Services.AddSingleton(s => new Diagnostics(s.GetRequiredService<ISettingsService>(), s.GetRequiredService<IControllerService>(),
			s.GetRequiredService<IEmulationService>(), s.GetRequiredService<IVirtualPadBackend>(), s.GetRequiredService<IHidHideService>(),
			PlatformServices.ViGEmBusVersion, LogsFolder, CurrentVersion.ToString(3), System.Runtime.InteropServices.RuntimeInformation.OSDescription));
		builder.Services.AddSingleton(s => new SettingsViewModel(s.GetRequiredService<ISettingsService>(), LogsFolder, updates: s.GetRequiredService<IUpdateService>(),
			diagnostics: s.GetRequiredService<Diagnostics>().Build, copy: CopyToClipboard));
		builder.Services.AddSingleton<ShellViewModel>();
		return builder.Build();
	}

	// Through DualPulse's own window (the app keeps it itself; the desktop lifetime's MainWindow stays unset).
	private static async Task CopyToClipboard(string text)
	{
		Avalonia.Input.Platform.IClipboard clipboard =
			(Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
			?.Windows.OfType<ShellWindow>().FirstOrDefault()?.Clipboard
			?? throw new InvalidOperationException("No window to copy from");
		await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
	}
}
