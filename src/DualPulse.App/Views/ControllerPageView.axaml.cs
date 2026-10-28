using Avalonia.Controls;

namespace DualPulse.App.Views;

public partial class ControllerPageView : UserControl
{
	public ControllerPageView()
	{
		InitializeComponent();
	}

	private void OnRenameBackdropPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
	{
		(DataContext as ViewModels.ControllerPageViewModel)?.CancelRenameCommand.Execute(null);
	}
}
