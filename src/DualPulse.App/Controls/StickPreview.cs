using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DualPulse.App.Controls;

// The stick's throw as a circle: the deadzone as a tinted disc, the full-tilt point as a dashed ring, the stick's raw
// position as a faint dot and what games get as an accent dot. Positions are -1..1 per axis.
public sealed class StickPreview : Control
{
	public static readonly StyledProperty<Point> RawProperty = AvaloniaProperty.Register<StickPreview, Point>(nameof(Raw));

	public static readonly StyledProperty<Point> GameProperty = AvaloniaProperty.Register<StickPreview, Point>(nameof(Game));

	public static readonly StyledProperty<double> DeadzoneProperty = AvaloniaProperty.Register<StickPreview, double>(nameof(Deadzone));

	public static readonly StyledProperty<double> FullTiltAtProperty = AvaloniaProperty.Register<StickPreview, double>(nameof(FullTiltAt), 100);

	private IBrush accent = Brushes.Goldenrod;

	private IBrush surface = Brushes.Black;

	private IBrush muted = Brushes.Gray;

	private IBrush deadzoneFill = Brushes.DarkGoldenrod;

	private IPen ring = new Pen(Brushes.Gray, 1);

	static StickPreview()
	{
		AffectsRender<StickPreview>(RawProperty, GameProperty, DeadzoneProperty, FullTiltAtProperty);
	}

	public Point Raw
	{
		get => GetValue(RawProperty);
		set => SetValue(RawProperty, value);
	}

	public Point Game
	{
		get => GetValue(GameProperty);
		set => SetValue(GameProperty, value);
	}

	// Percent, like the sliders.
	public double Deadzone
	{
		get => GetValue(DeadzoneProperty);
		set => SetValue(DeadzoneProperty, value);
	}

	public double FullTiltAt
	{
		get => GetValue(FullTiltAtProperty);
		set => SetValue(FullTiltAtProperty, value);
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		accent = Resource("Accent");
		surface = Resource("SurfaceRaised");
		muted = Resource("TextMuted");
		deadzoneFill = Resource("AccentMuted");
		ring = new Pen(Resource("TextMuted"), 1.5, new DashStyle([3, 3], 0));
	}

	public override void Render(DrawingContext context)
	{
		double radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 6;
		Point centre = new(Bounds.Width / 2, Bounds.Height / 2);
		context.DrawEllipse(surface, null, centre, radius, radius);
		if (FullTiltAt < 100)
		{
			context.DrawEllipse(null, ring, centre, radius * FullTiltAt / 100, radius * FullTiltAt / 100);
		}
		if (Deadzone > 0)
		{
			context.DrawEllipse(deadzoneFill, null, centre, radius * Deadzone / 100, radius * Deadzone / 100);
		}
		context.DrawEllipse(muted, null, At(centre, radius, Raw), 5, 5);
		context.DrawEllipse(accent, null, At(centre, radius, Game), 6, 6);
	}

	private static Point At(Point centre, double radius, Point position)
	{
		return new Point(centre.X + Math.Clamp(position.X, -1, 1) * radius, centre.Y + Math.Clamp(position.Y, -1, 1) * radius);
	}

	private IBrush Resource(string key)
	{
		return this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : Brushes.Gray;
	}
}
