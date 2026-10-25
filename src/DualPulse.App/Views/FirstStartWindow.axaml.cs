using Avalonia.Controls;
using Avalonia.Threading;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Closes shortly after a ready mode is picked (so the selection shows); closing it any other way also counts as done
// (FirstStartViewModel.Dismiss).
public partial class FirstStartWindow : Window
{
	public FirstStartWindow()
	{
		InitializeComponent();
	}

	public FirstStartWindow(FirstStartViewModel guide) : this()
	{
		DataContext = guide;
		guide.Finished += CloseSoon;
		Closed += (_, _) =>
		{
			guide.Finished -= CloseSoon;
			guide.Dismiss();
		};
	}

	private void CloseSoon() => DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(350));
}
