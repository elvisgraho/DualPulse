using DualPulse.App.Services;
using DualPulse.Core.Protocol;

namespace DualPulse.App.ViewModels;

// A lightbar colour swatch (Home and Lighting), marked when it is the colour set now.
public sealed record SwatchChoice(Rgb Colour, bool Selected)
{
	private static readonly Rgb[] Presets =
	[
		AppSettings.AccentLightbar,
		new Rgb(0xFF, 0xFF, 0xFF),
		new Rgb(0x3D, 0x7B, 0xFF),
		new Rgb(0xFF, 0x3B, 0x3B),
		new Rgb(0x3D, 0xDC, 0x84),
		new Rgb(0x00, 0x00, 0x00)
	];

	// The presets, with the current colour marked; a colour set elsewhere (hue, hex) is added as the last swatch.
	public static IReadOnlyList<SwatchChoice> For(Rgb current)
	{
		List<SwatchChoice> swatches = [.. Presets.Select(colour => new SwatchChoice(colour, colour == current))];
		if (!Presets.Contains(current))
		{
			swatches.Add(new SwatchChoice(current, true));
		}
		return swatches;
	}
}
