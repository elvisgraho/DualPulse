using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Core.Protocol;

namespace DualPulse.App.ViewModels;

// The Sticks page: a deadzone and a full-tilt point per stick (or one pair for both), per game profile, with a live
// preview of what games get. It reshapes the sticks only through a virtual controller.
public sealed partial class SticksViewModel : ObservableObject
{
	private readonly ISettingsService settings;

	private readonly IControllerService controller;

	private readonly ProfileSelection? selection;

	private readonly UiContext ui = new();

	private readonly Func<InputSnapshot?> latest;

	private readonly Func<TimeSpan, Task> delay;

	// latest and delay: where the stick positions come from and how measuring waits (tests replace both).
	public SticksViewModel(ISettingsService settings, IControllerService controller, ProfileSelection? selection = null,
		Func<InputSnapshot?>? latest = null, Func<TimeSpan, Task>? delay = null)
	{
		this.settings = settings;
		this.controller = controller;
		this.selection = selection;
		this.latest = latest ?? (() => controller.Session?.Latest);
		this.delay = delay ?? Task.Delay;
		Left = new StickEditorViewModel(this, isLeft: true);
		Right = new StickEditorViewModel(this, isLeft: false);
		Strip = new ProfileStripViewModel(selection, ProfilePart.Sticks, "sticks");
		settings.Changed += _ => ui.Run(Refresh);
		if (selection != null)
		{
			selection.Changed += () => ui.Run(Refresh);
		}
	}

	public ProfileStripViewModel Strip { get; }

	public StickEditorViewModel Left { get; }

	public StickEditorViewModel Right { get; }

	// Games read the controller directly in Native mode, so nothing here applies.
	public bool ShowNativeNote => settings.Current.Emulation == null;

	internal StickSettings Sticks => selection?.Selected?.Sticks ?? settings.Current.EffectiveSticks;

	public bool Linked
	{
		get => Sticks.Linked;
		set
		{
			if (value != Sticks.Linked)
			{
				// Unlinking starts the right stick from the shared settings.
				Write(Sticks with { Linked = value, Right = value ? Sticks.Right : Sticks.Left });
			}
		}
	}

	// Samples the sticks for the previews.
	public void RefreshPreview()
	{
		InputSnapshot? now = latest();
		Left.ShowPosition(now?.LeftX ?? 128, now?.LeftY ?? 128);
		Right.ShowPosition(now?.RightX ?? 128, now?.RightY ?? 128);
	}

	// Watches the resting stick(s) for about two seconds; null when no controller is connected. While linked, the left
	// card measures both sticks.
	internal async Task<List<(byte X, byte Y)>?> SampleRestingAsync(bool isLeft)
	{
		const int Samples = 100;
		List<(byte X, byte Y)> resting = [];
		for (int i = 0; i < Samples; i++)
		{
			if (latest() is not { } now)
			{
				return null;
			}
			if (isLeft || Linked)
			{
				resting.Add((now.LeftX, now.LeftY));
			}
			if (!isLeft || Linked)
			{
				resting.Add((now.RightX, now.RightY));
			}
			await delay(TimeSpan.FromMilliseconds(20));
		}
		return resting;
	}

	internal void WriteRange(bool isLeft, StickRange range)
	{
		Write(isLeft ? Sticks with { Left = range } : Sticks with { Right = range });
	}

	private void Write(StickSettings sticks)
	{
		if (selection?.Selected != null)
		{
			if (Strip.CanEdit)
			{
				selection.Update(p => p with { Sticks = sticks });
			}
			return;
		}
		if (sticks != settings.Current.EffectiveSticks)
		{
			settings.Update(s => s with { Sticks = sticks });
		}
	}

	private void Refresh()
	{
		OnPropertyChanged(nameof(Linked));
		OnPropertyChanged(nameof(ShowNativeNote));
		Strip.Refresh();
		Left.Refresh();
		Right.Refresh();
	}
}

// One stick's (or both sticks') deadzone and full-tilt point, in percent, and where the stick is: raw and as games
// get it, for the preview.
public sealed partial class StickEditorViewModel : ObservableObject
{
	private readonly SticksViewModel page;

	private readonly bool isLeft;

	private (byte X, byte Y) raw = (128, 128);

	internal StickEditorViewModel(SticksViewModel page, bool isLeft)
	{
		this.page = page;
		this.isLeft = isLeft;
	}

	public string Title => isLeft ? (page.Linked ? "Left and right stick" : "Left stick") : "Right stick";

	// What Measure found (or why it couldn't).
	[ObservableProperty]
	public partial string MeasureStatus { get; private set; } = "";

	private StickRange Range => isLeft ? page.Sticks.Left : page.Sticks.Right;

	public int Deadzone
	{
		get => (int)Math.Round(Range.Deadzone * 100);
		set => page.WriteRange(isLeft, Range with { Deadzone = Math.Clamp(value, 0, 30) / 100.0 });
	}

	public int FullTiltAt
	{
		get => (int)Math.Round(Range.FullTiltAt * 100);
		set => page.WriteRange(isLeft, Range with { FullTiltAt = Math.Clamp(value, 70, 100) / 100.0 });
	}

	// -1..1 on each axis, for the preview.
	[ObservableProperty]
	public partial Avalonia.Point RawPosition { get; private set; }

	[ObservableProperty]
	public partial Avalonia.Point GamePosition { get; private set; }

	// Sets the deadzone just above how far the resting stick drifts.
	[RelayCommand]
	private async Task Measure()
	{
		MeasureStatus = "Let go of the stick...";
		List<(byte X, byte Y)>? resting = await page.SampleRestingAsync(isLeft);
		if (resting == null)
		{
			MeasureStatus = "Connect your controller first.";
			return;
		}
		int percent = StickShape.SuggestDeadzone(resting);
		Deadzone = percent;
		MeasureStatus = $"Set to {percent}%.";
	}

	internal void ShowPosition(byte x, byte y)
	{
		if ((x, y) == raw)
		{
			return;
		}
		raw = (x, y);
		Update();
	}

	internal void Refresh()
	{
		OnPropertyChanged(string.Empty);
		Update();
	}

	private void Update()
	{
		RawPosition = Normalized(raw.X, raw.Y);
		(byte gx, byte gy) = StickShape.Apply(raw.X, raw.Y, Range.Deadzone, Range.FullTiltAt);
		GamePosition = Normalized(gx, gy);
	}

	private static Avalonia.Point Normalized(byte x, byte y) => new((x - 127.5) / 127.5, (y - 127.5) / 127.5);
}
