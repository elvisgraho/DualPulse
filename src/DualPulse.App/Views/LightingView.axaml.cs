using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Refreshes the lightbar preview 10 times a second while the page is visible, so animated modes show live.
public partial class LightingView : UserControl
{
	private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

	public LightingView()
	{
		InitializeComponent();
		previewTimer.Tick += (_, _) =>
		{
			if (IsEffectivelyVisible)
			{
				(DataContext as LightingViewModel)?.RefreshPreview();
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
