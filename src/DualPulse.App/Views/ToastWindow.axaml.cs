using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using DualPulse.App.Services;

namespace DualPulse.App.Views;

// A small notification in the bottom-right corner of the primary screen. It doesn't take focus, closes after six
// seconds (or the given time) or when clicked.
public partial class ToastWindow : Window
{
	private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(6);

	public ToastWindow()
	{
		InitializeComponent();
	}

	public ToastWindow(string title, string message, TimeSpan? lifetime = null) : this()
	{
		this.FindControl<TextBlock>("TitleText")!.Text = title;
		this.FindControl<TextBlock>("MessageText")!.Text = message;
		PointerPressed += (_, _) => Close();
		// The size is only known once SizeToContent has measured the content, so place the window then.
		SizeChanged += (_, e) => PlaceBottomRight(e.NewSize);
		DispatcherTimer.RunOnce(Close, lifetime ?? DefaultLifetime);
	}

	private void PlaceBottomRight(Size size)
	{
		if (Screens.Primary is not { } screen || size.Width <= 0)
		{
			return;
		}
		PixelRect area = screen.WorkingArea;
		double scale = screen.Scaling;
		int margin = (int)(16 * scale);
		PixelSize pixels = PixelSize.FromSize(size, scale);
		Position = new PixelPoint(area.Right - pixels.Width - margin, area.Bottom - pixels.Height - margin);
	}
}

// Shows notifications as ToastWindows on the UI thread.
public sealed class ToastNotifier : INotifier
{
	public void Show(string title, string message, TimeSpan? duration = null)
	{
		Dispatcher.UIThread.Post(() => new ToastWindow(title, message, duration).Show());
	}
}
