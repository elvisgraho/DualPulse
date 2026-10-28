using System.Text.Json.Serialization;
using DualPulse.Core.Features;
using DualPulse.Core.Mapping;
using DualPulse.Core.Power;
using DualPulse.Core.Protocol;
using DualPulse.Emulation;

namespace DualPulse.App.Services;

public enum TriggerPreset
{
	Off,
	Feedback,
	Weapon,
	Vibration,
	// Set on the Triggers page; the effects themselves are in AppSettings.Triggers.
	Custom
}

// Lighting beyond the lightbar colour. Brightness is 0-100 and scales the lightbar in every mode.
public sealed record LightingSettings(
	LightbarMode Mode = LightbarMode.Static,
	int Brightness = 100,
	PlayerLedPattern PlayerLeds = PlayerLedPattern.Player1,
	LedBrightness PlayerLedBrightness = LedBrightness.High,
	MicLed MicLed = MicLed.Off,
	bool LowBatteryFlash = true,
	bool MuteButtonMutesMic = true);

public enum RumbleMode
{
	// Improved rumble on firmware that supports it (update 02.24 or newer).
	Automatic,
	Classic,
	Improved
}

// Game rumble. Strength is 0-200 (%); values over 100 boost weak rumble up to the motors' maximum.
// BluetoothHaptics plays rumble as haptics (report 0x32) over Bluetooth instead of the emulated motors. AudioHaptics
// adds what the computer plays to that stream (Bluetooth only), scaled by AudioHapticsIntensity (0-2) and shaped by
// AudioHapticsStyle.
public sealed record RumbleSettings(int Strength = 100, bool Muted = false, RumbleMode Mode = RumbleMode.Automatic, bool BluetoothHaptics = true,
	bool AudioHaptics = false, double AudioHapticsIntensity = 1, DualPulse.Core.Protocol.HapticsAudioStyle AudioHapticsStyle = DualPulse.Core.Protocol.HapticsAudioStyle.Punchy);

public enum ControllerAudioOutput
{
	// Earbuds when something is in the jack, the speaker otherwise.
	Auto,
	Speaker,
	Earbuds
}

// PC sound on the controller over Bluetooth: the output device whose sound is forwarded (null = the default output),
// where it plays and how loud (0-100).
// What games hear from the virtual DualSense's microphone: a 100 Hz low cut, the click-proof noise gate (opening at
// GateThresholdDb, -70 to -20 dBFS) and a boost of 0-12 dB (see MicrophoneProcessor).
public sealed record MicrophoneSettings(int BoostDb = 0, bool NoiseGate = true, int GateThresholdDb = -45, bool LowCut = true);

public sealed record ControllerAudioSettings(bool Enabled = false, string? DeviceId = null, ControllerAudioOutput Output = ControllerAudioOutput.Auto, int Volume = 70);

// The DSX-compatible mod server. Port 0 picks a free port (used by tests). WritePortFiles writes the port where
// DSX v3 (%LOCALAPPDATA%\DSX) and v2 (C:\Temp\DualSenseX) mods look for it.
public sealed record ModServerSettings(bool Enabled = false, int Port = 6969, bool WritePortFiles = true);

// PC control options. Buttons maps button names (Buttons enum) to actions; null uses PcControl.DefaultButtons.
public sealed record MappingSettings(
	bool TouchpadEnabled = true,
	double TouchpadSpeed = 1,
	bool StickMouseEnabled = true,
	double StickSpeed = 1,
	bool StickScrollEnabled = true,
	IReadOnlyDictionary<string, PcAction>? Buttons = null,
	bool WindowSwitcherEnabled = true,
	bool MediaLayerEnabled = true)
{
	public IReadOnlyDictionary<Buttons, PcAction> ButtonActions()
	{
		if (Buttons == null)
		{
			return PcControl.DefaultButtons;
		}
		Dictionary<Buttons, PcAction> actions = [];
		foreach ((string name, PcAction action) in Buttons)
		{
			if (Enum.TryParse(name, out Buttons button) && button != Core.Protocol.Buttons.Ps)
			{
				actions[button] = action;
			}
		}
		return actions;
	}
}

public enum GyroOutput
{
	// Added to the virtual pad's right stick: works in every game with controller support.
	Stick,
	// Mouse movement: for games that aim better with a mouse.
	Mouse
}

public enum GyroActivation
{
	WhileL2,
	WhileL1,
	WhileR1,
	Always
}

// Gyro aiming. Offset* is the calibrated resting bias in raw sensor counts.
public sealed record GyroSettings(
	bool Enabled = false,
	GyroOutput Output = GyroOutput.Stick,
	GyroActivation Activation = GyroActivation.WhileL2,
	double Sensitivity = 2,
	bool InvertX = false,
	bool InvertY = false,
	GyroAxis Axis = GyroAxis.Yaw,
	short OffsetX = 0,
	short OffsetY = 0,
	short OffsetZ = 0);

// User settings, stored as settings.json. Null Lightbar means the accent colour; null Emulation means native mode
// (no virtual pad); null Power and null Lighting mean their defaults. HiddenDeviceIds records which devices DualPulse
// hid through HidHide, so exactly those are restored when hiding is turned off (also after a crash).
// GameSoundOnController: a game driving the virtual DualSense also plays its controller-speaker sound on the real one.
// CheckForUpdates: look up the latest release on GitHub (off until the user turns it on).
// ButtonRemaps: buttons sent to games as other buttons (with a virtual controller). Sticks: stick deadzones (null =
// unchanged). GameLights / GameTriggers: in DualSense mode, whether a game may set the lights / adaptive triggers
// (off keeps the user's own). ChosenProfile: the named profile picked by hand (its Exe key), applied while no game profile is.
// SetupDone: the first-start window was closed. NotifiedUpdate: the release version last announced with a pop-up.
public sealed record AppSettings(
	int Version = 1,
	PadKind? Emulation = null,
	Rgb? Lightbar = null,
	TriggerPreset TriggerPreset = TriggerPreset.Off,
	PowerSettings? Power = null,
	bool StartMinimized = false,
	LightingSettings? Lighting = null,
	TriggerSettings? Triggers = null,
	RumbleSettings? Rumble = null,
	bool HideController = false,
	IReadOnlyList<string>? HiddenDeviceIds = null,
	ModServerSettings? ModServer = null,
	bool StartWithWindows = false,
	MappingSettings? Mapping = null,
	GyroSettings? Gyro = null,
	IReadOnlyList<GameProfile>? Profiles = null,
	ControllerAudioSettings? Audio = null,
	bool GameSoundOnController = false,
	bool CheckForUpdates = false,
	bool SetupDone = false,
	string? NotifiedUpdate = null,
	IReadOnlyList<ButtonRemap>? ButtonRemaps = null,
	StickSettings? Sticks = null,
	string? ChosenProfile = null,
	bool GameLights = true,
	bool GameTriggers = true,
	MicrophoneSettings? Microphone = null,
	bool ControllerShortcuts = true)
{
	public static readonly Rgb AccentLightbar = new(0xC9, 0xA8, 0x6A);

	[JsonIgnore]
	public PowerSettings EffectivePower => Power ?? new PowerSettings();

	[JsonIgnore]
	public Rgb EffectiveLightbar => Lightbar ?? AccentLightbar;

	[JsonIgnore]
	public LightingSettings EffectiveLighting => Lighting ?? new LightingSettings();

	[JsonIgnore]
	public GyroSettings EffectiveGyro => Gyro ?? new GyroSettings();

	[JsonIgnore]
	public StickSettings EffectiveSticks => Sticks ?? new StickSettings();

	[JsonIgnore]
	public ControllerAudioSettings EffectiveAudio => Audio ?? new ControllerAudioSettings();

	[JsonIgnore]
	public MicrophoneSettings EffectiveMicrophone => Microphone ?? new MicrophoneSettings();

	[JsonIgnore]
	public IReadOnlyList<GameProfile> EffectiveProfiles => Profiles ?? [];

	[JsonIgnore]
	public MappingSettings EffectiveMapping => Mapping ?? new MappingSettings();

	[JsonIgnore]
	public ModServerSettings EffectiveModServer => ModServer ?? new ModServerSettings();

	[JsonIgnore]
	public RumbleSettings EffectiveRumble => Rumble ?? new RumbleSettings();

	// Settings saved before the Triggers page existed only have TriggerPreset.
	[JsonIgnore]
	public TriggerSettings EffectiveTriggers => Triggers ?? TriggerSettings.FromPreset(TriggerPreset);
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(ProfileFileData))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
