using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Samples the stick positions about 30 times a second while the page is visible, for the previews.
public partial class SticksView : UserControl
{
	private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

	public SticksView()
	{
		InitializeComponent();
		previewTimer.Tick += (_, _) =>
		{
			if (IsEffectivelyVisible)
			{
				(DataContext as SticksViewModel)?.RefreshPreview();
			}
		};
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		previewTimer.Start();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		previewTimer.Stop();
		base.OnDetachedFromVisualTree(e);
	}
}
