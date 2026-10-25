using Avalonia.Data.Converters;
using Avalonia.Media;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Views;

public static class Converters
{
	public static readonly FuncValueConverter<Rgb, IBrush> RgbToBrush = new(colour => new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B)));

	// The controller drawing dims while no controller is connected.
	public static readonly FuncValueConverter<bool, double> ConnectedOpacity = new(connected => connected ? 1.0 : 0.45);
}
