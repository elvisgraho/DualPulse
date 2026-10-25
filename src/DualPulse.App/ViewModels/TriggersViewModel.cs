using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Core.Mapping;

namespace DualPulse.App.ViewModels;

public sealed record TriggerKindChoice(string Label, TriggerKind Kind);

// The Triggers page. Edits go straight to the settings (ControllerService applies them) and mark the preset as
// Custom unless the result equals a preset. With "same for both" on, the left editor drives both triggers. With a game
// profile selected, edits go to that profile instead (only once it has its own triggers).
public sealed partial class TriggersViewModel : ObservableObject
{
	private const int TravelStep = 3;

	private readonly ISettingsService settings;

	private readonly IControllerService controller;

	private readonly ProfileSelection? selection;

	private readonly UiContext ui = new();

	private byte leftRaw;

	private byte rightRaw;

	public TriggersViewModel(ISettingsService settings, IControllerService controller, ProfileSelection? selection = null)
	{
		this.settings = settings;
		this.controller = controller;
		this.selection = selection;
		Left = new TriggerEditorViewModel(this, isLeft: true);
		Right = new TriggerEditorViewModel(this, isLeft: false);
		Strip = new ProfileStripViewModel(selection, ProfilePart.Triggers, "triggers");
		settings.Changed += _ => ui.Run(Refresh);
		if (selection != null)
		{
			selection.Changed += () => ui.Run(Refresh);
		}
	}

	public ProfileStripViewModel Strip { get; }

	public TriggerEditorViewModel Left { get; }

	public TriggerEditorViewModel Right { get; }

	public IReadOnlyList<TriggerPreset> Presets { get; } = [TriggerPreset.Off, TriggerPreset.Feedback, TriggerPreset.Weapon, TriggerPreset.Vibration];

	internal TriggerSettings Triggers => selection?.Selected?.Triggers ?? settings.Current.EffectiveTriggers;

	// How far each trigger is pressed, 0-1, for the travel bars.
	[ObservableProperty]
	public partial double LeftTravel { get; private set; }

	[ObservableProperty]
	public partial double RightTravel { get; private set; }

	public bool Linked
	{
		get => Triggers.Linked;
		set
		{
			if (value != Triggers.Linked)
			{
				// Unlinking starts the right trigger from the shared effect.
				Write(Triggers with { Linked = value, Right = value ? Triggers.Right : Triggers.Left });
			}
		}
	}

	// Samples the trigger positions; only changes of a few steps update the bars, so a resting trigger causes no redraw.
	public void RefreshTravel()
	{
		byte l2 = controller.Session?.Latest?.L2 ?? 0;
		byte r2 = controller.Session?.Latest?.R2 ?? 0;
		if (Math.Abs(l2 - leftRaw) >= TravelStep || (l2 == 0) != (leftRaw == 0))
		{
			leftRaw = l2;
			LeftTravel = l2 / 255.0;
		}
		if (Math.Abs(r2 - rightRaw) >= TravelStep || (r2 == 0) != (rightRaw == 0))
		{
			rightRaw = r2;
			RightTravel = r2 / 255.0;
		}
	}

	[RelayCommand]
	private void ApplyPreset(TriggerPreset preset)
	{
		// A preset sets the effect; the range (deadzone, start point) is the user's and stays.
		TriggerSettings current = Triggers;
		TriggerSettings chosen = TriggerSettings.FromPreset(preset);
		chosen = chosen with
		{
			Left = chosen.Left with { Deadzone = current.Left.Deadzone, StartAt = current.Left.StartAt },
			Right = chosen.Right with { Deadzone = current.Right.Deadzone, StartAt = current.Right.StartAt }
		};
		if (selection?.Selected != null)
		{
			Write(chosen);
			return;
		}
		settings.Update(s => s with { TriggerPreset = preset, Triggers = chosen });
	}

	internal void WriteEffect(bool isLeft, TriggerEffectSettings effect)
	{
		Write(isLeft ? Triggers with { Left = effect } : Triggers with { Right = effect });
	}

	private void Write(TriggerSettings triggers)
	{
		if (selection?.Selected != null)
		{
			if (Strip.CanEdit)
			{
				selection.Update(p => p with { Triggers = triggers });
			}
			return;
		}
		if (triggers != settings.Current.Triggers)
		{
			settings.Update(s => s with { Triggers = triggers, TriggerPreset = triggers.MatchPreset() });
		}
	}

	private void Refresh()
	{
		OnPropertyChanged(nameof(Linked));
		Strip.Refresh();
		Left.Refresh();
		Right.Refresh();
	}
}

// One trigger's effect editor. Values are kept inside the ranges the chosen effect accepts, so what the sliders show
// is what the controller gets.
public sealed class TriggerEditorViewModel : ObservableObject
{
	private readonly TriggersViewModel page;

	private readonly bool isLeft;

	internal TriggerEditorViewModel(TriggersViewModel page, bool isLeft)
	{
		this.page = page;
		this.isLeft = isLeft;
	}

	public IReadOnlyList<TriggerKindChoice> Kinds { get; } =
	[
		new("Off", TriggerKind.Off),
		new("Resistance", TriggerKind.Resistance),
		new("Weapon", TriggerKind.Weapon),
		new("Vibration", TriggerKind.Vibration)
	];

	public string Title => isLeft ? (page.Linked ? "L2 and R2" : "L2") : "R2";

	private TriggerEffectSettings Effect => isLeft ? page.Triggers.Left : page.Triggers.Right;

	public TriggerKindChoice SelectedKind
	{
		get => Kinds.First(k => k.Kind == Effect.Kind);
		set => Write(Effect with { Kind = value.Kind });
	}

	public bool ShowZones => Effect.Kind != TriggerKind.Off;

	public bool ShowEnd => Effect.Kind == TriggerKind.Weapon;

	public bool ShowFrequency => Effect.Kind == TriggerKind.Vibration;

	public string StrengthLabel => Effect.Kind == TriggerKind.Vibration ? "Amplitude" : "Strength";

	public int StartMinimum => Effect.Kind == TriggerKind.Weapon ? 2 : 0;

	public int StartMaximum => Effect.Kind == TriggerKind.Weapon ? 7 : 9;

	public int EndMinimum => Effect.Start + 1;

	public int Start
	{
		get => Effect.Start;
		set => Write(Effect with { Start = value });
	}

	public int End
	{
		get => Effect.End;
		set => Write(Effect with { End = value });
	}

	public int Strength
	{
		get => Effect.Strength;
		set => Write(Effect with { Strength = value });
	}

	public int Frequency
	{
		get => Effect.Frequency;
		set => Write(Effect with { Frequency = value });
	}

	// The range games see, in percent (0-30): the first Deadzone of the pull sends nothing, then output starts at StartAt.
	public int Deadzone
	{
		get => (int)Math.Round(Effect.Deadzone * 100);
		set => Write(Effect with { Deadzone = value / 100.0 });
	}

	public int StartAt
	{
		get => (int)Math.Round(Effect.StartAt * 100);
		set => Write(Effect with { StartAt = value / 100.0 });
	}

	internal void Refresh()
	{
		OnPropertyChanged(string.Empty);
	}

	private void Write(TriggerEffectSettings effect)
	{
		page.WriteEffect(isLeft, Normalize(effect));
	}

	private static TriggerEffectSettings Normalize(TriggerEffectSettings effect)
	{
		bool weapon = effect.Kind == TriggerKind.Weapon;
		int start = weapon ? Math.Clamp(effect.Start, 2, 7) : Math.Clamp(effect.Start, 0, 9);
		return effect with
		{
			Start = start,
			// Only Weapon has an end zone; it must come after the start.
			End = weapon ? Math.Clamp(effect.End, start + 1, 8) : effect.End,
			Strength = Math.Clamp(effect.Strength, 1, 8),
			Frequency = Math.Clamp(effect.Frequency, 1, 255),
			Deadzone = Math.Clamp(effect.Deadzone, 0, TriggerRange.Max),
			StartAt = Math.Clamp(effect.StartAt, 0, TriggerRange.Max)
		};
	}
}
