namespace DualPulse.Core.Protocol;

// One button sent to games as another; To = None switches the button off. A remapped button no longer sends itself,
// and its target still sends itself, so two remaps can swap buttons.
public sealed record ButtonRemap(Buttons From, Buttons To)
{
	public static Buttons Apply(Buttons pressed, IReadOnlyList<ButtonRemap> remaps)
	{
		if (remaps.Count == 0)
		{
			return pressed;
		}
		Buttons result = pressed;
		foreach (ButtonRemap remap in remaps)
		{
			result &= ~remap.From;
		}
		foreach (ButtonRemap remap in remaps)
		{
			if ((pressed & remap.From) != 0)
			{
				result |= remap.To;
			}
		}
		return result;
	}
}
