using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Core.Mapping;
using DualPulse.Core.Protocol;

namespace DualPulse.App.ViewModels;

public sealed record PcActionChoice(string Label, PcAction Action);

// One controller button and what it sends in PC control.
public sealed partial class ButtonRow : ObservableObject
{
	private readonly MappingViewModel page;

	private PcActionChoice selectedAction;

	internal ButtonRow(MappingViewModel page, string label, Buttons button, PcActionChoice selected)
	{
		this.page = page;
		Label = label;
		Button = button;
		selectedAction = selected;
	}

	public string Label { get; }

	public Buttons Button { get; }

	// False while a hold layer uses this button.
	public bool IsEditable => page.LayerNote(Button).Length == 0;

	public string Note => page.LayerNote(Button);

	public PcActionChoice SelectedAction
	{
		get => selectedAction;
		set
		{
			if (SetProperty(ref selectedAction, value))
			{
				page.SaveButtons();
			}
		}
	}

	internal void Show(PcActionChoice choice)
	{
		selectedAction = choice;
		OnPropertyChanged(nameof(SelectedAction));
	}

	internal void RefreshLayer()
	{
		OnPropertyChanged(nameof(IsEditable));
		OnPropertyChanged(nameof(Note));
	}
}

// The PC control page: the controller as mouse and keyboard, with its pointer options and button table.
public sealed partial class MappingViewModel : ObservableObject
{
	private static readonly (Buttons Button, string Label)[] MappableButtons =
	[
		(Buttons.Cross, "Cross"), (Buttons.Circle, "Circle"), (Buttons.Square, "Square"), (Buttons.Triangle, "Triangle"),
		(Buttons.DpadUp, "D-pad up"), (Buttons.DpadDown, "D-pad down"), (Buttons.DpadLeft, "D-pad left"), (Buttons.DpadRight, "D-pad right"),
		(Buttons.L1, "L1"), (Buttons.R1, "R1"), (Buttons.L2, "L2"), (Buttons.R2, "R2"), (Buttons.L3, "L3"), (Buttons.R3, "R3"),
		(Buttons.Create, "Create"), (Buttons.Options, "Options"), (Buttons.Mute, "Mute button")
	];

	private readonly ISettingsService settings;

	private readonly IPcControlService pcControl;

	private readonly UiContext ui = new();

	public MappingViewModel(ISettingsService settings, IPcControlService pcControl)
	{
		this.settings = settings;
		this.pcControl = pcControl;
		IReadOnlyDictionary<Buttons, PcAction> actions = Mapping.ButtonActions();
		ButtonRows = MappableButtons.Select(b => new ButtonRow(this, b.Label, b.Button, Choice(actions.GetValueOrDefault(b.Button)))).ToList();
		pcControl.ActiveChanged += () => ui.Run(() =>
		{
			OnPropertyChanged(nameof(PcControlOn));
			OnPropertyChanged(nameof(StatusText));
		});
		settings.Changed += _ => ui.Run(Refresh);
	}

	public IReadOnlyList<PcActionChoice> ActionChoices { get; } =
	[
		new("Nothing", PcAction.None),
		new("Left click", PcAction.LeftClick),
		new("Right click", PcAction.RightClick),
		new("Middle click", PcAction.MiddleClick),
		new("Enter", PcAction.Enter),
		new("Escape", PcAction.Escape),
		new("Space", PcAction.Space),
		new("Tab", PcAction.Tab),
		new("Backspace", PcAction.Backspace),
		new("Delete", PcAction.Delete),
		new("Arrow up", PcAction.Up),
		new("Arrow down", PcAction.Down),
		new("Arrow left", PcAction.Left),
		new("Arrow right", PcAction.Right),
		new("Page up", PcAction.PageUp),
		new("Page down", PcAction.PageDown),
		new("Home", PcAction.Home),
		new("End", PcAction.End),
		new("Windows key", PcAction.Windows),
		new("Alt", PcAction.Alt),
		new("Shift", PcAction.Shift),
		new("Ctrl", PcAction.Control),
		new("F11 (full screen)", PcAction.F11),
		new("Play / pause", PcAction.PlayPause),
		new("Next track", PcAction.NextTrack),
		new("Previous track", PcAction.PreviousTrack),
		new("Volume up", PcAction.VolumeUp),
		new("Volume down", PcAction.VolumeDown),
		new("Mute sound", PcAction.VolumeMute)
	];

	public IReadOnlyList<ButtonRow> ButtonRows { get; }

	public bool PcControlOn => pcControl.IsActive;

	public string StatusText => pcControl.IsActive ? "On: the controller drives the mouse and keyboard" : "Off";

	private MappingSettings Mapping => settings.Current.EffectiveMapping;

	public bool TouchpadEnabled
	{
		get => Mapping.TouchpadEnabled;
		set => Update(Mapping with { TouchpadEnabled = value });
	}

	public double TouchpadSpeed
	{
		get => Mapping.TouchpadSpeed;
		set => Update(Mapping with { TouchpadSpeed = Math.Round(value, 2) });
	}

	public bool StickMouseEnabled
	{
		get => Mapping.StickMouseEnabled;
		set => Update(Mapping with { StickMouseEnabled = value });
	}

	public double StickSpeed
	{
		get => Mapping.StickSpeed;
		set => Update(Mapping with { StickSpeed = Math.Round(value, 2) });
	}

	public bool StickScrollEnabled
	{
		get => Mapping.StickScrollEnabled;
		set => Update(Mapping with { StickScrollEnabled = value });
	}

	// Hold L1: window switcher (Alt+Tab).
	public bool WindowSwitcherEnabled
	{
		get => Mapping.WindowSwitcherEnabled;
		set => Update(Mapping with { WindowSwitcherEnabled = value });
	}

	// Hold R1: volume and media keys.
	public bool MediaLayerEnabled
	{
		get => Mapping.MediaLayerEnabled;
		set => Update(Mapping with { MediaLayerEnabled = value });
	}

	internal string LayerNote(Buttons button)
	{
		return button switch
		{
			Buttons.L1 when Mapping.WindowSwitcherEnabled => "Hold: window switcher",
			Buttons.R1 when Mapping.MediaLayerEnabled => "Hold: media controls",
			_ => ""
		};
	}

	internal void SaveButtons()
	{
		Dictionary<string, PcAction> table = ButtonRows
			.Where(r => r.SelectedAction.Action != PcAction.None)
			.ToDictionary(r => r.Button.ToString(), r => r.SelectedAction.Action);
		Update(Mapping with { Buttons = table });
	}

	[RelayCommand]
	private void TogglePcControl()
	{
		pcControl.IsActive = !pcControl.IsActive;
		OnPropertyChanged(nameof(PcControlOn));
		OnPropertyChanged(nameof(StatusText));
	}

	[RelayCommand]
	private void ResetButtons()
	{
		Update(Mapping with { Buttons = null });
		Refresh();
	}

	private PcActionChoice Choice(PcAction action) => ActionChoices.FirstOrDefault(c => c.Action == action) ?? ActionChoices[0];

	private void Refresh()
	{
		OnPropertyChanged(nameof(TouchpadEnabled));
		OnPropertyChanged(nameof(TouchpadSpeed));
		OnPropertyChanged(nameof(StickMouseEnabled));
		OnPropertyChanged(nameof(StickSpeed));
		OnPropertyChanged(nameof(StickScrollEnabled));
		OnPropertyChanged(nameof(WindowSwitcherEnabled));
		OnPropertyChanged(nameof(MediaLayerEnabled));
		IReadOnlyDictionary<Buttons, PcAction> actions = Mapping.ButtonActions();
		foreach (ButtonRow row in ButtonRows)
		{
			row.RefreshLayer();
			PcActionChoice choice = Choice(actions.GetValueOrDefault(row.Button));
			if (row.SelectedAction != choice)
			{
				row.Show(choice);
			}
		}
	}

	private void Update(MappingSettings updated)
	{
		if (updated != Mapping)
		{
			settings.Update(s => s with { Mapping = updated });
		}
	}
}
