using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Refreshes the waveforms 30 times a second, only while this page is on screen in a visible, non-minimized window:
// the timer and the controller's microphone start when it appears and stop when it goes (another tab or page, the
// window hidden in the tray or minimized).
public partial class MicrophoneView : UserControl
{
	private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };

	private Window? window;

	// This page and its parents (the tab, the Controller page): any of them hiding hides the page.
	private readonly List<Visual> watched = [];

	private bool running;

	public MicrophoneView()
	{
		InitializeComponent();
		timer.Tick += (_, _) => (DataContext as MicrophoneViewModel)?.Refresh();
	}

	private void Update()
	{
		bool shown = watched.Count > 0 && watched.All(v => v.IsVisible) && window is { WindowState: not WindowState.Minimized };
		if (shown == running || DataContext is not MicrophoneViewModel page)
		{
			return;
		}
		running = shown;
		if (shown)
		{
			page.Start();
			timer.Start();
		}
		else
		{
			timer.Stop();
			page.Stop();
		}
	}

	private void OnVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty)
		{
			Update();
		}
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		window = TopLevel.GetTopLevel(this) as Window;
		for (Visual? visual = this; visual != null; visual = visual.GetVisualParent())
		{
			visual.PropertyChanged += OnVisibilityChanged;
			watched.Add(visual);
		}
		Update();
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		foreach (Visual visual in watched)
		{
			visual.PropertyChanged -= OnVisibilityChanged;
		}
		watched.Clear();
		window = null;
		Update();
		base.OnDetachedFromVisualTree(e);
	}
}
