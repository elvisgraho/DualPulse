using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DualPulse.App.Controls;

// The last few seconds of sound as mirrored bars around a centre line, oldest on the left. Peaks are 0-1; drawn on a
// square-root scale so speech is easy to see.
public sealed class Waveform : Control
{
	public static readonly StyledProperty<float[]?> PeaksProperty = AvaloniaProperty.Register<Waveform, float[]?>(nameof(Peaks));

	private IBrush accent = Brushes.Goldenrod;

	private IPen baseline = new Pen(Brushes.Gray, 1);

	static Waveform()
	{
		AffectsRender<Waveform>(PeaksProperty);
	}

	public float[]? Peaks
	{
		get => GetValue(PeaksProperty);
		set => SetValue(PeaksProperty, value);
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		accent = this.TryFindResource("Accent", out object? a) && a is IBrush accentBrush ? accentBrush : Brushes.Goldenrod;
		baseline = new Pen(this.TryFindResource("Border", out object? b) && b is IBrush border ? border : Brushes.Gray, 1);
	}

	public override void Render(DrawingContext context)
	{
		double middle = Bounds.Height / 2;
		context.DrawLine(baseline, new Point(0, middle), new Point(Bounds.Width, middle));
		float[]? peaks = Peaks;
		if (peaks == null || peaks.Length == 0)
		{
			return;
		}
		double step = Bounds.Width / peaks.Length;
		double width = Math.Max(1, step - 1);
		for (int i = 0; i < peaks.Length; i++)
		{
			double half = Math.Sqrt(Math.Clamp(peaks[i], 0, 1)) * middle;
			if (half >= 0.5)
			{
				context.DrawRectangle(accent, null, new Rect(i * step, middle - half, width, half * 2));
			}
		}
	}
}
