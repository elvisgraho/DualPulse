using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Controls;

// A stylized, original top-down drawing of the controller that mirrors live input: pressed elements fill with the
// accent, stick dots move, trigger bars fill and touch points show. Drawn in a 400 x 270 design space, scaled to fit.
public sealed class ControllerView : Control
{
	public static readonly StyledProperty<InputSnapshot?> SnapshotProperty = AvaloniaProperty.Register<ControllerView, InputSnapshot?>(nameof(Snapshot));

	private const double DesignWidth = 400;

	private const double DesignHeight = 270;

	private const double StickTravel = 13;

	private static readonly Geometry Body = Geometry.Parse(
		"M 110,46 H 290 C 330,46 352,62 362,100 L 384,200 C 392,238 368,258 344,250 C 326,244 316,226 304,206 L 290,182 H 110 " +
		"L 96,206 C 84,226 74,244 56,250 C 32,258 8,238 16,200 L 38,100 C 48,62 70,46 110,46 Z");

	private static readonly Rect Touchpad = new(140, 52, 120, 62);

	private static readonly Rect L1 = new(72, 30, 66, 10);

	private static readonly Rect R1 = new(262, 30, 66, 10);

	private static readonly Rect L2 = new(80, 8, 50, 12);

	private static readonly Rect R2 = new(270, 8, 50, 12);

	private static readonly Point DpadCentre = new(100, 105);

	private static readonly Point FaceCentre = new(300, 105);

	private static readonly Point LeftStick = new(152, 150);

	private static readonly Point RightStick = new(248, 150);

	private static readonly Point PsButton = new(200, 142);

	private IBrush accent = Brushes.Goldenrod;

	private IBrush accentMuted = Brushes.DarkGoldenrod;

	private IBrush surface = Brushes.Black;

	private IPen outline = new Pen(Brushes.Gray, 1.5);

	private IPen idle = new Pen(Brushes.Gray, 1.5);

	private IPen active = new Pen(Brushes.Goldenrod, 1.5);

	static ControllerView()
	{
		AffectsRender<ControllerView>(SnapshotProperty);
	}

	public InputSnapshot? Snapshot
	{
		get => GetValue(SnapshotProperty);
		set => SetValue(SnapshotProperty, value);
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		accent = Resource("Accent");
		accentMuted = Resource("AccentMuted");
		surface = Resource("SurfaceRaised");
		outline = new Pen(Resource("Border"), 1.5);
		idle = new Pen(Resource("TextMuted"), 1.5);
		active = new Pen(accent, 1.5);
	}

	public override void Render(DrawingContext context)
	{
		double scale = Math.Min(Bounds.Width / DesignWidth, Bounds.Height / DesignHeight);
		if (scale <= 0)
		{
			return;
		}
		Matrix transform = Matrix.CreateScale(scale, scale)
			* Matrix.CreateTranslation((Bounds.Width - DesignWidth * scale) / 2, (Bounds.Height - DesignHeight * scale) / 2);
		using DrawingContext.PushedState pushed = context.PushTransform(transform);

		InputSnapshot input = Snapshot ?? new InputSnapshot { LeftX = 128, LeftY = 128, RightX = 128, RightY = 128 };
		Buttons buttons = input.Buttons;

		context.DrawGeometry(surface, outline, Body);

		DrawTrigger(context, L2, input.L2);
		DrawTrigger(context, R2, input.R2);
		DrawRounded(context, L1, 4, buttons.HasFlag(Buttons.L1));
		DrawRounded(context, R1, 4, buttons.HasFlag(Buttons.R1));

		bool padPressed = buttons.HasFlag(Buttons.Touchpad);
		if (padPressed)
		{
			// The half that clicks (left or right button), as in PC control.
			context.DrawRectangle(accentMuted, null, new RoundedRect(ControllerLayout.PressedTouchpadArea(input.Touch0, input.Touch1, Touchpad), 10));
		}
		DrawRounded(context, Touchpad, 10, padPressed, fillPressed: false);
		foreach (TouchPoint touch in new[] { input.Touch0, input.Touch1 })
		{
			if (touch.Active)
			{
				context.DrawEllipse(accent, null, ControllerLayout.TouchPoint(touch, Touchpad.Deflate(4)), 5, 5);
			}
		}
		DrawRounded(context, new Rect(122, 56, 7, 14), 3.5, buttons.HasFlag(Buttons.Create));
		DrawRounded(context, new Rect(271, 56, 7, 14), 3.5, buttons.HasFlag(Buttons.Options));

		DrawRounded(context, Square(DpadCentre + new Point(0, -17), 14), 3, buttons.HasFlag(Buttons.DpadUp));
		DrawRounded(context, Square(DpadCentre + new Point(0, 17), 14), 3, buttons.HasFlag(Buttons.DpadDown));
		DrawRounded(context, Square(DpadCentre + new Point(-17, 0), 14), 3, buttons.HasFlag(Buttons.DpadLeft));
		DrawRounded(context, Square(DpadCentre + new Point(17, 0), 14), 3, buttons.HasFlag(Buttons.DpadRight));

		DrawCircle(context, FaceCentre + new Point(0, -18), 8, buttons.HasFlag(Buttons.Triangle));
		DrawCircle(context, FaceCentre + new Point(18, 0), 8, buttons.HasFlag(Buttons.Circle));
		DrawCircle(context, FaceCentre + new Point(0, 18), 8, buttons.HasFlag(Buttons.Cross));
		DrawCircle(context, FaceCentre + new Point(-18, 0), 8, buttons.HasFlag(Buttons.Square));

		DrawStick(context, LeftStick, input.LeftX, input.LeftY, buttons.HasFlag(Buttons.L3));
		DrawStick(context, RightStick, input.RightX, input.RightY, buttons.HasFlag(Buttons.R3));

		DrawCircle(context, PsButton, 6, buttons.HasFlag(Buttons.Ps));
		DrawRounded(context, new Rect(192, 158, 16, 5), 2.5, buttons.HasFlag(Buttons.Mute));
	}

	private static Rect Square(Point centre, double size)
	{
		return new Rect(centre.X - size / 2, centre.Y - size / 2, size, size);
	}

	private IBrush Resource(string key)
	{
		return this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : Brushes.Gray;
	}

	private void DrawRounded(DrawingContext context, Rect rect, double radius, bool pressed, bool fillPressed = true)
	{
		context.DrawRectangle(pressed && fillPressed ? accent : null, pressed ? active : idle, new RoundedRect(rect, radius));
	}

	private void DrawCircle(DrawingContext context, Point centre, double radius, bool pressed)
	{
		context.DrawEllipse(pressed ? accent : null, pressed ? active : idle, centre, radius, radius);
	}

	private void DrawStick(DrawingContext context, Point centre, byte x, byte y, bool pressed)
	{
		context.DrawEllipse(null, pressed ? active : idle, centre, 22, 22);
		Point dot = ControllerLayout.StickDot(x, y, centre, StickTravel);
		context.DrawEllipse(pressed ? accent : null, pressed ? active : idle, dot, 7, 7);
		if (!pressed && (x is < 118 or > 138 || y is < 118 or > 138))
		{
			context.DrawEllipse(accent, null, dot, 3, 3);
		}
	}

	private void DrawTrigger(DrawingContext context, Rect rect, byte value)
	{
		double fill = ControllerLayout.TriggerFill(value);
		if (fill > 0)
		{
			context.DrawRectangle(accent, null, new RoundedRect(rect.WithWidth(rect.Width * fill), 3));
		}
		context.DrawRectangle(null, fill > 0 ? active : idle, new RoundedRect(rect, 3));
	}
}
