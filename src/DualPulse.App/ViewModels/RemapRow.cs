using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.Core.Protocol;

namespace DualPulse.App.ViewModels;

public sealed record ButtonChoice(string Label, Buttons Button);

// One "button sends another" row on the Emulation page. Changes go back through the page, which saves all rows.
public sealed partial class RemapRow : ObservableObject
{
	// The digital buttons a game sees; L2 and R2 are analog, so they aren't remapped.
	public static readonly IReadOnlyList<ButtonChoice> Buttons =
	[
		new("Cross", Core.Protocol.Buttons.Cross),
		new("Circle", Core.Protocol.Buttons.Circle),
		new("Square", Core.Protocol.Buttons.Square),
		new("Triangle", Core.Protocol.Buttons.Triangle),
		new("L1", Core.Protocol.Buttons.L1),
		new("R1", Core.Protocol.Buttons.R1),
		new("L3", Core.Protocol.Buttons.L3),
		new("R3", Core.Protocol.Buttons.R3),
		new("D-pad up", Core.Protocol.Buttons.DpadUp),
		new("D-pad down", Core.Protocol.Buttons.DpadDown),
		new("D-pad left", Core.Protocol.Buttons.DpadLeft),
		new("D-pad right", Core.Protocol.Buttons.DpadRight),
		new("Options", Core.Protocol.Buttons.Options),
		new("Create", Core.Protocol.Buttons.Create),
		new("Touchpad click", Core.Protocol.Buttons.Touchpad),
		new("PS", Core.Protocol.Buttons.Ps)
	];

	private static readonly IReadOnlyList<ButtonChoice> Targets = [.. Buttons, new("Nothing", Core.Protocol.Buttons.None)];

	private readonly Action changed;

	private readonly Action<RemapRow> remove;

	internal RemapRow(ButtonRemap remap, Action changed, Action<RemapRow> remove)
	{
		this.changed = changed;
		this.remove = remove;
		From = Buttons.FirstOrDefault(c => c.Button == remap.From) ?? Buttons[0];
		To = Targets.FirstOrDefault(c => c.Button == remap.To) ?? Targets[^1];
	}

	public IReadOnlyList<ButtonChoice> FromChoices => Buttons;

	public IReadOnlyList<ButtonChoice> ToChoices => Targets;

	public ButtonChoice From
	{
		get;
		set
		{
			// The list control clears its selection while items change; that isn't the user choosing.
			if (value != null && SetProperty(ref field, value))
			{
				changed();
			}
		}
	} = Buttons[0];

	public ButtonChoice To
	{
		get;
		set
		{
			if (value != null && SetProperty(ref field, value))
			{
				changed();
			}
		}
	} = Targets[^1];

	public ButtonRemap ToRemap() => new(From.Button, To.Button);

	[RelayCommand]
	private void Remove() => remove(this);
}
