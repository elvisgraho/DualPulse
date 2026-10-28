using Avalonia.Controls;
using Avalonia.Threading;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Controller details; the values refresh every second while it is open.
public partial class DeviceInfoWindow : Window
{
	public DeviceInfoWindow()
	{
		InitializeComponent();
	}

	public DeviceInfoWindow(DeviceInfoViewModel info) : this()
	{
		DataContext = info;
		_ = info.LoadAsync();
		DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(1) };
		refresh.Tick += (_, _) => _ = info.LoadAsync();
		refresh.Start();
		Closed += (_, _) => refresh.Stop();
	}
}
