using Microsoft.Extensions.Logging;
using DualPulse.Core.Features;
using DualPulse.Core.Protocol;

namespace DualPulse.Core.Devices;

// One connected controller. A read thread parses every input report (no UI work) and a write thread sends the
// newest OutputState: each report carries the full state, so bursts of changes coalesce into one write.
// Stop() closes the connection, which unblocks the reader; the reader then marks the session as ended.
public sealed class ControllerSession : IDisposable
{
	private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);

	private static readonly TimeSpan FinalWriteTimeout = TimeSpan.FromMilliseconds(250);

	// Right after connecting (above all after a USB/Bluetooth switch) the controller can ignore the first output report
	// and keep its own blue connection light; output is otherwise only sent on change, so it is re-sent at 0.5, 1.5 and
	// 2.5 s while the controller settles.
	private static readonly TimeSpan SettleFirstResend = TimeSpan.FromMilliseconds(500);

	private static readonly TimeSpan SettleResendInterval = TimeSpan.FromSeconds(1);

	private const int SettleResends = 3;

	// Controller-side audio buffering for report 0x39 (16-128; 100 played smoothly where 48 was choppy).
	private const byte AudioBufferLength = 100;

	private static readonly IReadOnlySet<int> DualSenseProductIds = new HashSet<int> { 0x0CE6, 0x0DF2 };

	private readonly IHidTransport transport;

	private readonly BluetoothLayout layout;

	private readonly ILogger logger;

	private readonly TimeProvider time;

	private readonly LinkQualityMeter linkQuality = new();

	private readonly object outputGate = new();

	private readonly AutoResetEvent writeSignal = new(initialState: false);

	private readonly ManualResetEventSlim writerDone = new(initialState: false);

	private IHidConnection? connection;

	private OutputState output = OutputState.Default;

	private bool outputPending;

	private volatile bool stopRequested;

	private volatile bool reading;

	private volatile bool writing;

	// Published as an immutable box: InputSnapshot is a large struct, and copying it while the read thread writes it
	// could mix two reports. A reference swap is atomic.
	private volatile SnapshotBox? latest;

	private ITimer? settleTimer;

	private int settleResendCount;

	// A settle re-send repeats the trigger effects too (the controller may have missed the first report).
	private bool resendTriggers;

	private readonly object writeLock = new();

	private readonly AutoResetEvent hapticsSignal = new(false);

	private Thread? hapticsThread;

	private long reportsRead;

	private long lastReportTimestamp;

	private DualSenseBattery battery;

	public ControllerSession(HidDeviceInfo device, IHidTransport transport, BluetoothLayout layout, ILogger logger, TimeProvider? time = null)
	{
		Device = device;
		this.transport = transport;
		this.layout = layout;
		this.logger = logger;
		this.time = time ?? TimeProvider.System;
		Transport = ControllerDiscovery.IsBluetooth(device.Path) ? Transport.Bluetooth : Transport.Usb;
		Activity = new InputActivityTracker(this.time.GetUtcNow().UtcDateTime);
	}

	public HidDeviceInfo Device { get; }

	public Transport Transport { get; }

	public InputSnapshot? Latest => latest?.Value;

	public DualSenseBattery Battery => battery;

	public FirmwareInfo? Firmware { get; private set; }

	public LinkQualitySnapshot? LinkQuality => linkQuality.Current;

	public InputActivityTracker Activity { get; }

	public bool IsReading => reading;

	internal bool IsWriting => writing;

	public OutputState Output
	{
		get
		{
			lock (outputGate)
			{
				return output;
			}
		}
	}

	// Raised on the read thread for every parsed report; handlers must be quick.
	public event Action<InputSnapshot>? InputReceived;

	// Opens the device (throws if that fails), reads firmware info, then starts the reader and writer.
	public void Start()
	{
		connection = transport.Open(Device);
		reading = true;
		writing = true;
		if (DualSenseProductIds.Contains(Device.ProductId))
		{
			ReadFirmware(connection);
		}
		new Thread(ReadLoop) { Name = "Controller read", IsBackground = true, Priority = ThreadPriority.Highest }.Start();
		new Thread(WriteLoop) { Name = "Controller write", IsBackground = true, Priority = ThreadPriority.AboveNormal }.Start();
		settleTimer = time.CreateTimer(_ => ResendWhileSettling(), null, SettleFirstResend, SettleResendInterval);
	}

	private void ResendWhileSettling()
	{
		if (stopRequested || Interlocked.Increment(ref settleResendCount) > SettleResends)
		{
			settleTimer?.Dispose();
			return;
		}
		lock (outputGate)
		{
			outputPending = true;
			resendTriggers = true;
		}
		writeSignal.Set();
	}

	// Game rumble as haptics over Bluetooth (report 0x32): streamed every 10.67 ms while rumble is on, then a short
	// silence, then nothing. Ignored over USB. The state should have HapticsMode on so report 0x31 leaves the
	// actuators to the stream.
	public void SetHapticRumble(byte large, byte small)
	{
		if (Transport != Transport.Bluetooth || stopRequested)
		{
			return;
		}
		HapticMixer.SetRumble(large, small);
		WakeHaptics();
	}

	// What the haptics stream plays over Bluetooth: rumble (SetHapticRumble) plus pushed audio frames.
	public HapticsMixer HapticMixer { get; } = new();

	// Audio for the controller's speaker or headset over Bluetooth (report 0x39); the path and volume come from the
	// output state (AudioOutput, AudioVolume).
	public SpeakerStream Speaker { get; } = new();

	// Starts the audio lane if it is idle (after pushing speaker audio); ignored over USB.
	public void WakeAudio() => WakeHaptics();

	// Starts the haptics stream if it is idle (after pushing audio); ignored over USB.
	public void WakeHaptics()
	{
		if (Transport != Transport.Bluetooth || stopRequested)
		{
			return;
		}
		lock (outputGate)
		{
			if (hapticsThread == null)
			{
				hapticsThread = new Thread(HapticsLoop) { Name = "Controller haptics", IsBackground = true, Priority = ThreadPriority.Highest };
				hapticsThread.Start();
			}
		}
		hapticsSignal.Set();
	}

	public void Apply(Func<OutputState, OutputState> change)
	{
		lock (outputGate)
		{
			output = change(output);
			outputPending = true;
		}
		writeSignal.Set();
	}

	// The controller's Bluetooth address (feature report 0x09), needed to turn it off; null if unavailable.
	public long? ReadBluetoothAddress()
	{
		try
		{
			return connection != null && !stopRequested && Power.BluetoothPower.TryReadMac(connection, out long mac) ? mac : null;
		}
		catch (Exception exception)
		{
			logger.LogDebug(exception, "Pairing info unavailable for {Path}", Device.Path);
			return null;
		}
	}

	public void Stop(string reason)
	{
		if (stopRequested)
		{
			return;
		}
		logger.LogInformation("Stopping session {Path}: {Reason} ({Reports})", Device.Path, reason, ReportSummary());
		// Leave the controller neutral (triggers released, motors still, lightbar off) instead of frozen in the last
		// state: the writer sends this before it exits, and the connection closes once it has (or after a short wait).
		lock (outputGate)
		{
			output = OutputState.Default with { ImprovedRumble = output.ImprovedRumble };
			outputPending = true;
		}
		stopRequested = true;
		settleTimer?.Dispose();
		hapticsSignal.Set();
		writeSignal.Set();
		if (connection != null && writing)
		{
			writerDone.Wait(FinalWriteTimeout);
		}
		connection?.Dispose();
	}

	public void Dispose()
	{
		Stop("disposed");
	}

	// The Bluetooth audio lane. Sleeps until there is something to play, then:
	// - While speaker audio is active: report 0x39 every 21.33 ms (two haptic blocks from the mixer and two Opus
	//   frames), primed with three silent reports each time it starts.
	// - Otherwise: haptics alone in report 0x32 every 10.67 ms until the stream's silence tail ends.
	// 1 ms timer resolution while sending.
	private void HapticsLoop()
	{
		IHidConnection hid = connection!;
		byte[] buffer = new byte[Math.Max(Device.MaxOutputLength, AudioReport.Length)];
		HapticsStream stream = new();
		sbyte[] hapticsLeft = new sbyte[2 * HapticsReport.Frames];
		sbyte[] hapticsRight = new sbyte[2 * HapticsReport.Frames];
		byte[] opus = new byte[2 * AudioReport.OpusFrameLength];
		long epoch = time.GetTimestamp();
		bool failing = false;
		byte sequence = 0;
		byte audioCounter = 0;
		while (!stopRequested)
		{
			hapticsSignal.WaitOne(500);
			if (stopRequested)
			{
				break;
			}
			TimeSpan next = time.GetElapsedTime(epoch);
			bool audioPlaying = false;
			int primes = 0;
			TimerResolution.Begin();
			try
			{
				while (!stopRequested)
				{
					int length;
					TimeSpan interval;
					if (Speaker.IsActive)
					{
						if (!audioPlaying)
						{
							audioPlaying = true;
							primes = 3;
							logger.LogInformation("Bluetooth audio lane: speaker audio on (report 0x39)");
						}
						HapticMixer.Fill(hapticsLeft.AsSpan(0, HapticsReport.Frames), hapticsRight.AsSpan(0, HapticsReport.Frames));
						HapticMixer.Fill(hapticsLeft.AsSpan(HapticsReport.Frames), hapticsRight.AsSpan(HapticsReport.Frames));
						if (primes > 0)
						{
							primes--;
							Speaker.Silence(opus);
						}
						else
						{
							Speaker.Next(opus);
						}
						length = AudioReport.Build(buffer, sequence, audioCounter, AudioBufferLength, hapticsLeft, hapticsRight, Output.UsesHeadset, opus);
						audioCounter += 2;
						interval = AudioReport.Interval;
					}
					else
					{
						if (audioPlaying)
						{
							audioPlaying = false;
							logger.LogInformation("Bluetooth audio lane: speaker audio off");
						}
						if (!stream.Next(HapticMixer, time.GetElapsedTime(epoch), buffer))
						{
							break;
						}
						length = HapticsReport.Length;
						interval = HapticsReport.Interval;
					}
					sequence = (byte)((sequence + 1) & 0x0F);
					buffer.AsSpan(length).Clear();
					try
					{
						lock (writeLock)
						{
							hid.Write(buffer);
						}
						failing = false;
					}
					catch (Exception exception)
					{
						if (!failing)
						{
							failing = true;
							logger.LogWarning("Controller rejected audio or haptics on {Path}: {Error}", Device.Path, exception.Message);
						}
					}
					next += interval;
					while (!stopRequested && time.GetElapsedTime(epoch) < next)
					{
						Thread.Sleep(1);
					}
				}
			}
			finally
			{
				TimerResolution.End();
			}
		}
	}

	// For the log: how much input this session got, and how long ago the last report was.
	private string ReportSummary()
	{
		long count = Interlocked.Read(ref reportsRead);
		return count == 0
			? "no input reports"
			: $"{count} input reports, last {(int)time.GetElapsedTime(Interlocked.Read(ref lastReportTimestamp)).TotalMilliseconds} ms ago";
	}

	private void ReadFirmware(IHidConnection hid)
	{
		try
		{
			byte[] buffer = new byte[Math.Max(Device.MaxFeatureLength, 64)];
			buffer[0] = FirmwareInfo.ReportId;
			hid.GetFeature(buffer);
			Firmware = FirmwareInfo.Parse(buffer);
		}
		catch (Exception exception)
		{
			logger.LogDebug(exception, "Firmware info unavailable for {Path}", Device.Path);
		}
		if (Firmware != null)
		{
			logger.LogInformation("{Firmware}", Firmware.Summary);
			bool improved = Firmware.SupportsImprovedRumble;
			Apply(state => state with { ImprovedRumble = improved });
		}
	}

	private void ReadLoop()
	{
		IHidConnection hid = connection!;
		byte[] buffer = new byte[Math.Max(Device.MaxInputLength, 78)];
		double ticksPerTimestamp = (double)TimeSpan.TicksPerSecond / time.TimestampFrequency;
		long started = time.GetTimestamp();
		try
		{
			while (!stopRequested)
			{
				int length = hid.Read(buffer, ReadTimeout);
				if (!DualSenseInputParser.TryParse(buffer.AsSpan(0, length), out InputSnapshot snapshot))
				{
					continue;
				}
				if (Interlocked.Increment(ref reportsRead) == 1)
				{
					logger.LogInformation("First input from {Path} after {Ms} ms: report 0x{Id:X2}, {Length} bytes, output layout {Layout}",
						Device.Path, (int)time.GetElapsedTime(started).TotalMilliseconds, buffer[0], length, layout);
				}
				Interlocked.Exchange(ref lastReportTimestamp, time.GetTimestamp());
				latest = new SnapshotBox(snapshot);
				battery = DualSenseBatteryStatus.Parse(snapshot.BatteryStatus);
				linkQuality.Observe(snapshot.BluetoothSequence, (long)(time.GetTimestamp() * ticksPerTimestamp));
				uint buttons = (uint)snapshot.Buttons;
				Activity.Observe(snapshot.LeftX, snapshot.LeftY, snapshot.RightX, snapshot.RightY, snapshot.L2, snapshot.R2, (byte)buttons, (byte)(buttons >> 8), (byte)(buttons >> 16), time.GetUtcNow().UtcDateTime);
				try
				{
					InputReceived?.Invoke(snapshot);
				}
				catch (Exception exception)
				{
					logger.LogError(exception, "Input handler failed");
				}
			}
		}
		catch (Exception exception)
		{
			if (!stopRequested)
			{
				logger.LogWarning("Reader for {Path} stopped: {Error} ({Reports})", Device.Path, exception.Message, ReportSummary());
			}
		}
		finally
		{
			reading = false;
			stopRequested = true;
			writeSignal.Set();
			hid.Dispose();
		}
	}

	private void WriteLoop()
	{
		IHidConnection hid = connection!;
		byte[] buffer = new byte[Math.Max(Device.MaxOutputLength, OutputReportBuilder.BluetoothLength)];
		byte sequence = 0;
		bool failing = false;
		// The trigger effects last sent: unchanged ones stay out of later reports (see OutputReportBuilder.Build).
		byte[]? sentTriggers = null;
		// The last report written carried a game's block: the next composed one resets what the game may have set.
		bool gameWasDriving = false;
		// The motors ran in the last report written: the next one selects classic rumble too (to stop them at least).
		bool motorsRan = false;
		// Diagnostics: the first classic-rumble selection of the session is logged (it switches a DualSense's haptics off).
		bool rumbleSelectionLogged = false;
		try
		{
			while (true)
			{
				bool stopping = stopRequested;
				if (!stopping)
				{
					writeSignal.WaitOne(500);
					stopping = stopRequested;
				}
				OutputState? state = null;
				bool forceTriggers = false;
				lock (outputGate)
				{
					if (outputPending)
					{
						state = output;
						outputPending = false;
						forceTriggers = resendTriggers;
						resendTriggers = false;
					}
				}
				if (state != null)
				{
					byte[] triggers = [.. state.RightTrigger.Bytes, .. state.LeftTrigger.Bytes];
					// A game coming or going changes who has the triggers: DualPulse's go out again.
					bool sendTriggers = forceTriggers || stopping || sentTriggers == null || gameWasDriving != (state.GameBlock != null)
						|| !triggers.AsSpan().SequenceEqual(sentTriggers);
					bool motorsRun = state.LeftMotor != 0 || state.RightMotor != 0;
					int length = OutputReportBuilder.Build(state, Transport, sequence, layout, buffer, sendTriggers, resetPower: gameWasDriving && state.GameBlock == null,
						motors: motorsRun || motorsRan);
					sequence = (byte)((sequence + 1) & 0x0F);
					// Windows expects writes of the device's full output report length (547 bytes over Bluetooth).
					int writeLength = Math.Max(length, Device.MaxOutputLength);
					buffer.AsSpan(length, writeLength - length).Clear();
					try
					{
						lock (writeLock)
						{
							hid.Write(buffer.AsSpan(0, writeLength));
						}
						sentTriggers = triggers;
						gameWasDriving = state.GameBlock != null;
						if ((motorsRun || motorsRan) && !(Transport == Transport.Bluetooth && state.HapticsMode) && !rumbleSelectionLogged)
						{
							rumbleSelectionLogged = true;
							logger.LogInformation("Selected classic rumble on {Transport} (motors {Left}/{Right}): haptics are off until the controller is switched off", Transport, state.LeftMotor, state.RightMotor);
						}
						motorsRan = motorsRun;
						if (failing)
						{
							failing = false;
							logger.LogInformation("Writes to {Path} work again", Device.Path);
						}
					}
					catch (Exception exception)
					{
						// A vanished device is detected by the reader; a failed write is retried with the next state.
						// The first failure of a run is a warning (lighting and rumble stop reaching the controller).
						if (!failing && !stopping)
						{
							failing = true;
							logger.LogWarning("Controller rejected writes to {Path}: {Error}", Device.Path, exception.Message);
						}
						else
						{
							logger.LogDebug("Write to {Path} failed: {Error}", Device.Path, exception.Message);
						}
					}
				}
				// The pending state at stop time (the neutral one) is written once, then the writer exits.
				if (stopping)
				{
					break;
				}
			}
		}
		finally
		{
			writing = false;
			writerDone.Set();
		}
	}

	private sealed class SnapshotBox(InputSnapshot value)
	{
		public readonly InputSnapshot Value = value;
	}
}
