using System.Text.Json.Serialization;

namespace DualPulse.App.Services;

// How games see one stick (with a virtual controller): Deadzone (0-0.3 of the throw) reads as centred, and the stick
// reaches the edge at FullTiltAt (0.7-1). The defaults change nothing.
public sealed record StickRange(double Deadzone = 0, double FullTiltAt = 1)
{
	public const double MaxDeadzone = 0.3;

	public const double MinFullTiltAt = 0.7;

	[JsonIgnore]
	public bool ChangesNothing => Deadzone <= 0 && FullTiltAt >= 1;
}

// Both sticks. When Linked, the left settings apply to both and Right is ignored.
public sealed record StickSettings(bool Linked = true, StickRange? Left = null, StickRange? Right = null)
{
	public StickRange Left { get; init; } = Left ?? new StickRange();

	public StickRange Right { get; init; } = Right ?? new StickRange();

	[JsonIgnore]
	public StickRange EffectiveRight => Linked ? Left : Right;
}
