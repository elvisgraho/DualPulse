using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Refreshes the "last instruction ... ago" text once a second while the page is visible.
public partial class ModsView : UserControl
{
	private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };

	public ModsView()
	{
		InitializeComponent();
		clock.Tick += (_, _) =>
		{
			if (IsEffectivelyVisible)
			{
				(DataContext as ModsViewModel)?.Refresh();
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
