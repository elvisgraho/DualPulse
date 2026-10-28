using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Refreshes the waveforms 20 times a second. The controller's microphone is only recorded while this page is on
// screen in a visible window (not while the app sits in the tray).
public partial class MicrophoneView : UserControl
{
	private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };

	private bool running;

	public MicrophoneView()
	{
		InitializeComponent();
		timer.Tick += (_, _) => Tick();
	}

	private void Tick()
	{
		if (DataContext is not MicrophoneViewModel page)
		{
			return;
		}
		bool shown = IsEffectivelyVisible && TopLevel.GetTopLevel(this) is Window { IsVisible: true } window && window.WindowState != WindowState.Minimized;
		if (shown != running)
		{
			running = shown;
			if (shown)
			{
				page.Start();
			}
			else
			{
				page.Stop();
			}
		}
		if (shown)
		{
			page.Refresh();
		}
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		timer.Start();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		timer.Stop();
		if (running && DataContext is MicrophoneViewModel page)
		{
			running = false;
			page.Stop();
		}
		base.OnDetachedFromVisualTree(e);
	}
}
