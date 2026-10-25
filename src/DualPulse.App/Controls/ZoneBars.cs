using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace DualPulse.App.Controls;

// One row of 10 bars, one per zone of trigger travel (top to bottom), each 0-8. Clicking or dragging across the row
// draws the values: the pointer's height sets the bar under it.
public sealed class ZoneBars : Control
{
	public const int Count = 10;

	public const int Maximum = 8;

	public static readonly StyledProperty<IReadOnlyList<int>?> ValuesProperty =
		AvaloniaProperty.Register<ZoneBars, IReadOnlyList<int>?>(nameof(Values), defaultBindingMode: BindingMode.TwoWay);

	private const double Gap = 6;

	private IBrush accent = Brushes.Goldenrod;

	private IBrush track = Brushes.Black;

	private bool drawing;

	static ZoneBars()
	{
		AffectsRender<ZoneBars>(ValuesProperty);
		CursorProperty.OverrideDefaultValue<ZoneBars>(new Cursor(StandardCursorType.Hand));
	}

	public IReadOnlyList<int>? Values
	{
		get => GetValue(ValuesProperty);
		set => SetValue(ValuesProperty, value);
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		accent = Resource("Accent");
		track = Resource("SurfaceRaised");
	}

	public override void Render(DrawingContext context)
	{
		double width = (Bounds.Width - Gap * (Count - 1)) / Count;
		for (int zone = 0; zone < Count; zone++)
		{
			Rect column = new(zone * (width + Gap), 0, width, Bounds.Height);
			context.DrawRectangle(track, null, new RoundedRect(column, 4));
			int value = Values is { } values && zone < values.Count ? Math.Clamp(values[zone], 0, Maximum) : 0;
			if (value > 0)
			{
				double height = Bounds.Height * value / Maximum;
				context.DrawRectangle(accent, null, new RoundedRect(new Rect(column.X, Bounds.Height - height, width, height), 4));
			}
		}
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		drawing = true;
		e.Pointer.Capture(this);
		Draw(e.GetPosition(this));
		e.Handled = true;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		if (drawing)
		{
			Draw(e.GetPosition(this));
		}
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		drawing = false;
		e.Pointer.Capture(null);
	}

	protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
	{
		base.OnPointerCaptureLost(e);
		drawing = false;
	}

	private void Draw(Point point)
	{
		if (Bounds.Width <= 0 || Bounds.Height <= 0)
		{
			return;
		}
		int zone = Math.Clamp((int)(point.X / Bounds.Width * Count), 0, Count - 1);
		int value = Math.Clamp((int)Math.Round((1 - point.Y / Bounds.Height) * Maximum), 0, Maximum);
		int[] values = [.. Enumerable.Range(0, Count).Select(i => Values is { } current && i < current.Count ? current[i] : 0)];
		if (values[zone] != value)
		{
			values[zone] = value;
			Values = values;
		}
	}

	private IBrush Resource(string key)
	{
		return this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : Brushes.Gray;
	}
}
