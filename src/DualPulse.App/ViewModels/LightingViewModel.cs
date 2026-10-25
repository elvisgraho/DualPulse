using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Core.Features;
using DualPulse.Core.Protocol;

namespace DualPulse.App.ViewModels;

public sealed record ModeChoice(string Label, LightbarMode Mode);

public sealed record PlayerLedChoice(string Label, PlayerLedPattern Pattern);

public sealed record LedBrightnessChoice(string Label, LedBrightness Brightness);

public sealed record MicLedChoice(string Label, MicLed MicLed);

// The Lighting page. Every change goes straight to the settings (LightingService applies it); changes made elsewhere,
// such as the Home swatches, flow back into the page through ISettingsService.Changed.
public sealed partial class LightingViewModel : ObservableObject
{
	private readonly ISettingsService settings;

	private readonly IControllerService controller;

	private readonly IMicMuteService? micMute;

	private readonly UiContext ui = new();

	private double hue;

	private double saturation;

	private string hexColour = "";

	public LightingViewModel(ISettingsService settings, IControllerService controller, IMicMuteService? micMute = null)
	{
		this.micMute = micMute;
		if (micMute != null)
		{
			micMute.Changed += () => ui.Run(() => OnPropertyChanged(nameof(MicHint)));
		}
		this.settings = settings;
		this.controller = controller;
		SyncColour(settings.Current.EffectiveLightbar);
		settings.Changed += current => ui.Run(() => OnSettingsChanged(current));
	}

	public IReadOnlyList<SwatchChoice> Swatches => SwatchChoice.For(settings.Current.EffectiveLightbar);

	public IReadOnlyList<ModeChoice> ModeChoices { get; } =
	[
		new("Static", LightbarMode.Static),
		new("Breathing", LightbarMode.Breathing),
		new("Rainbow", LightbarMode.Rainbow),
		new("Battery", LightbarMode.Battery)
	];

	public IReadOnlyList<PlayerLedChoice> PlayerLedChoices { get; } =
	[
		new("Off", PlayerLedPattern.Off),
		new("Player 1", PlayerLedPattern.Player1),
		new("Player 2", PlayerLedPattern.Player2),
		new("Player 3", PlayerLedPattern.Player3),
		new("Player 4", PlayerLedPattern.Player4),
		new("All", PlayerLedPattern.All),
		new("Battery level", PlayerLedPattern.Battery)
	];

	public IReadOnlyList<LedBrightnessChoice> PlayerLedBrightnessChoices { get; } =
	[
		new("High", LedBrightness.High),
		new("Medium", LedBrightness.Medium),
		new("Low", LedBrightness.Low)
	];

	public IReadOnlyList<MicLedChoice> MicLedChoices { get; } =
	[
		new("Off", MicLed.Off),
		new("On", MicLed.On),
		new("Pulse", MicLed.Pulse)
	];

	[ObservableProperty]
	public partial bool HexInvalid { get; private set; }

	// The colour the controller shows right now (animated modes included), refreshed by the view.
	[ObservableProperty]
	public partial Rgb Preview { get; private set; }

	private LightingSettings Lighting => settings.Current.EffectiveLighting;

	public bool ColourApplies => Lighting.Mode is LightbarMode.Static or LightbarMode.Breathing;

	public ModeChoice SelectedMode
	{
		get => ModeChoices.First(c => c.Mode == Lighting.Mode);
		set => UpdateLighting(l => l with { Mode = value.Mode });
	}

	public int Brightness
	{
		get => Lighting.Brightness;
		set => UpdateLighting(l => l with { Brightness = Math.Clamp(value, 0, 100) });
	}

	public PlayerLedChoice SelectedPlayerLeds
	{
		get => PlayerLedChoices.First(c => c.Pattern == Lighting.PlayerLeds);
		set => UpdateLighting(l => l with { PlayerLeds = value.Pattern });
	}

	public LedBrightnessChoice SelectedPlayerLedBrightness
	{
		get => PlayerLedBrightnessChoices.First(c => c.Brightness == Lighting.PlayerLedBrightness);
		set => UpdateLighting(l => l with { PlayerLedBrightness = value.Brightness });
	}

	public bool LowBatteryFlash
	{
		get => Lighting.LowBatteryFlash;
		set => UpdateLighting(l => l with { LowBatteryFlash = value });
	}

	public bool MuteButtonMutesMic
	{
		get => Lighting.MuteButtonMutesMic;
		set => UpdateLighting(l => l with { MuteButtonMutesMic = value });
	}

	// Shown instead of the mute-light choice while the Mute button drives the microphone.
	public string MicHint => micMute?.IsMicMuted == null ? "No microphone found." : "Shows whether your microphone is muted.";

	public MicLedChoice SelectedMicLed
	{
		get => MicLedChoices.First(c => c.MicLed == Lighting.MicLed);
		set => UpdateLighting(l => l with { MicLed = value.MicLed });
	}

	// 0-360; with Saturation it picks a custom colour at full value (brightness is separate).
	public double Hue
	{
		get => hue;
		set
		{
			if (SetProperty(ref hue, value))
			{
				SetColour(LightingEngine.FromHueSaturation(hue, saturation));
			}
		}
	}

	// 0-100.
	public double Saturation
	{
		get => saturation;
		set
		{
			if (SetProperty(ref saturation, value))
			{
				SetColour(LightingEngine.FromHueSaturation(hue, saturation));
			}
		}
	}

	// "#RRGGBB" (the # is optional when typing). An invalid entry is flagged and leaves the colour unchanged.
	public string HexColour
	{
		get => hexColour;
		set
		{
			if (!SetProperty(ref hexColour, value))
			{
				return;
			}
			string digits = value.Trim().TrimStart('#');
			if (digits.Length == 6 && int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
			{
				HexInvalid = false;
				SetColour(new Rgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
			}
			else
			{
				HexInvalid = true;
			}
		}
	}

	public void RefreshPreview()
	{
		Preview = controller.Session?.Output.Lightbar
			?? LightingEngine.Lightbar(Lighting.Mode, settings.Current.EffectiveLightbar, Lighting.Brightness, TimeSpan.Zero, 100);
	}

	[RelayCommand]
	private void SetColour(Rgb colour)
	{
		if (settings.Current.Lightbar != colour)
		{
			settings.Update(s => s with { Lightbar = colour });
		}
	}

	private void UpdateLighting(Func<LightingSettings, LightingSettings> change)
	{
		LightingSettings updated = change(Lighting);
		if (updated != Lighting)
		{
			settings.Update(s => s with { Lighting = updated });
		}
	}

	private void OnSettingsChanged(AppSettings current)
	{
		SyncColour(current.EffectiveLightbar);
		OnPropertyChanged(nameof(Swatches));
		OnPropertyChanged(nameof(SelectedMode));
		OnPropertyChanged(nameof(Brightness));
		OnPropertyChanged(nameof(SelectedPlayerLeds));
		OnPropertyChanged(nameof(SelectedPlayerLedBrightness));
		OnPropertyChanged(nameof(SelectedMicLed));
		OnPropertyChanged(nameof(LowBatteryFlash));
		OnPropertyChanged(nameof(MuteButtonMutesMic));
		OnPropertyChanged(nameof(ColourApplies));
		RefreshPreview();
	}

	// Brings hex, hue and saturation in line with the stored colour without writing it back. Hue and saturation keep
	// their values when the colour is grey or black, where hue is undefined.
	private void SyncColour(Rgb colour)
	{
		string hex = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
		if (!string.Equals(hex, hexColour.Trim().TrimStart('#').Insert(0, "#"), StringComparison.OrdinalIgnoreCase))
		{
			hexColour = hex;
			HexInvalid = false;
			OnPropertyChanged(nameof(HexColour));
		}
		double max = Math.Max(colour.R, Math.Max(colour.G, colour.B));
		double min = Math.Min(colour.R, Math.Min(colour.G, colour.B));
		if (max == 0 || max == min)
		{
			return;
		}
		if (LightingEngine.FromHueSaturation(hue, saturation) == colour)
		{
			return;
		}
		double delta = max - min;
		double h = max == colour.R ? (colour.G - colour.B) / delta % 6 : max == colour.G ? (colour.B - colour.R) / delta + 2 : (colour.R - colour.G) / delta + 4;
		hue = (h * 60 + 360) % 360;
		saturation = delta / max * 100;
		OnPropertyChanged(nameof(Hue));
		OnPropertyChanged(nameof(Saturation));
	}
}
