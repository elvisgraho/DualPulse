using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DualPulse.App.Services;
using DualPulse.App.ViewModels;
using DualPulse.App.Views;

namespace DualPulse.App;

// Closing the window hides it to the tray; only Quit ends the app.
public partial class App : Application
{
	private IHost? host;

	private ShellWindow? window;

	private bool quitting;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			IHost started = Hosting.Build();
			host = started;
			// An exception from a command or binding is logged and the app keeps running.
			ILogger<App> logger = started.Services.GetRequiredService<ILogger<App>>();
			Dispatcher.UIThread.UnhandledException += (_, e) =>
			{
				logger.LogError(e.Exception, "Unhandled UI exception");
				e.Handled = true;
			};
			started.Start();
			window = new ShellWindow { DataContext = started.Services.GetRequiredService<ShellViewModel>() };
			BindTray(started.Services.GetRequiredService<HomeViewModel>());
			window.Closing += OnWindowClosing;
			if (Program.Instance is { } instance)
			{
				instance.ShowRequested += () => Dispatcher.UIThread.Post(ShowWindow);
			}
			bool minimized = started.Services.GetRequiredService<ISettingsService>().Current.StartMinimized
				|| StartupService.StartedMinimized(desktop.Args ?? []);
			// Development aid: shows a sample notification to check how it looks.
			if ((desktop.Args ?? []).Contains("--preview-notification", StringComparer.OrdinalIgnoreCase))
			{
				started.Services.GetRequiredService<INotifier>().Show("Controller battery", "Controller battery low (20%).");
			}
			if (!minimized)
			{
				window.Show();
			}
			desktop.Exit += (_, _) =>
			{
				started.StopAsync().GetAwaiter().GetResult();
				started.Dispose();
			};
		}
		base.OnFrameworkInitializationCompleted();
	}

	// Tray tooltip and menu: a status line, the emulation switch, turning the controller off (Bluetooth), Open and
	// Quit. Items follow the Home view model, which refreshes every 500 ms.
	private void BindTray(HomeViewModel home)
	{
		if (TrayIcon.GetIcons(this) is not [TrayIcon tray, ..])
		{
			return;
		}
		NativeMenuItem status = new() { IsEnabled = false };
		NativeMenuItem emulation = new("Emulation") { ToggleType = MenuItemToggleType.CheckBox };
		emulation.Click += (_, _) => home.ToggleEmulationCommand.Execute(null);
		NativeMenuItem pcControl = new("PC control (PS + Options)") { ToggleType = MenuItemToggleType.CheckBox };
		pcControl.Click += (_, _) => home.TogglePcControlCommand.Execute(null);
		NativeMenuItem powerOff = new("Turn controller off");
		powerOff.Click += (_, _) => home.PowerOffCommand.Execute(null);
		NativeMenuItem open = new("Open");
		open.Click += OnOpenClicked;
		NativeMenuItem quit = new("Quit");
		quit.Click += OnQuitClicked;
		tray.Menu = new NativeMenu { Items = { status, new NativeMenuItemSeparator(), emulation, pcControl, powerOff, new NativeMenuItemSeparator(), open, quit } };

		void Update()
		{
			tray.ToolTipText = home.TrayToolTip;
			status.Header = home.TrayStatus;
			emulation.IsChecked = home.EmulationOn;
			pcControl.IsChecked = home.PcControlOn;
			powerOff.IsEnabled = home.PowerOffCommand.CanExecute(null);
		}

		Update();
		home.PropertyChanged += (_, _) => Update();
		home.PowerOffCommand.CanExecuteChanged += (_, _) => Update();
	}

	private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
	{
		if (CloseBehavior.HideToTray(e.CloseReason, e.IsProgrammatic, quitting))
		{
			e.Cancel = true;
			window?.Hide();
		}
	}

	private void ShowWindow()
	{
		if (window == null)
		{
			return;
		}
		window.Show();
		if (window.WindowState == WindowState.Minimized)
		{
			window.WindowState = WindowState.Normal;
		}
		window.Activate();
	}

	private void OnTrayClicked(object? sender, EventArgs e)
	{
		ShowWindow();
	}

	private void OnOpenClicked(object? sender, EventArgs e)
	{
		ShowWindow();
	}

	private void OnQuitClicked(object? sender, EventArgs e)
	{
		quitting = true;
		(ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
	}
}
