using Avalonia.Controls;
using DualPulse.App.ViewModels;

namespace DualPulse.App.Views;

// Closes on Done; closing it any other way also counts as done (FirstStartViewModel.Dismiss).
public partial class FirstStartWindow : Window
{
	public FirstStartWindow()
	{
		InitializeComponent();
	}

	public FirstStartWindow(FirstStartViewModel guide) : this()
	{
		DataContext = guide;
		guide.Finished += Close;
		Closed += (_, _) =>
		{
			guide.Finished -= Close;
			guide.Dismiss();
		};
	}
}
