using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DualPulse.App.Controls;

// The last few seconds of sound as mirrored bars around a centre line, oldest on the left. Peaks are 0-1; drawn on a
// square-root scale so speech is easy to see. The last peak is the bar still filling; Progress (0-1) is how full it is,
// and everything shifts left by that much, so the waveform scrolls smoothly instead of a bar at a time.
public sealed class Waveform : Control
{
	public static readonly StyledProperty<float[]?> PeaksProperty = AvaloniaProperty.Register<Waveform, float[]?>(nameof(Peaks));

	public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<Waveform, double>(nameof(Progress));

	// A level (0-1, linear like the peaks) marked with dashed lines, e.g. where a gate opens; 0 draws none.
	public static readonly StyledProperty<double> ThresholdProperty = AvaloniaProperty.Register<Waveform, double>(nameof(Threshold));

	private IBrush accent = Brushes.Goldenrod;

	private IPen baseline = new Pen(Brushes.Gray, 1);

	private IBrush threshold = Brushes.Gray;

	static Waveform()
	{
		AffectsRender<Waveform>(PeaksProperty, ProgressProperty, ThresholdProperty);
	}

	public float[]? Peaks
	{
		get => GetValue(PeaksProperty);
		set => SetValue(PeaksProperty, value);
	}

	public double Progress
	{
		get => GetValue(ProgressProperty);
		set => SetValue(ProgressProperty, value);
	}

	public double Threshold
	{
		get => GetValue(ThresholdProperty);
		set => SetValue(ThresholdProperty, value);
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		accent = this.TryFindResource("Accent", out object? a) && a is IBrush accentBrush ? accentBrush : Brushes.Goldenrod;
		baseline = new Pen(this.TryFindResource("Border", out object? b) && b is IBrush border ? border : Brushes.Gray, 1);
		threshold = this.TryFindResource("TextMuted", out object? t) && t is IBrush muted ? muted : Brushes.Gray;
	}

	public override void Render(DrawingContext context)
	{
		double middle = Bounds.Height / 2;
		context.DrawLine(baseline, new Point(0, middle), new Point(Bounds.Width, middle));
		if (Threshold > 0)
		{
			double offset = Math.Sqrt(Math.Clamp(Threshold, 0, 1)) * middle;
			IPen dashed = new Pen(threshold, 1, new DashStyle([4, 4], 0));
			context.DrawLine(dashed, new Point(0, middle - offset), new Point(Bounds.Width, middle - offset));
			context.DrawLine(dashed, new Point(0, middle + offset), new Point(Bounds.Width, middle + offset));
		}
		float[]? peaks = Peaks;
		if (peaks == null || peaks.Length < 2)
		{
			return;
		}
		using DrawingContext.PushedState clip = context.PushClip(new Rect(Bounds.Size));
		double step = Bounds.Width / (peaks.Length - 1);
		double width = Math.Max(1, step - 1);
		double shift = Math.Clamp(Progress, 0, 1) * step;
		for (int i = 0; i < peaks.Length; i++)
		{
			double half = Math.Sqrt(Math.Clamp(peaks[i], 0, 1)) * middle;
			if (half >= 0.5)
			{
				context.DrawRectangle(accent, null, new Rect(i * step - shift, middle - half, width, half * 2));
			}
		}
	}
}
