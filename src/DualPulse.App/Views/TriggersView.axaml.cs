using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Samples the trigger positions about 30 times a second while the page is visible; the view model only updates the
// bars when a trigger moved.
public partial class TriggersView : UserControl
{
	private readonly DispatcherTimer travelTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

	public TriggersView()
	{
		InitializeComponent();
		travelTimer.Tick += (_, _) =>
		{
			if (IsEffectivelyVisible)
			{
				(DataContext as TriggersViewModel)?.RefreshTravel();
			}
		};
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		travelTimer.Start();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		travelTimer.Stop();
		base.OnDetachedFromVisualTree(e);
	}
}
