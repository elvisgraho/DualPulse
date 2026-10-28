using CommunityToolkit.Mvvm.ComponentModel;
using DualPulse.App.Services;
using DualPulse.Core.Audio;
using DualPulse.Core.Devices;
using DualPulse.Core.Protocol;
using DualPulse.Emulation;

namespace DualPulse.App.ViewModels;

public sealed record MicLedChoice(string Label, MicLed MicLed);

// The Microphone page: the controller's own microphone (its Windows name, the last seconds of sound, a status line and
// its Windows input volume), what games hear through the virtual DualSense's microphone (DualSense mode only), and the
// Mute button settings. The controller's microphone is only recorded while the page is shown (Start/Stop); Refresh
// runs on a short timer from the view.
public sealed partial class MicrophoneViewModel : ObservableObject
{
	private static readonly TimeSpan SilentFor = TimeSpan.FromSeconds(1.5);

	private static readonly TimeSpan DeviceCheckInterval = TimeSpan.FromSeconds(1);

	private readonly IControllerService controller;

	private readonly ISettingsService settings;

	private readonly IControllerUsbAudio? usbAudio;

	private readonly VirtualMicrophoneMonitor? monitor;

	private readonly IMicMuteService? micMute;

	private readonly TimeProvider time;

	private readonly PeakHistory levels = new();

	private bool started;

	private IDisposable? capture;

	private long? lastOpenAttempt;

	private long? lastDeviceCheck;

	private long lastFrames;

	private long lastSoundAt;

	public MicrophoneViewModel(IControllerService controller, ISettingsService settings, IControllerUsbAudio? usbAudio = null,
		VirtualMicrophoneMonitor? monitor = null, IMicMuteService? micMute = null, TimeProvider? time = null)
	{
		this.controller = controller;
		this.settings = settings;
		this.usbAudio = usbAudio;
		this.monitor = monitor;
		this.micMute = micMute;
		this.time = time ?? TimeProvider.System;
	}

	[ObservableProperty]
	public partial string Status { get; private set; } = "";

	[ObservableProperty]
	public partial string? RealName { get; private set; }

	[ObservableProperty]
	public partial float[] RealPeaks { get; private set; } = [];

	[ObservableProperty]
	public partial bool HasVolume { get; private set; }

	[ObservableProperty]
	public partial bool ShowVirtual { get; private set; }

	[ObservableProperty]
	public partial string? VirtualName { get; private set; }

	[ObservableProperty]
	public partial string VirtualStatus { get; private set; } = "";

	[ObservableProperty]
	public partial float[] VirtualPeaks { get; private set; } = [];

	private int volume;

	// The controller microphone's Windows input volume, 0-100.
	public int Volume
	{
		get => volume;
		set
		{
			value = Math.Clamp(value, 0, 100);
			if (SetProperty(ref volume, value))
			{
				usbAudio?.SetMicrophoneVolume(value);
			}
		}
	}

	public IReadOnlyList<MicLedChoice> MicLedChoices { get; } =
	[
		new("Off", MicLed.Off),
		new("On", MicLed.On),
		new("Pulse", MicLed.Pulse)
	];

	private LightingSettings Lighting => settings.Current.EffectiveLighting;

	public bool MuteButtonMutesMic
	{
		get => Lighting.MuteButtonMutesMic;
		set => UpdateLighting(l => l with { MuteButtonMutesMic = value });
	}

	public string MicHint => micMute?.IsMicMuted == null ? "No microphone found." : "Shows whether your microphone is muted.";

	public MicLedChoice SelectedMicLed
	{
		get => MicLedChoices.First(c => c.MicLed == Lighting.MicLed);
		set => UpdateLighting(l => l with { MicLed = value.MicLed });
	}

	// The page is shown: the controller's microphone may be recorded for the waveform.
	public void Start()
	{
		started = true;
		lastDeviceCheck = null;
		Refresh();
	}

	public void Stop()
	{
		started = false;
		CloseCapture();
	}

	public void Refresh()
	{
		ControllerSession? session = controller.Session;
		bool onCable = session?.Transport == Transport.Usb;
		if (lastDeviceCheck is not { } checkedAt || time.GetElapsedTime(checkedAt) >= DeviceCheckInterval)
		{
			lastDeviceCheck = time.GetTimestamp();
			(string? real, string? virtualName) = usbAudio?.MicrophoneNames() ?? (null, null);
			RealName = onCable ? real : null;
			VirtualName = virtualName;
			int? current = onCable ? usbAudio?.MicrophoneVolume() : null;
			HasVolume = current != null;
			if (current is { } percent && percent != volume)
			{
				volume = percent;
				OnPropertyChanged(nameof(Volume));
			}
			OnPropertyChanged(nameof(MicHint));
			OnPropertyChanged(nameof(MuteButtonMutesMic));
			OnPropertyChanged(nameof(SelectedMicLed));
		}
		if (started && onCable)
		{
			OpenCapture();
		}
		else
		{
			CloseCapture();
		}
		long frames = levels.FramesSeen;
		if (frames != lastFrames)
		{
			lastFrames = frames;
			lastSoundAt = time.GetTimestamp();
		}
		Status = session == null ? "Connect your controller."
			: !onCable ? "Over Bluetooth, Windows gets no microphone from the controller. Plug in the cable to use it."
			: capture == null ? "Windows hasn't found the controller's microphone yet."
			: time.GetElapsedTime(lastSoundAt) >= SilentFor ? "No sound from the controller. Turn it off and on again while it's plugged in."
			: "Working.";
		RealPeaks = levels.Snapshot();

		ShowVirtual = settings.Current.Emulation == PadKind.DualSense;
		bool recording = monitor?.InUse == true;
		VirtualStatus = recording ? "A game or app is recording from it." : "Not in use.";
		VirtualPeaks = monitor?.Levels.Snapshot() ?? [];
	}

	private void OpenCapture()
	{
		if (capture != null || usbAudio == null || (lastOpenAttempt is { } attempt && time.GetElapsedTime(attempt) < DeviceCheckInterval))
		{
			return;
		}
		lastOpenAttempt = time.GetTimestamp();
		try
		{
			capture = usbAudio.OpenMicrophone(levels.Push);
		}
		catch (Exception)
		{
			capture = null;
		}
		lastSoundAt = time.GetTimestamp();
	}

	private void CloseCapture()
	{
		capture?.Dispose();
		capture = null;
		lastOpenAttempt = null;
		levels.Clear();
	}

	private void UpdateLighting(Func<LightingSettings, LightingSettings> change)
	{
		LightingSettings updated = change(Lighting);
		if (updated != Lighting)
		{
			settings.Update(s => s with { Lighting = updated });
		}
	}
}
