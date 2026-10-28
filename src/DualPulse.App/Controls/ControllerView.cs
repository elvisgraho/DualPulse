using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DualPulse.Core.Protocol;

namespace DualPulse.App.Controls;

// A stylized, original top-down drawing of the controller that mirrors live input: pressed elements fill with the
// accent, stick caps move, trigger bars fill and touch points show; the lightbar strips show the chosen colour; on a
// cable, a USB-C plug sits in the top port. Shaded (shadow, gradients, recessed wells) and drawn in a 400 x 270 design
// space, scaled to fit.
public sealed class ControllerView : Control
{
	public static readonly StyledProperty<InputSnapshot?> SnapshotProperty = AvaloniaProperty.Register<ControllerView, InputSnapshot?>(nameof(Snapshot));

	public static readonly StyledProperty<bool> CabledProperty = AvaloniaProperty.Register<ControllerView, bool>(nameof(Cabled));

	public static readonly StyledProperty<IBrush?> LightbarProperty = AvaloniaProperty.Register<ControllerView, IBrush?>(nameof(Lightbar));

	private const double DesignWidth = 400;

	private const double DesignHeight = 270;

	private const double StickTravel = 13;

	// The outer shell: a wide top, rounded shoulders and long grips that splay outwards.
	private static readonly Geometry Body = Geometry.Parse(
		"M 200,40 C 250,40 300,38 330,44 C 362,50 378,72 384,104 L 392,196 C 396,236 376,258 350,254 C 330,251 318,236 306,216 " +
		"L 290,190 C 260,186 240,184 200,184 C 160,184 140,186 110,190 L 94,216 C 82,236 70,251 50,254 C 24,258 4,236 8,196 " +
		"L 16,104 C 22,72 38,50 70,44 C 100,38 150,40 200,40 Z");

	// The dark centre panel: it drops from the touchpad (drawn over its top) between the grips and holds the sticks,
	// PS and mute; the D-pad and face buttons sit on the shell.
	private static readonly Geometry Core = Geometry.Parse(
		"M 131,60 L 269,60 C 284,78 294,100 295,128 C 296,156 295,174 290,190 C 260,186 240,184 200,184 " +
		"C 160,184 140,186 110,190 C 105,174 104,156 105,128 C 106,100 116,78 131,60 Z");

	// The touchpad, slightly narrower at the bottom; Touchpad is its box, for placing touch points.
	private static readonly Geometry TouchpadShape = Geometry.Parse(
		"M 140,46 L 260,46 C 264,46 266,48 265.5,52 L 258,108 C 257,114 254,117 248,117 L 152,117 C 146,117 143,114 142,108 L 134.5,52 C 134,48 136,46 140,46 Z");

	private static readonly Rect Touchpad = new(140, 48, 120, 66);

	// The light strips run down the touchpad's lower sides, on the seam with the centre panel, and curve under its corners.
	private static readonly Geometry LeftLight = Geometry.Parse("M 131,60 L 137,107 C 138.5,117 143,121 154,121");

	private static readonly Geometry RightLight = Geometry.Parse("M 269,60 L 263,107 C 261.5,117 257,121 246,121");

	// The cable leaves the plug upwards with a slight bend (past the top of the design space; the control does not clip).
	private static readonly Geometry Cable = Geometry.Parse("M 200,24 C 200,0 222,-2 226,-46");

	private static readonly Rect Plug = new(191, 24, 18, 17);

	// The left bumper and trigger, following the shoulder's curve; the right ones are their mirror image. Both sit behind
	// the shell, the trigger behind the bumper, so only their tops show.
	private static readonly Geometry Bumper = Geometry.Parse(
		"M 54,60 C 58,46 78,36 110,34 L 142,33 C 146,33 147,35 147,37 L 147,40 C 147,42 146,43 142,43 L 112,43 C 88,44 72,50 64,62 Z");

	private static readonly Geometry Trigger = Geometry.Parse(
		"M 68,48 C 68,27 82,14 102,14 L 122,14 C 137,14 145,24 145,38 L 145,48 Z");

	// The trigger's pressure fill rises from behind the bumper to its top.
	private const double TriggerBottom = 38;

	private const double TriggerTop = 14;

	private static readonly Matrix Mirror = Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(DesignWidth, 0);

	private static readonly Point DpadCentre = new(72, 106);

	private static readonly Point FaceCentre = new(328, 106);

	private static readonly Point LeftStick = new(150, 154);

	private static readonly Point RightStick = new(250, 154);

	private static readonly Point PsButton = new(200, 152);

	private IBrush accent = Brushes.Goldenrod;

	private IBrush accentMuted = Brushes.DarkGoldenrod;

	private IBrush body = Brushes.Black;

	private IBrush well = Brushes.Black;

	private IBrush touchpad = Brushes.Black;

	private IBrush stickCap = Brushes.Gray;

	private IBrush part = Brushes.Gray;

	private IBrush core = Brushes.Black;

	private IPen outline = new Pen(Brushes.Gray, 1.2);

	private IPen rim = new Pen(Brushes.Gray, 1.2);

	private IPen edge = new Pen(Brushes.Gray, 1);

	private IPen glyph = new Pen(Brushes.Gray, 1.4);

	private IPen pressedGlyph = new Pen(Brushes.Black, 1.6);

	private IPen activeEdge = new Pen(Brushes.Goldenrod, 1.5);

	static ControllerView()
	{
		AffectsRender<ControllerView>(SnapshotProperty, CabledProperty, LightbarProperty);
	}

	public InputSnapshot? Snapshot
	{
		get => GetValue(SnapshotProperty);
		set => SetValue(SnapshotProperty, value);
	}

	public bool Cabled
	{
		get => GetValue(CabledProperty);
		set => SetValue(CabledProperty, value);
	}

	// The lightbar colour shown beside the touchpad; the accent when unset.
	public IBrush? Lightbar
	{
		get => GetValue(LightbarProperty);
		set => SetValue(LightbarProperty, value);
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		accent = Resource("Accent");
		accentMuted = Resource("AccentMuted");
		Color raised = ColourOf("SurfaceRaised");
		Color background = ColourOf("Bg");
		// Two tones: a grey shell around a near-black centre panel.
		body = Vertical(Mix(raised, Colors.White, 0.17), Mix(raised, Colors.White, 0.03));
		core = Vertical(Mix(background, Colors.White, 0.05), Mix(background, Colors.Black, 0.1));
		well = Vertical(Mix(background, Colors.Black, 0.2), Mix(background, Colors.White, 0.04));
		part = Vertical(Mix(raised, Colors.White, 0.06), Mix(raised, Colors.Black, 0.3));
		touchpad = Vertical(Mix(raised, Colors.White, 0.22), Mix(raised, Colors.White, 0.1));
		stickCap = new RadialGradientBrush
		{
			Center = new RelativePoint(0.4, 0.35, RelativeUnit.Relative),
			GradientOrigin = new RelativePoint(0.4, 0.35, RelativeUnit.Relative),
			RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
			RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
			GradientStops = [new GradientStop(Mix(raised, Colors.White, 0.12), 0), new GradientStop(Mix(raised, Colors.Black, 0.2), 1)]
		};
		outline = new Pen(Resource("Border"), 1.2);
		// A faint light catching the top edge of the shell.
		rim = new Pen(Vertical(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.45), 1.2);
		edge = new Pen(new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)), 1);
		glyph = new Pen(Resource("TextMuted"), 1.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
		pressedGlyph = new Pen(new SolidColorBrush(background), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
		activeEdge = new Pen(accent, 1.5);
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

		if (Cabled)
		{
			context.DrawGeometry(null, new Pen(glyph.Brush, 3, lineCap: PenLineCap.Round), Cable);
			context.DrawRectangle(well, outline, new RoundedRect(Plug, 4));
		}

		DrawTrigger(context, input.L2);
		DrawShoulder(context, buttons.HasFlag(Buttons.L1));
		using (context.PushTransform(Mirror))
		{
			DrawTrigger(context, input.R2);
			DrawShoulder(context, buttons.HasFlag(Buttons.R1));
		}

		DrawShell(context);
		context.DrawGeometry(core, edge, Core);
		DrawTouchpad(context, input, buttons.HasFlag(Buttons.Touchpad));
		DrawLightbar(context);

		DrawAngledPill(context, new Point(124, 60), -18, buttons.HasFlag(Buttons.Create));
		DrawAngledPill(context, new Point(276, 60), 18, buttons.HasFlag(Buttons.Options));
		DrawSpeaker(context);

		DrawDpad(context, buttons);

		DrawFaceButton(context, FaceCentre + new Point(0, -21), buttons.HasFlag(Buttons.Triangle), FaceSymbol.Triangle);
		DrawFaceButton(context, FaceCentre + new Point(21, 0), buttons.HasFlag(Buttons.Circle), FaceSymbol.Circle);
		DrawFaceButton(context, FaceCentre + new Point(0, 21), buttons.HasFlag(Buttons.Cross), FaceSymbol.Cross);
		DrawFaceButton(context, FaceCentre + new Point(-21, 0), buttons.HasFlag(Buttons.Square), FaceSymbol.Square);

		DrawStick(context, LeftStick, input.LeftX, input.LeftY, buttons.HasFlag(Buttons.L3));
		DrawStick(context, RightStick, input.RightX, input.RightY, buttons.HasFlag(Buttons.R3));

		bool ps = buttons.HasFlag(Buttons.Ps);
		context.DrawEllipse(ps ? accent : well, ps ? activeEdge : edge, PsButton, 7, 7);
		DrawPill(context, new Rect(192, 166, 16, 5), buttons.HasFlag(Buttons.Mute));
	}

	private enum FaceSymbol
	{
		Triangle,
		Circle,
		Cross,
		Square
	}

	// A soft shadow under the shell, the shaded shell, its outline and a faint light on the top edge.
	private void DrawShell(DrawingContext context)
	{
		foreach ((double offset, byte alpha) in new[] { (9.0, (byte)0x22), (5.0, (byte)0x2E), (2.0, (byte)0x3A) })
		{
			using (context.PushTransform(Matrix.CreateTranslation(0, offset)))
			{
				context.DrawGeometry(new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)), null, Body);
			}
		}
		context.DrawGeometry(body, outline, Body);
		context.DrawGeometry(null, rim, Body);
	}

	private void DrawTouchpad(DrawingContext context, InputSnapshot input, bool pressed)
	{
		context.DrawGeometry(touchpad, pressed ? activeEdge : outline, TouchpadShape);
		if (pressed)
		{
			// The half that clicks (left or right button), as in PC control.
			using (context.PushGeometryClip(TouchpadShape))
			{
				context.DrawRectangle(accentMuted, null, ControllerLayout.PressedTouchpadArea(input.Touch0, input.Touch1, new Rect(134, 46, 132, 72)));
			}
		}
		foreach (TouchPoint touch in new[] { input.Touch0, input.Touch1 })
		{
			if (touch.Active)
			{
				Point point = ControllerLayout.TouchPoint(touch, Touchpad.Deflate(4));
				context.DrawEllipse(accentMuted, null, point, 9, 9);
				context.DrawEllipse(accent, null, point, 4.5, 4.5);
			}
		}
	}

	// The light strips beside the touchpad, glowing in the lightbar colour.
	private void DrawLightbar(DrawingContext context)
	{
		IBrush colour = Lightbar ?? accent;
		Color glow = colour is ISolidColorBrush solid ? solid.Color : Colors.Goldenrod;
		IPen wideGlow = new Pen(new SolidColorBrush(Color.FromArgb(0x1C, glow.R, glow.G, glow.B)), 11, lineCap: PenLineCap.Round);
		IPen nearGlow = new Pen(new SolidColorBrush(Color.FromArgb(0x48, glow.R, glow.G, glow.B)), 5.5, lineCap: PenLineCap.Round);
		IPen light = new Pen(colour, 2.4, lineCap: PenLineCap.Round);
		foreach (Geometry strip in new[] { LeftLight, RightLight })
		{
			context.DrawGeometry(null, wideGlow, strip);
			context.DrawGeometry(null, nearGlow, strip);
			context.DrawGeometry(null, light, strip);
		}
	}

	private void DrawShoulder(DrawingContext context, bool pressed)
	{
		context.DrawGeometry(pressed ? accent : part, new Pen(pressed ? accent : outline.Brush, 1.2, lineJoin: PenLineJoin.Round), Bumper);
	}

	private void DrawTrigger(DrawingContext context, byte value)
	{
		context.DrawGeometry(part, new Pen(outline.Brush, 1.2, lineJoin: PenLineJoin.Round), Trigger);
		double fill = ControllerLayout.TriggerFill(value);
		if (fill > 0)
		{
			double top = TriggerBottom - (TriggerBottom - TriggerTop) * fill;
			using (context.PushGeometryClip(Trigger))
			{
				context.DrawRectangle(accent, null, new Rect(0, top, DesignWidth, 60 - top));
			}
			context.DrawGeometry(null, activeEdge, Trigger);
		}
	}

	// A small pill tilted by `degrees`, as Create and Options sit beside the touchpad.
	private void DrawAngledPill(DrawingContext context, Point centre, double degrees, bool pressed)
	{
		Matrix tilt = Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateRotation(degrees * Math.PI / 180) * Matrix.CreateTranslation(centre.X, centre.Y);
		using (context.PushTransform(tilt))
		{
			DrawPill(context, new Rect(centre.X - 3.5, centre.Y - 7, 7, 14), pressed);
		}
	}

	// A small grille of speaker holes above the PS button.
	private void DrawSpeaker(DrawingContext context)
	{
		IBrush hole = new SolidColorBrush(Color.FromArgb(0x90, 0, 0, 0));
		for (int row = 0; row < 2; row++)
		{
			for (int column = 0; column < 5 - row; column++)
			{
				double x = 200 + (column - (4 - row) / 2.0) * 5;
				context.DrawEllipse(hole, null, new Point(x, 131 + row * 4.5), 1.2, 1.2);
			}
		}
	}

	private void DrawPill(DrawingContext context, Rect rect, bool pressed)
	{
		double radius = Math.Min(rect.Width, rect.Height) / 2;
		context.DrawRectangle(pressed ? accent : well, pressed ? activeEdge : edge, new RoundedRect(rect, radius));
	}

	// One cross-shaped well; a pressed arm fills with the accent. Each arm has a small arrow.
	private void DrawDpad(DrawingContext context, Buttons buttons)
	{
		const double reach = 27;
		const double half = 8.5;
		Point c = DpadCentre;
		StreamGeometry cross = new();
		using (StreamGeometryContext g = cross.Open())
		{
			g.BeginFigure(new Point(c.X - half, c.Y - reach), true);
			foreach (Point p in new[]
			{
				new Point(c.X + half, c.Y - reach), new Point(c.X + half, c.Y - half), new Point(c.X + reach, c.Y - half),
				new Point(c.X + reach, c.Y + half), new Point(c.X + half, c.Y + half), new Point(c.X + half, c.Y + reach),
				new Point(c.X - half, c.Y + reach), new Point(c.X - half, c.Y + half), new Point(c.X - reach, c.Y + half),
				new Point(c.X - reach, c.Y - half), new Point(c.X - half, c.Y - half)
			})
			{
				g.LineTo(p);
			}
			g.EndFigure(true);
		}
		context.DrawGeometry(well, new Pen(edge.Brush, 1, lineJoin: PenLineJoin.Round), cross);

		(Buttons Button, Vector Direction)[] arms =
		[
			(Buttons.DpadUp, new Vector(0, -1)), (Buttons.DpadDown, new Vector(0, 1)),
			(Buttons.DpadLeft, new Vector(-1, 0)), (Buttons.DpadRight, new Vector(1, 0))
		];
		foreach ((Buttons button, Vector direction) in arms)
		{
			bool pressed = buttons.HasFlag(button);
			Point mid = c + direction * (half + reach) / 2;
			if (pressed)
			{
				Rect arm = direction.X == 0
					? new Rect(c.X - half + 1, mid.Y - 8.5, 2 * half - 2, 17)
					: new Rect(mid.X - 8.5, c.Y - half + 1, 17, 2 * half - 2);
				context.DrawRectangle(accent, null, new RoundedRect(arm, 2));
			}
			// A small arrow pointing outwards.
			Vector side = new(direction.Y, -direction.X);
			Point tip = mid + direction * 3.5;
			StreamGeometry arrow = new();
			using (StreamGeometryContext g = arrow.Open())
			{
				g.BeginFigure(tip, true);
				g.LineTo(mid - direction * 2 + side * 3.8);
				g.LineTo(mid - direction * 2 - side * 3.8);
				g.EndFigure(true);
			}
			context.DrawGeometry(pressed ? pressedGlyph.Brush : glyph.Brush, null, arrow);
		}
	}

	private void DrawFaceButton(DrawingContext context, Point centre, bool pressed, FaceSymbol symbol)
	{
		context.DrawEllipse(pressed ? accent : well, pressed ? activeEdge : edge, centre, 10, 10);
		IPen pen = pressed ? pressedGlyph : glyph;
		const double s = 4.3;
		switch (symbol)
		{
			case FaceSymbol.Triangle:
				StreamGeometry triangle = new();
				using (StreamGeometryContext g = triangle.Open())
				{
					g.BeginFigure(centre + new Vector(0, -s), false);
					g.LineTo(centre + new Vector(s * 1.05, s * 0.75));
					g.LineTo(centre + new Vector(-s * 1.05, s * 0.75));
					g.EndFigure(true);
				}
				context.DrawGeometry(null, pen, triangle);
				break;
			case FaceSymbol.Circle:
				context.DrawEllipse(null, pen, centre, s, s);
				break;
			case FaceSymbol.Cross:
				context.DrawLine(pen, centre + new Vector(-s * 0.85, -s * 0.85), centre + new Vector(s * 0.85, s * 0.85));
				context.DrawLine(pen, centre + new Vector(s * 0.85, -s * 0.85), centre + new Vector(-s * 0.85, s * 0.85));
				break;
			case FaceSymbol.Square:
				context.DrawRectangle(null, pen, new Rect(centre.X - s * 0.8, centre.Y - s * 0.8, s * 1.6, s * 1.6));
				break;
		}
	}

	// A recessed well with a domed cap that follows the stick; pressed (L3/R3) rings the cap with the accent.
	private void DrawStick(DrawingContext context, Point centre, byte x, byte y, bool pressed)
	{
		context.DrawEllipse(well, edge, centre, 24, 24);
		Point cap = ControllerLayout.StickDot(x, y, centre, StickTravel);
		using (context.PushTransform(Matrix.CreateTranslation(0, 2)))
		{
			context.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)), null, cap, 15, 15);
		}
		context.DrawEllipse(stickCap, pressed ? activeEdge : outline, cap, 15, 15);
		context.DrawEllipse(null, pressed ? activeEdge : new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), 1.2), cap, 10, 10);
		if (!pressed && (x is < 118 or > 138 || y is < 118 or > 138))
		{
			context.DrawEllipse(accent, null, cap, 3, 3);
		}
	}

	private IBrush Resource(string key)
	{
		return this.TryFindResource(key, out object? value) && value is IBrush brush ? brush : Brushes.Gray;
	}

	private Color ColourOf(string key)
	{
		return Resource(key) is ISolidColorBrush solid ? solid.Color : Colors.Gray;
	}

	private static Color Mix(Color from, Color to, double amount)
	{
		return Color.FromArgb(from.A,
			(byte)Math.Round(from.R + (to.R - from.R) * amount),
			(byte)Math.Round(from.G + (to.G - from.G) * amount),
			(byte)Math.Round(from.B + (to.B - from.B) * amount));
	}

	// Top to bottom; the second colour is reached at `end` (0-1) of the height.
	private static LinearGradientBrush Vertical(Color top, Color bottom, double end = 1)
	{
		return new LinearGradientBrush
		{
			StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
			EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
			GradientStops = [new GradientStop(top, 0), new GradientStop(bottom, end)]
		};
	}
}
