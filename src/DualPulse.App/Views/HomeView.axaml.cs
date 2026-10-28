using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Refreshes the status values every 500 ms; the timer keeps running while the window is hidden so the tray tooltip
// stays current. Live input reaches the view model without polling (see HomeViewModel).
public partial class HomeView : UserControl
{
	private readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

	public HomeView()
	{
		InitializeComponent();
		statusTimer.Tick += (_, _) => (DataContext as HomeViewModel)?.Refresh();
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		statusTimer.Start();
	}

	private void OnDetails(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if ((DataContext as HomeViewModel)?.CreateDeviceInfo() is { } info && TopLevel.GetTopLevel(this) is Window owner)
		{
			_ = new DeviceInfoWindow(info).ShowDialog(owner);
		}
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		statusTimer.Stop();
		base.OnDetachedFromVisualTree(e);
	}
}
