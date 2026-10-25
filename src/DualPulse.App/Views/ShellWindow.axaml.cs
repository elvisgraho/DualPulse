using Avalonia.Controls;
using Avalonia.Platform;

namespace DualPulse.App.Views;

public partial class ShellWindow : Window
{
	public ShellWindow()
	{
		InitializeComponent();
	}

	// The default height fits Home without scrolling; on a shorter screen the window fits the usable area instead.
	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);
		if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is Screen screen)
		{
			double usable = screen.WorkingArea.Height / screen.Scaling;
			// Leaves room for the title bar and window frame.
			double fits = Math.Max(MinHeight, usable - 48);
			if (Height > fits)
			{
				Height = fits;
				Position = Position.WithY(screen.WorkingArea.Y);
			}
		}
	}
}
