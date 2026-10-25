using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Re-checks the page (drivers, Steam running) every few seconds while it is visible.
public partial class EmulationView : UserControl
{
	private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(3) };

	public EmulationView()
	{
		InitializeComponent();
		clock.Tick += (_, _) =>
		{
			if (IsEffectivelyVisible)
			{
				(DataContext as EmulationViewModel)?.Refresh();
			}
		};
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		clock.Start();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		clock.Stop();
		base.OnDetachedFromVisualTree(e);
	}
}
