using Avalonia;
using Microsoft.Extensions.Logging;

namespace DualPulse.App;

internal static class Program
{
	internal static SingleInstance? Instance { get; private set; }

	[STAThread]
	public static void Main(string[] args)
	{
		using SingleInstance instance = new("DualPulse-7f3c1e52");
		if (!instance.IsFirst)
		{
			instance.SignalFirst();
			return;
		}
		Instance = instance;
		// Anything that escapes is logged with its stack trace; UI-thread exceptions are handled in App.
		ILogger crash = Hosting.FileLogs.CreateLogger("Crash");
		AppDomain.CurrentDomain.UnhandledException += (_, e) => crash.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			crash.LogError(e.Exception, "Unobserved task exception");
			e.SetObserved();
		};
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	// Also used by the Avalonia previewer.
	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
	}
}
