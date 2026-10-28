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
// runs on a short timer from the view. Device names and volume are looked up in the background about once a second
// (asking Windows for them takes long enough to stall the waveform).
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

	private readonly PeakHistory levels = new(binsPerSecond: 50);

	private Task<DeviceInfo>? deviceCheck;

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

	// The waveforms: peaks with the bar still filling last, and how full it is (see Waveform).
	[ObservableProperty]
	public partial float[] RealPeaks { get; private set; } = [];

	[ObservableProperty]
	public partial double RealProgress { get; private set; }

	[ObservableProperty]
	public partial double VirtualProgress { get; private set; }

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

	// When the slider last moved: device checks started before then don't bring back the old volume.
	private long volumeSetAt;

	// The controller microphone's Windows input volume, 0-100 (written off the UI thread).
	public int Volume
	{
		get => volume;
		set
		{
			value = Math.Clamp(value, 0, 100);
			if (SetProperty(ref volume, value))
			{
				volumeSetAt = time.GetTimestamp();
				IControllerUsbAudio? audio = usbAudio;
				_ = Task.Run(() => audio?.SetMicrophoneVolume(value));
			}
		}
	}

	private MicrophoneSettings Microphone => settings.Current.EffectiveMicrophone;

	// What games hear (DualSense mode): the low cut, the noise gate and a boost of 0-12 dB.
	public bool LowCut
	{
		get => Microphone.LowCut;
		set => UpdateMicrophone(m => m with { LowCut = value });
	}

	public bool NoiseGate
	{
		get => Microphone.NoiseGate;
		set => UpdateMicrophone(m => m with { NoiseGate = value });
	}

	// Where the gate opens, -70 to -20 dBFS.
	public int GateThresholdDb
	{
		get => Microphone.GateThresholdDb;
		set => UpdateMicrophone(m => m with { GateThresholdDb = Math.Clamp(value, -70, -20) });
	}

	// The gate's threshold on the top waveform (linear, 0-1); 0 when the gate doesn't apply. The gate measures average
	// loudness (RMS) and the waveform shows peaks, which in speech sit about 10 dB higher, so the line is drawn there.
	public double GateLine => ShowVirtual && NoiseGate ? Math.Pow(10, (GateThresholdDb + 10) / 20.0) : 0;

	public int BoostDb
	{
		get => Microphone.BoostDb;
		set => UpdateMicrophone(m => m with { BoostDb = Math.Clamp(value, 0, 12) });
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
		if (deviceCheck is { IsCompleted: true } done)
		{
			deviceCheck = null;
			if (done.IsCompletedSuccessfully)
			{
				Apply(done.Result);
			}
		}
		if (deviceCheck == null && (lastDeviceCheck is not { } checkedAt || time.GetElapsedTime(checkedAt) >= DeviceCheckInterval))
		{
			long startedAt = time.GetTimestamp();
			lastDeviceCheck = startedAt;
			IControllerUsbAudio? audio = usbAudio;
			deviceCheck = Task.Run(() =>
			{
				(string? real, string? virtualName) = audio?.MicrophoneNames() ?? (null, null);
				return new DeviceInfo(onCable ? real : null, virtualName, onCable ? audio?.MicrophoneVolume() : null, startedAt);
			});
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
		(RealPeaks, RealProgress) = levels.Live();

		bool showVirtual = settings.Current.Emulation == PadKind.DualSense;
		if (showVirtual != ShowVirtual)
		{
			ShowVirtual = showVirtual;
			OnPropertyChanged(nameof(GateLine));
		}
		bool recording = monitor?.InUse == true;
		VirtualStatus = recording ? "A game or app is recording from it." : "Not in use.";
		(VirtualPeaks, VirtualProgress) = monitor?.Levels.Live() ?? ([], 0);
	}

	private sealed record DeviceInfo(string? RealName, string? VirtualName, int? Volume, long StartedAt);

	private void Apply(DeviceInfo info)
	{
		RealName = info.RealName;
		VirtualName = info.VirtualName;
		HasVolume = info.Volume != null;
		if (info.Volume is { } percent && percent != volume && info.StartedAt > volumeSetAt)
		{
			volume = percent;
			OnPropertyChanged(nameof(Volume));
		}
		OnPropertyChanged(nameof(MicHint));
		OnPropertyChanged(nameof(MuteButtonMutesMic));
		OnPropertyChanged(nameof(SelectedMicLed));
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

	private void UpdateMicrophone(Func<MicrophoneSettings, MicrophoneSettings> change)
	{
		MicrophoneSettings updated = change(Microphone);
		if (updated != Microphone)
		{
			settings.Update(s => s with { Microphone = updated });
			OnPropertyChanged(nameof(LowCut));
			OnPropertyChanged(nameof(NoiseGate));
			OnPropertyChanged(nameof(GateThresholdDb));
			OnPropertyChanged(nameof(GateLine));
			OnPropertyChanged(nameof(BoostDb));
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
}
