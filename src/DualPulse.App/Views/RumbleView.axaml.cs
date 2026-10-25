using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Samples the motor levels about 30 times a second while the page is visible; bars only change when a motor does.
public partial class RumbleView : UserControl
{
	private readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

	public RumbleView()
	{
		InitializeComponent();
		meterTimer.Tick += (_, _) =>
		{
			if (IsEffectivelyVisible)
			{
				(DataContext as RumbleViewModel)?.RefreshMeters();
			}
		};
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		meterTimer.Start();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		meterTimer.Stop();
		base.OnDetachedFromVisualTree(e);
	}
}
