using HidSharp;

// Hardware probe for development: captures real input reports as test fixtures and (Task 4) sends output reports.
// Not product code. Reads raw report bytes directly so fixtures don't depend on the parser they are meant to test.

const int SonyVendorId = 0x054C;
int[] productIds = [0x0CE6, 0x0DF2];

if (args.Length >= 2 && args[0] == "capture-all")
{
	string? from = args.SkipWhile(a => a != "--from").Skip(1).FirstOrDefault();
	return CaptureAll(args[1], TimeSpan.FromSeconds(args.Length >= 3 && int.TryParse(args[2], out int s) ? s : 240), from);
}
if (args.Length >= 4 && args[0] == "lightbar")
{
	DualPulse.Core.Protocol.Rgb color = new(byte.Parse(args[1]), byte.Parse(args[2]), byte.Parse(args[3]));
	DualPulse.Core.Protocol.BluetoothLayout layout = args.Contains("--legacy-bt") ? DualPulse.Core.Protocol.BluetoothLayout.Legacy : DualPulse.Core.Protocol.BluetoothLayout.Sequenced;
	string? only = args.SkipWhile(a => a != "--only").Skip(1).FirstOrDefault();
	return SendLightbar(color, layout, only);
}
if (args.Length >= 1 && args[0] == "xinput-rumble")
{
	return XInputRumble();
}
if (args.Length >= 1 && args[0] == "mod-demo")
{
	return ModDemo(args.Length >= 2 ? int.Parse(args[1]) : 6969);
}
if (args.Length >= 1 && args[0] == "hidhide-status")
{
	return HidHideStatus(args.Length >= 2 ? args[1] : null);
}
if (args.Length >= 1 && args[0] == "dualsense-pad")
{
	// The app's own path: UsbIpDualSensePad through usbip-win2, a tone to its speaker endpoint, then unplugged.
	HashSet<string> before = [.. RenderEndpoints().Select(e => e.Id)];
	using DualPulse.Emulation.UsbIpDualSensePad pad = DualPulse.Emulation.UsbIpDualSensePad.Create(new DualPulse.Emulation.UsbIpWinAttacher());
	int frames = 0, peak = 0, blocks = 0;
	bool lost = false;
	pad.Speaker += (samples, channels) => { frames++; foreach (short s in samples) { peak = Math.Max(peak, Math.Abs((int)s)); } };
	pad.OutputBlock += _ => blocks++;
	pad.Lost += () => lost = true;
	Console.WriteLine("pad created");
	for (int i = 0; i < 40; i++)
	{
		pad.Submit(new DualPulse.Core.Protocol.InputSnapshot { LeftX = 0x80, LeftY = 0x80, RightX = 0x80, RightY = 0x80 });
		Thread.Sleep(100);
	}
	(string Id, string Name)? endpoint = RenderEndpoints().Cast<(string Id, string Name)?>().FirstOrDefault(e => !before.Contains(e!.Value.Id) && e.Value.Name.Contains("DualSense"));
	Console.WriteLine(endpoint == null ? "no speaker endpoint" : $"playing to {endpoint.Value.Name}");
	if (endpoint != null)
	{
		PlayQuadTone(endpoint.Value.Id, TimeSpan.FromSeconds(3));
	}
	Console.WriteLine($"audio transfers {frames}, peak {peak}, output blocks {blocks}, lost {lost}");
	return 0;
}
if (args.Length >= 1 && args[0] == "usb-channel-meter")
{
	// Plays 120 Hz on channels 3-4 only into the real controller's sound device and reads Windows' per-channel meter.
	using NAudio.CoreAudioApi.MMDeviceEnumerator enumerator = new();
	NAudio.CoreAudioApi.MMDevice? real = enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active)
		.FirstOrDefault(d => d.FriendlyName.Contains("DualSense", StringComparison.OrdinalIgnoreCase) && !DualPulse.Emulation.WindowsControllerUsbAudio.IsVirtual(d));
	if (real == null)
	{
		Console.Error.WriteLine("No real DualSense sound device.");
		return 1;
	}
	Console.WriteLine($"{real.FriendlyName}: channel mask {(real.AudioClient.MixFormat as NAudio.Wave.WaveFormatExtensible)?.ToString()}");
	using DualPulse.Core.Audio.IControllerAudioStream? audio = new DualPulse.Emulation.WindowsControllerUsbAudio().Open();
	short[] block = new short[480 * 4];
	long n = 0;
	for (int b = 0; b < 200; b++)
	{
		for (int i = 0; i < 480; i++)
		{
			short value = (short)(0.9 * 32767 * Math.Sin(2 * Math.PI * 120 * n++ / 48000));
			block[4 * i + 2] = value;
			block[4 * i + 3] = value;
		}
		audio!.Write(block, 4);
		Thread.Sleep(10);
		if (b % 40 == 39)
		{
			NAudio.CoreAudioApi.AudioMeterInformationChannels peaks = real.AudioMeterInformation.PeakValues;
			string levels = string.Join(" ", Enumerable.Range(0, peaks.Count).Select(c => $"ch{c + 1} {peaks[c]:0.00}"));
			Console.WriteLine($"meter: master {real.AudioMeterInformation.MasterPeakValue:0.00}  {levels}");
		}
	}
	return 0;
}
if (args.Length >= 1 && args[0] == "usb-haptics-matrix")
{
	// Which output report leaves a USB DualSense playing haptics audio: five steps, each a report then a 3 s buzz
	// (120 Hz on channels 3-4) and a 2 s pause. DualPulse must be closed and the controller visible (not hidden).
	HidDevice? real = DeviceList.Local.GetHidDevices(SonyVendorId).FirstOrDefault(d => productIds.Contains(d.ProductID)
		&& !DualPulse.Core.Devices.ControllerDiscovery.IsBluetooth(d.DevicePath) && string.IsNullOrEmpty(SafeSerial(d)));
	if (real == null)
	{
		Console.Error.WriteLine("No real DualSense on USB (is it hidden, or DualPulse holding it?).");
		return 1;
	}
	using HidStream hid = real.Open();
	using DualPulse.Core.Audio.IControllerAudioStream? audio = new DualPulse.Emulation.WindowsControllerUsbAudio().Open();
	if (audio == null)
	{
		Console.Error.WriteLine("No real DualSense sound device.");
		return 1;
	}
	void Report(string name, byte flag0, byte flag1, byte flag2)
	{
		byte[] report = new byte[48];
		report[0] = 0x02;
		report[1 + 0] = flag0;
		report[1 + 1] = flag1;
		report[1 + 38] = flag2;
		hid.Write(report);
		Console.WriteLine($"{DateTime.Now:HH:mm:ss} report {name}: flag0 {flag0:X2} flag1 {flag1:X2} flag2 {flag2:X2}");
	}
	void Buzz(int step)
	{
		Console.WriteLine($"{DateTime.Now:HH:mm:ss}   buzz {step}");
		short[] block = new short[480 * 4];
		long n = 0;
		for (int b = 0; b < 300; b++)
		{
			for (int i = 0; i < 480; i++)
			{
				short value = (short)(0.9 * 32767 * Math.Sin(2 * Math.PI * 120 * n++ / 48000));
				block[4 * i + 2] = value;
				block[4 * i + 3] = value;
			}
			audio.Write(block, 4);
			Thread.Sleep(10);
		}
		Thread.Sleep(2000);
	}
	void Audio(string name, byte flag0, byte route, byte headphone = 0x63, byte speaker = 0x64, byte mic = 0x40)
	{
		byte[] report = new byte[48];
		report[0] = 0x02;
		report[1 + 0] = flag0;
		report[1 + 1] = 0x62;
		report[1 + 4] = headphone;
		report[1 + 5] = speaker;
		report[1 + 6] = mic;
		report[1 + 7] = route;
		hid.Write(report);
		Console.WriteLine($"{DateTime.Now:HH:mm:ss} report {name}: flag0 {flag0:X2} volumes {headphone:X2} {speaker:X2} {mic:X2} route {route:X2}");
	}
	Thread.Sleep(3000);
	if (args.Contains("--fresh"))
	{
		// Round 3, on a controller just switched on over the cable: first no report at all.
		Console.WriteLine($"{DateTime.Now:HH:mm:ss} 1 no report at all");
		Buzz(1);
		Report("2 empty report", 0x00, 0x00, 0x00);
		Buzz(2);
		Report("3 only the haptics low-pass flag (flag1 0x20)", 0x00, 0x20, 0x00);
		Buzz(3);
		Report("4 power save and motor power, no low-pass flag", 0x00, 0x42, 0x00);
		Buzz(4);
		Report("5 classic rumble select", 0x02, 0x00, 0x04);
		Buzz(5);
		return 0;
	}
	if (args.Contains("--audio"))
	{
		// Round 2: the audio settings Cyberpunk sends (volumes, route), which round 1 left out.
		Audio("1 audio route only (speaker path, 0x3C)", 0x80, 0x3C);
		Buzz(1);
		Audio("2 volumes and route, as the game sends them", 0xF0, 0x3C);
		Buzz(2);
		Audio("3 volumes and route 0x0C (the game's first route)", 0xF0, 0x0C);
		Buzz(3);
		Audio("4 volumes and headphone route (0x00)", 0xF0, 0x00);
		Buzz(4);
		Audio("5 volumes only, no route", 0x70, 0x00);
		Buzz(5);
		return 0;
	}
	Report("1 haptics setup, no motor bits", 0x00, 0x62, 0x00);
	Buzz(1);
	Report("2 compatible vibration bit (a game's first report)", 0x01, 0x62, 0x00);
	Buzz(2);
	Report("3 classic rumble select (DualPulse's first report on USB)", 0x02, 0x62, 0x04);
	Buzz(3);
	Report("4 after 3: compatible vibration bit", 0x01, 0x62, 0x00);
	Buzz(4);
	Report("5a classic rumble select again", 0x02, 0x62, 0x04);
	Report("5b then haptics setup, no motor bits", 0x00, 0x62, 0x00);
	Buzz(5);
	return 0;
}
if (args.Length >= 1 && args[0] == "usb-controller-audio")
{
	// Which DualSense speaker endpoints are DualPulse's virtual pad, and that the real one opens (one second of silence).
	using (NAudio.CoreAudioApi.MMDeviceEnumerator enumerator = new())
	{
		foreach (NAudio.CoreAudioApi.MMDevice device in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
		{
			if (device.FriendlyName.Contains("DualSense", StringComparison.OrdinalIgnoreCase))
			{
				Console.WriteLine($"{device.FriendlyName}: {(DualPulse.Emulation.WindowsControllerUsbAudio.IsVirtual(device) ? "virtual" : "real")}, volume {device.AudioEndpointVolume.MasterVolumeLevelScalar:0.00}, muted {device.AudioEndpointVolume.Mute}, format {device.AudioClient.MixFormat}");
			}
		}
	}
	using DualPulse.Core.Audio.IControllerAudioStream? stream = new DualPulse.Emulation.WindowsControllerUsbAudio().Open();
	Console.WriteLine(stream == null ? "no real DualSense sound device" : "opened the real controller's sound device");
	if (stream != null)
	{
		short[] silence = new short[480 * 4];
		for (int i = 0; i < 100; i++)
		{
			stream.Write(silence, 4);
			Thread.Sleep(10);
		}
		Console.WriteLine("wrote 1 s of silence");
	}
	return 0;
}
if (args.Length >= 1 && args[0] == "endpoint-props")
{
	// Every string property of each DualSense render endpoint: what tells the real controller's from the virtual one.
	using NAudio.CoreAudioApi.MMDeviceEnumerator enumerator = new();
	foreach (NAudio.CoreAudioApi.MMDevice device in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
	{
		if (!device.FriendlyName.Contains("DualSense", StringComparison.OrdinalIgnoreCase))
		{
			continue;
		}
		Console.WriteLine($"{device.FriendlyName}  [{device.ID}]");
		for (int i = 0; i < device.Properties.Count; i++)
		{
			try
			{
				NAudio.CoreAudioApi.PropertyStoreProperty property = device.Properties[i];
				if (property.Value is string text)
				{
					Console.WriteLine($"  {device.Properties.Get(i).formatId},{device.Properties.Get(i).propertyId} = {text}");
				}
			}
			catch (Exception)
			{
			}
		}
	}
	return 0;
}
if (args.Length >= 1 && args[0] == "default-audio")
{
	// Reads the default output and sets it to itself (harmless): proves the switch-back path works on this PC.
	using DualPulse.Emulation.WindowsDefaultAudioOutput output = new();
	DualPulse.Core.Audio.AudioDevice? current = output.Current;
	Console.WriteLine($"default output: {current?.Name ?? "none"}");
	if (current != null)
	{
		bool taken = output.Set(current.Id);
		Console.WriteLine($"setting it again {(taken ? "worked" : "FAILED")}: {output.Current?.Name ?? "none"}");
	}
	return 0;
}
if (args.Length >= 1 && args[0] == "quad-tone")
{
	// Plays to the given endpoint id, or every DualSense render endpoint (for recording another USB/IP server's audio traffic).
	// quad-tone [seconds] [endpoint id] [hz] [amplitude 0-1]
	double hz = args.Length >= 4 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 440;
	double amplitude = args.Length >= 5 ? double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0.3;
	foreach ((string id, string name) in RenderEndpoints().Where(e => args.Length >= 3 ? e.Id == args[2] : e.Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase)))
	{
		Console.WriteLine($"playing {hz} Hz at {amplitude:0.00} to {name}");
		PlayQuadTone(id, TimeSpan.FromSeconds(args.Length >= 2 ? int.Parse(args[1]) : 4), hz, amplitude);
	}
	return 0;
}
if (args.Length >= 1 && args[0] == "windows")
{
	foreach (DualPulse.Emulation.RunningWindow window in new DualPulse.Emulation.RunningWindows().List(Environment.ProcessId))
	{
		Console.WriteLine($"{window.Exe,-32} {window.Title}");
	}
	return 0;
}
if (args.Length >= 1 && args[0] == "foreground")
{
	using DualPulse.Emulation.ForegroundWatcher watcher = new();
	Console.WriteLine($"watching={watcher.IsWatching} now={watcher.ForegroundExe ?? "(unreadable)"}");
	watcher.Changed += exe => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {exe ?? "(unreadable)"}");
	Thread.Sleep(TimeSpan.FromSeconds(args.Length >= 2 ? int.Parse(args[1]) : 30));
	return 0;
}
if (args.Length >= 1 && args[0] == "speaker-tone")
{
	int flagsIndex = Array.IndexOf(args, "--flags");
	byte flags = flagsIndex > 0 && flagsIndex + 1 < args.Length ? Convert.ToByte(args[flagsIndex + 1], 16) : (byte)0x7E;
	int bufferIndex = Array.IndexOf(args, "--buflen");
	byte bufferLength = bufferIndex > 0 && bufferIndex + 1 < args.Length ? byte.Parse(args[bufferIndex + 1]) : (byte)100;
	return SpeakerTone(headset: args.Contains("--headset"), flags, bufferLength);
}
if (args.Length >= 1 && args[0] == "haptics-tone")
{
	double hz = args.Length >= 2 && double.TryParse(args[1], System.Globalization.CultureInfo.InvariantCulture, out double h) ? h : 150;
	return HapticsTone(hz, raw: args.Contains("--raw"), setup: !args.Contains("--no-setup"));
}
if (args.Length >= 1 && args[0] == "usbip-dualsense")
{
	// Serves DualPulse's in-house virtual DualSense over USB/IP on port 3250, attaches it with usbip-win2 for N seconds
	// (default 20), reports what Windows does with it, then detaches.
	int seconds = args.Length >= 2 ? int.Parse(args[1]) : 20;
	DualPulse.Core.Usb.VirtualDualSense virtualPad = new("1-1");
	using DualPulse.Core.UsbIp.UsbIpServer usbip = new(3250);
	usbip.Add(virtualPad);
	int blocks = 0;
	int speakerFrames = 0;
	int hapticPeak = 0;
	int speakerPeak = 0;
	bool tone = args.Contains("--tone");
	byte[]? lastBlock = null;
	virtualPad.OutputBlock += block => { Interlocked.Increment(ref blocks); lastBlock = block; };
	virtualPad.Speaker += (samples, channels) =>
	{
		Interlocked.Increment(ref speakerFrames);
		foreach (short sample in samples[..Math.Min(samples.Length, 64)])
		{
			speakerPeak = Math.Max(speakerPeak, Math.Abs((int)sample));
		}
	};
	virtualPad.Haptics += (samples, channels) =>
	{
		foreach (short sample in samples)
		{
			hapticPeak = Math.Max(hapticPeak, Math.Abs((int)sample));
		}
	};
	HashSet<string> endpointsBefore = tone ? [.. RenderEndpoints().Select(e => e.Id)] : [];
	if (args.Contains("--trace"))
	{
		virtualPad.Trace = line => Console.WriteLine($"  {line}");
	}
	usbip.Imported += bus => Console.WriteLine($"imported {bus}");
	usbip.Released += bus => Console.WriteLine($"released {bus}");
	usbip.Start();
	string tool = File.Exists(@"C:\Program Files\USBip\usbip.exe") ? @"C:\Program Files\USBip\usbip.exe" : "usbip";
	System.Diagnostics.Process attach = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tool, "-t 3250 attach -r 127.0.0.1 -b 1-1 --once --terse")
	{
		RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
	})!;
	string attachOutput = attach.StandardOutput.ReadToEnd() + attach.StandardError.ReadToEnd();
	attach.WaitForExit();
	Console.WriteLine($"usbip attach exit {attach.ExitCode}: {attachOutput.Trim()}");
	if (tone)
	{
		_ = Task.Run(() =>
		{
			Thread.Sleep(4000);
			(string Id, string Name)? ours = RenderEndpoints().Cast<(string Id, string Name)?>().FirstOrDefault(e => !endpointsBefore.Contains(e!.Value.Id) && e.Value.Name.Contains("DualSense"));
			if (ours == null)
			{
				Console.WriteLine("no new DualSense speaker endpoint found");
				return;
			}
			Console.WriteLine($"playing a 4-channel tone on {ours.Value.Name}");
			try
			{
				PlayQuadTone(ours.Value.Id, TimeSpan.FromSeconds(4));
			}
			catch (Exception error)
			{
				Console.WriteLine($"tone failed: {error.Message}");
			}
		});
	}
	byte[] neutral = new byte[64];
	neutral[0] = 0x01;
	neutral[1] = neutral[2] = neutral[3] = neutral[4] = 0x80;
	neutral[8] = 0x08;
	System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
	TimeSpan nextReport = TimeSpan.FromSeconds(2);
	while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
	{
		neutral[7]++;
		virtualPad.SetInput(neutral);
		Thread.Sleep(4);
		if (clock.Elapsed >= nextReport)
		{
			nextReport += TimeSpan.FromSeconds(2);
			Console.WriteLine($"{clock.Elapsed.TotalSeconds,4:0}s  output blocks {blocks}, audio transfers {speakerFrames} (speaker peak {speakerPeak}, haptics peak {hapticPeak}), last block {(lastBlock == null ? "-" : Convert.ToHexString(lastBlock))}");
		}
	}
	if (int.TryParse(attachOutput.Trim(), out int hubPort))
	{
		System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tool, $"detach -p {hubPort}") { UseShellExecute = false, CreateNoWindow = true })!.WaitForExit();
		Console.WriteLine($"detached port {hubPort}");
	}
	return 0;
}
if (args.Length >= 1 && args[0] == "feature-reports")
{
	// Reads feature reports 0x05, 0x09, 0x20 and 0x22 from the real DualSense on USB (read-only; skips virtual pads).
	HidDevice? real = DeviceList.Local.GetHidDevices(SonyVendorId).FirstOrDefault(d => productIds.Contains(d.ProductID)
		&& !DualPulse.Core.Devices.ControllerDiscovery.IsBluetooth(d.DevicePath)
		&& !DualPulse.Core.Devices.ControllerDiscovery.IsOwnVirtual(new DualPulse.Core.Devices.HidDeviceInfo(d.DevicePath, d.VendorID, d.ProductID, 0, 0, 0, SafeSerial(d)))
		&& string.IsNullOrEmpty(SafeSerial(d)));
	if (real == null)
	{
		Console.Error.WriteLine("No real DualSense on USB.");
		return 1;
	}
	using HidStream stream = real.Open();
	foreach (byte id in new byte[] { 0x05, 0x09, 0x20, 0x22 })
	{
		byte[] buffer = new byte[real.GetMaxFeatureReportLength()];
		buffer[0] = id;
		try
		{
			stream.GetFeature(buffer);
			Console.WriteLine($"feature 0x{id:X2} {Convert.ToHexString(buffer)}");
		}
		catch (Exception error)
		{
			Console.WriteLine($"feature 0x{id:X2} failed: {error.Message}");
		}
	}
	return 0;
}
if (args.Length >= 1 && args[0] == "usb-descriptors")
{
	// Reads the real DualSense's USB descriptors through its hub (read-only): device, configuration, strings, HID report.
	return UsbDescriptors.Dump(args.Length >= 2 ? args[1] : ".");
}
if (args.Length >= 1 && args[0] == "audio-devices")
{
	foreach (DualPulse.Core.Audio.AudioDevice device in new DualPulse.Emulation.WasapiLoopbackSourceFactory().OutputDevices())
	{
		Console.WriteLine($"{device.Name}  [{device.Id}]");
	}
	return 0;
}
if (args.Length >= 1 && args[0] == "audio-level")
{
	// Prints the loopback level (peak per half second) of what Windows is playing, for 5 s.
	using DualPulse.Emulation.WasapiLoopbackSource source = new();
	float peak = 0;
	source.Blocks += samples =>
	{
		foreach (float sample in samples)
		{
			peak = Math.Max(peak, Math.Abs(sample));
		}
	};
	if (!source.Start())
	{
		Console.Error.WriteLine("No output device to capture.");
		return 1;
	}
	Console.WriteLine($"capturing {source.SampleRate} Hz, {source.Channels} channels");
	for (int i = 0; i < 10; i++)
	{
		Thread.Sleep(500);
		Console.WriteLine($"peak {peak:0.000}");
		peak = 0;
	}
	return 0;
}
if (args.Length >= 1 && args[0] == "mic")
{
	// Prints the default recording device's mute state; "mic toggle" flips it, waits, and flips it back.
	using DualPulse.Emulation.WindowsMicrophone mic = new();
	mic.Changed += () => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} changed: muted={mic.IsMuted?.ToString() ?? "(no device)"}");
	Console.WriteLine($"muted={mic.IsMuted?.ToString() ?? "(no device)"}");
	if (args.Length >= 2 && args[1] == "toggle")
	{
		Console.WriteLine($"toggle: {mic.Toggle()}");
		Thread.Sleep(1500);
		Console.WriteLine($"toggle back: {mic.Toggle()}");
		Thread.Sleep(500);
	}
	if (args.Length >= 3 && args[1] == "watch")
	{
		Thread.Sleep(TimeSpan.FromSeconds(int.Parse(args[2])));
	}
	return 0;
}
Console.Error.WriteLine("usage: DualPulse.Probe capture-all <fixturesDir> [timeoutSeconds] [--from <fixture>]");
Console.Error.WriteLine("       DualPulse.Probe lightbar <r> <g> <b> [--legacy-bt] [--only bt|usb]");
Console.Error.WriteLine("       DualPulse.Probe xinput-rumble   (left/large motor 2 s, pause, right/small motor 2 s)");
return 2;

// Acts like a game mod: sends DSX packets to the mod server, waits between steps and prints each reply.
int ModDemo(int port)
{
	(string Step, string Json)[] steps =
	[
		("R2 weapon (click), L2 bow, lightbar green, all player LEDs, mic LED pulse",
			"""{"instructions":[{"type":1,"parameters":[0,2,22,2,6,8]},{"type":1,"parameters":[0,1,14,1,6,6,8]},{"type":2,"parameters":[0,0,255,0,255]},{"type":6,"parameters":[0,4]},{"type":5,"parameters":[0,1]}]}"""),
		("R2 legacy machine-gun vibration, lightbar red",
			"""{"instructions":[{"type":1,"parameters":[0,2,17,2,8,15]},{"type":2,"parameters":[0,255,0,0]}]}"""),
		("Reset to your settings", """{"instructions":[{"type":7,"parameters":[0]}]}""")
	];
	using System.Net.Sockets.UdpClient client = new(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
	client.Client.ReceiveTimeout = 3000;
	System.Net.IPEndPoint server = new(System.Net.IPAddress.Loopback, port);
	foreach ((string step, string json) in steps)
	{
		Console.WriteLine($"> {step}");
		byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
		client.Send(data, data.Length, server);
		try
		{
			System.Net.IPEndPoint from = new(System.Net.IPAddress.Any, 0);
			Console.WriteLine("  reply: " + System.Text.Encoding.UTF8.GetString(client.Receive(ref from)));
		}
		catch (System.Net.Sockets.SocketException)
		{
			Console.WriteLine("  no reply (is the mod server on?)");
			return 1;
		}
		Thread.Sleep(6000);
	}
	return 0;
}

// Read-only: prints HidHide's configuration, the connected DualSense device instance ids and an app's DOS path.
int HidHideStatus(string? appPath)
{
	DualPulse.Emulation.HidHideDriver driver = new();
	Console.WriteLine($"installed: {driver.IsInstalled}");
	Console.WriteLine($"attached to HID devices: {driver.IsAttached}");
	if (driver.IsInstalled)
	{
		Console.WriteLine($"active: {driver.GetActive()}");
		Console.WriteLine("allowed apps:");
		driver.GetAllowedApps().ToList().ForEach(a => Console.WriteLine("  " + a));
		Console.WriteLine("hidden devices:");
		driver.GetHiddenDevices().ToList().ForEach(d => Console.WriteLine("  " + d));
	}
	Console.WriteLine("connected DualSense instance ids:");
	foreach (HidDevice device in DeviceList.Local.GetHidDevices(SonyVendorId).Where(d => productIds.Contains(d.ProductID)))
	{
		Console.WriteLine($"  {DualPulse.Emulation.HidHideDriver.InstanceIdOf(device.DevicePath) ?? "(lookup failed)"}");
	}
	if (appPath != null)
	{
		Console.WriteLine($"app as DOS device path: {DualPulse.Emulation.HidHideDriver.DosDeviceNameOf(Path.GetFullPath(appPath)) ?? "(not on a lettered drive)"}");
	}
	return 0;
}

// Rumbles the first connected XInput pad the way a game would: left (large) motor, then right (small) motor.
int XInputRumble()
{
	for (uint user = 0; user < 4; user++)
	{
		if (XInput.XInputGetState(user, out _) != 0)
		{
			continue;
		}
		Console.WriteLine($"XInput pad {user}: left (large) motor");
		XInput.Vibration left = new() { Left = 50000 };
		XInput.XInputSetState(user, ref left);
		Thread.Sleep(2000);
		XInput.Vibration off = new();
		XInput.XInputSetState(user, ref off);
		Thread.Sleep(1000);
		Console.WriteLine($"XInput pad {user}: right (small) motor");
		XInput.Vibration right = new() { Right = 50000 };
		XInput.XInputSetState(user, ref right);
		Thread.Sleep(2000);
		XInput.XInputSetState(user, ref off);
		return 0;
	}
	Console.Error.WriteLine("No XInput pad connected.");
	return 1;
}

int SendLightbar(DualPulse.Core.Protocol.Rgb color, DualPulse.Core.Protocol.BluetoothLayout layout, string? only)
{
	DualPulse.Core.Protocol.OutputState state = DualPulse.Core.Protocol.OutputState.Default with { Lightbar = color };
	int sent = 0;
	foreach (HidDevice device in DeviceList.Local.GetHidDevices(SonyVendorId).Where(d => productIds.Contains(d.ProductID)))
	{
		bool bluetooth = DualPulse.Core.Devices.ControllerDiscovery.IsBluetooth(device.DevicePath);
		if (only != null && only != (bluetooth ? "bt" : "usb"))
		{
			continue;
		}
		DualPulse.Core.Protocol.Transport transport = bluetooth ? DualPulse.Core.Protocol.Transport.Bluetooth : DualPulse.Core.Protocol.Transport.Usb;
		// Windows expects every write to be the device's full output report length (547 bytes over Bluetooth).
		byte[] buffer = new byte[Math.Max(device.GetMaxOutputReportLength(), DualPulse.Core.Protocol.OutputReportBuilder.BluetoothLength)];
		int length = DualPulse.Core.Protocol.OutputReportBuilder.Build(state, transport, 0, layout, buffer);
		using HidStream stream = device.Open();
		stream.Write(buffer, 0, Math.Max(length, device.GetMaxOutputReportLength()));
		Console.WriteLine($"sent {color} to {(bluetooth ? "BT" : "USB")} ({(bluetooth ? layout.ToString() : "USB")} layout)");
		sent++;
	}
	return sent > 0 ? 0 : 1;
}

int CaptureAll(string fixturesDir, TimeSpan timeout, string? from)
{
	Directory.CreateDirectory(fixturesDir);
	List<HidDevice> devices = DeviceList.Local.GetHidDevices(SonyVendorId).Where(d => productIds.Contains(d.ProductID)).ToList();
	if (devices.Count == 0)
	{
		Console.Error.WriteLine("No DualSense connected.");
		return 1;
	}
	(string Name, Func<byte[], int, bool> Matches)[] fixtures =
	[
		("idle", Idle),
		("cross", (r, b) => r[b + 7] == 0x28 && r[b + 8] == 0 && (r[b + 9] & 0x07) == 0),
		("dpad-upright", (r, b) => r[b + 7] == 0x01 && r[b + 8] == 0 && (r[b + 9] & 0x07) == 0),
		("ps-create-options", (r, b) => r[b + 8] == 0x30 && (r[b + 9] & 0x07) == 0x01 && (r[b + 7] & 0xF0) == 0),
		("l2-full", (r, b) => r[b + 4] >= 250),
		("left-stick-upleft", (r, b) => r[b + 0] < 64 && r[b + 1] < 64),
		("touch-one", (r, b) => (r[b + 32] & 0x80) == 0 && (((r[b + 34] & 0x0F) << 8) | r[b + 33]) < 960 && (r[b + 36] & 0x80) != 0),
	];
	DateTime deadline = DateTime.UtcNow + timeout;
	List<Thread> threads = [];
	foreach (HidDevice device in devices)
	{
		string transport = device.DevicePath.Contains("00001124", StringComparison.OrdinalIgnoreCase) ? "bt" : "usb";
		Thread thread = new(() =>
		{
			using HidStream stream = device.Open();
			stream.ReadTimeout = 1000;
			int baseOffset = transport == "bt" ? 2 : 1;
			int next = from == null ? 0 : Array.FindIndex(fixtures, f => f.Name == from);
			DateTime? matchSince = null;
			DateTime nextStatus = DateTime.UtcNow;
			while (next < fixtures.Length && DateTime.UtcNow < deadline)
			{
				byte[] report;
				try { report = stream.Read(); } catch (TimeoutException) { continue; }
				if (DateTime.UtcNow >= nextStatus)
				{
					nextStatus = DateTime.UtcNow.AddSeconds(3);
					int b0 = baseOffset;
					Console.WriteLine($"{DateTime.Now:HH:mm:ss} {transport} waiting for {fixtures[next].Name}: LX={report[b0]} LY={report[b0 + 1]} touch0={((report[b0 + 32] & 0x80) == 0 ? "on" : "off")} x={((report[b0 + 34] & 0x0F) << 8) | report[b0 + 33]} touch1={((report[b0 + 36] & 0x80) == 0 ? "on" : "off")}");
				}
				if (fixtures[next].Matches(report, baseOffset))
				{
					matchSince ??= DateTime.UtcNow;
					if (DateTime.UtcNow - matchSince.Value >= TimeSpan.FromMilliseconds(150))
					{
						string name = $"{fixtures[next].Name}-{transport}";
						File.WriteAllText(Path.Combine(fixturesDir, name + ".hex"), Convert.ToHexString(report).Chunk(2).Select(c => new string(c)).Aggregate((a, b) => a + " " + b));
						Console.WriteLine($"{DateTime.Now:HH:mm:ss} captured {name} (battery byte 0x{report[baseOffset + 52]:X2})");
						next++;
						matchSince = null;
					}
				}
				else
				{
					matchSince = null;
				}
			}
			Console.WriteLine($"{transport}: {next}/{fixtures.Length} captured");
		});
		thread.Start();
		threads.Add(thread);
	}
	threads.ForEach(t => t.Join());
	return 0;
}

static bool Idle(byte[] r, int b)
{
	bool sticksCentred = Enumerable.Range(0, 4).All(i => Math.Abs(r[b + i] - 128) <= 20);
	return sticksCentred && r[b + 4] < 10 && r[b + 5] < 10 && r[b + 7] == 0x08 && r[b + 8] == 0 && (r[b + 9] & 0x07) == 0 && (r[b + 32] & 0x80) != 0;
}

// Feasibility probe (throwaway): streams a tone to the haptic motors over Bluetooth with output report 0x32, 2 s left
// then 2 s right. Format facts from SAxense (Egor Vorontsov, MPL-2.0): 142 bytes; byte 1 sequence nibble; block 0x11
// (header 0x91 0x07, then FE 00 00 00 00 FF and a counter); block 0x12 (header 0x92 0x40, 64 bytes = 32 frames of
// signed 8-bit stereo at 3 kHz); CRC-32 over 0xA2 + bytes 0-137 in the last 4 bytes; one report per 10.67 ms.
int HapticsTone(double hz, bool raw, bool setup)
{
	HidDevice? device = DeviceList.Local.GetHidDevices(SonyVendorId)
		.FirstOrDefault(d => productIds.Contains(d.ProductID) && DualPulse.Core.Devices.ControllerDiscovery.IsBluetooth(d.DevicePath));
	if (device == null)
	{
		Console.Error.WriteLine("No DualSense on Bluetooth.");
		return 1;
	}
	int maxLength = device.GetMaxOutputReportLength();
	Console.WriteLine($"Bluetooth device, max output report length {maxLength}; writing {(raw ? "142 bytes" : $"{maxLength} bytes")}, setup report {(setup ? "on" : "off")}, tone {hz} Hz");
	using HidStream stream = device.Open();
	stream.WriteTimeout = 1000;
	if (setup)
	{
		byte[] control = new byte[Math.Max(maxLength, DualPulse.Core.Protocol.OutputReportBuilder.BluetoothLength)];
		int controlLength = DualPulse.Core.Protocol.OutputReportBuilder.Build(DualPulse.Core.Protocol.OutputState.Default, DualPulse.Core.Protocol.Transport.Bluetooth, 0,
			DualPulse.Core.Protocol.BluetoothLayout.Sequenced, control);
		// Haptics need the actuators in audio mode: no rumble bits in valid_flag0 (haptics select / compatible vibration)
		// or valid_flag2's compatible-vibration bit; valid_flag1 enables power-save control, the haptics low-pass filter
		// and motor power reduction, with power save all on and no power reduction. Common block starts at byte 3.
		control[3 + 0] = 0x0C;
		control[3 + 1] = 0x02 | 0x04 | 0x10 | 0x20 | 0x40;
		control[3 + 9] = 0x00;
		control[3 + 36] = 0x00;
		control[3 + 38] = 0x01 | 0x02;
		byte[] controlCrc = new byte[75];
		controlCrc[0] = 0xA2;
		Array.Copy(control, 0, controlCrc, 1, 74);
		System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(control.AsSpan(74), System.IO.Hashing.Crc32.HashToUInt32(controlCrc));
		stream.Write(control, 0, Math.Max(controlLength, maxLength));
		Console.WriteLine("haptics setup report sent");
	}
	const int frames = 32;
	const double sampleRate = 3000;
	TimeSpan interval = TimeSpan.FromTicks((long)(TimeSpan.TicksPerSecond * frames / sampleRate));
	byte[] report = new byte[raw ? 142 : Math.Max(maxLength, 142)];
	System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
	TimeBeginPeriod(1);
	try
	{
		long sampleIndex = 0;
		int errors = 0;
		int sent = 0;
		TimeSpan next = TimeSpan.Zero;
		for (int i = 0; i < (int)(4 / interval.TotalSeconds); i++)
		{
			bool left = i < (int)(2 / interval.TotalSeconds);
			Array.Clear(report);
			report[0] = 0x32;
			report[1] = (byte)((i & 0x0F) << 4);
			report[2] = 0x91;
			report[3] = 0x07;
			report[4] = 0xFE;
			report[9] = 0xFF;
			report[10] = (byte)i;
			report[11] = 0x92;
			report[12] = 0x40;
			for (int f = 0; f < frames; f++, sampleIndex++)
			{
				// Signed 8-bit samples: 0 is silence.
				byte value = (byte)(sbyte)Math.Round(110 * Math.Sin(2 * Math.PI * hz * sampleIndex / sampleRate));
				report[13 + 2 * f] = left ? value : (byte)0;
				report[14 + 2 * f] = left ? (byte)0 : value;
			}
			byte[] crcInput = new byte[139];
			crcInput[0] = 0xA2;
			Array.Copy(report, 0, crcInput, 1, 138);
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(138), System.IO.Hashing.Crc32.HashToUInt32(crcInput));
			while (clock.Elapsed < next)
			{
				Thread.Sleep(1);
			}
			next += interval;
			try
			{
				stream.Write(report, 0, report.Length);
				sent++;
			}
			catch (Exception error)
			{
				if (errors++ == 0)
				{
					Console.WriteLine($"write failed: {error.Message}");
				}
			}
		}
		Console.WriteLine($"sent {sent} reports in {clock.Elapsed.TotalSeconds:0.00} s, {errors} failed");
		return errors == 0 ? 0 : 1;
	}
	finally
	{
		TimeEndPeriod(1);
	}
}

// Feasibility probe (throwaway): plays a 440 Hz tone for 3 s on the controller speaker (or the headset jack) over
// Bluetooth with output report 0x39 (547 bytes, one per 21.33 ms). Layout from the ds5bridge protocol notes (building
// on DS5Dongle, MPL-2.0, facts only): byte 1 sequence nibble; block 0x91 (6 bytes: a flag mask 0x7E whose set bits
// below bit 6 announce the bytes that follow (bit 0 = microphone), four buffer-length bytes (16-128), and a counter
// that rises by 2, one per audio frame); block 0xD2
// (64, doubled: 2 x 64 bytes of haptics); block 0xD3 speaker / 0xD6 headset (200, doubled: 2 x 200-byte Opus frames,
// 48 kHz stereo, 10 ms, CBR 160 kbit/s; 512 samples squeezed into each 480-sample frame); CRC-32 over 0xA2 + bytes
// 0-542 in the last 4 bytes. Before streaming, report 0x31 enables audio control and the volume and selects the path.
int SpeakerTone(bool headset, byte flags, byte bufferLength)
{
	HidDevice? device = DeviceList.Local.GetHidDevices(SonyVendorId)
		.FirstOrDefault(d => productIds.Contains(d.ProductID) && DualPulse.Core.Devices.ControllerDiscovery.IsBluetooth(d.DevicePath));
	if (device == null)
	{
		Console.Error.WriteLine("No DualSense on Bluetooth.");
		return 1;
	}
	int maxLength = device.GetMaxOutputReportLength();
	Console.WriteLine($"Bluetooth device, max output report length {maxLength}; {(headset ? "headset" : "speaker")}, flags 0x{flags:X2}, buffer length {bufferLength}");
	using HidStream stream = device.Open();
	stream.WriteTimeout = 1000;

	// Report 0x31: haptics-mode bits (as the haptics probe), plus audio control and the volume for the chosen path.
	byte[] control = new byte[Math.Max(maxLength, DualPulse.Core.Protocol.OutputReportBuilder.BluetoothLength)];
	int controlLength = DualPulse.Core.Protocol.OutputReportBuilder.Build(DualPulse.Core.Protocol.OutputState.Default with { HapticsMode = true },
		DualPulse.Core.Protocol.Transport.Bluetooth, 0, DualPulse.Core.Protocol.BluetoothLayout.Sequenced, control);
	control[3 + 0] = (byte)(0x0C | 0x80 | (headset ? 0x10 : 0x20));
	control[3 + 4] = headset ? (byte)0x30 : (byte)0;
	control[3 + 5] = headset ? (byte)0 : (byte)0x64;
	control[3 + 7] = headset ? (byte)0x00 : (byte)0x30;
	byte[] controlCrc = new byte[75];
	controlCrc[0] = 0xA2;
	Array.Copy(control, 0, controlCrc, 1, 74);
	System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(control.AsSpan(74), System.IO.Hashing.Crc32.HashToUInt32(controlCrc));
	stream.Write(control, 0, Math.Max(controlLength, maxLength));
	Console.WriteLine("audio setup report sent");

	Concentus.IOpusEncoder encoder = Concentus.OpusCodecFactory.CreateEncoder(48000, 2, Concentus.Enums.OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
	encoder.Bitrate = 160000;
	encoder.UseVBR = false;
	const int reportLength = 547;
	TimeSpan interval = TimeSpan.FromTicks((long)(TimeSpan.TicksPerSecond * 1024 / 48000.0));
	byte[] report = new byte[Math.Max(maxLength, reportLength)];
	short[] frame = new short[480 * 2];
	byte[] packet = new byte[400];
	long toneIndex = 0;
	int shortFrames = 0;
	int errors = 0;
	int sent = 0;
	System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
	TimeBeginPeriod(1);
	try
	{
		TimeSpan next = TimeSpan.Zero;
		int total = 3 + (int)(3 / interval.TotalSeconds);
		for (int i = 0; i < total; i++)
		{
			bool silent = i < 3;
			Array.Clear(report);
			report[0] = 0x39;
			report[1] = (byte)((i & 0x0F) << 4);
			report[2] = 0x91;
			report[3] = 0x06;
			report[4] = flags;
			report[5] = bufferLength;
			report[6] = bufferLength;
			report[7] = bufferLength;
			report[8] = bufferLength;
			report[9] = (byte)(2 * i);
			report[10] = 0xD2;
			report[11] = 64;
			report[140] = headset ? (byte)0xD6 : (byte)0xD3;
			report[141] = 200;
			for (int half = 0; half < 2; half++)
			{
				// 512 tone samples squeezed into 480 (linear interpolation), as the controller plays them.
				for (int f = 0; f < 480; f++)
				{
					double source = toneIndex + f * 512.0 / 480;
					short value = silent ? (short)0 : (short)(0.3 * short.MaxValue * Math.Sin(2 * Math.PI * 440 * source / 48000));
					frame[2 * f] = value;
					frame[2 * f + 1] = value;
				}
				toneIndex += 512;
				Array.Clear(packet);
				int encoded = encoder.Encode(frame, 480, packet.AsSpan(0, 200), 200);
				if (encoded < 200)
				{
					shortFrames++;
				}
				Array.Copy(packet, 0, report, 142 + 200 * half, 200);
			}
			byte[] crcInput = new byte[544];
			crcInput[0] = 0xA2;
			Array.Copy(report, 0, crcInput, 1, 543);
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(543), System.IO.Hashing.Crc32.HashToUInt32(crcInput));
			while (clock.Elapsed < next)
			{
				Thread.Sleep(1);
			}
			next += interval;
			try
			{
				stream.Write(report, 0, report.Length);
				sent++;
			}
			catch (Exception error)
			{
				if (errors++ == 0)
				{
					Console.WriteLine($"write failed: {error.Message}");
				}
			}
		}
		Console.WriteLine($"sent {sent} reports in {clock.Elapsed.TotalSeconds:0.00} s, {errors} failed, {shortFrames} Opus frames under 200 bytes");
		return errors == 0 ? 0 : 1;
	}
	finally
	{
		TimeEndPeriod(1);
	}
}

[System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
static extern uint TimeBeginPeriod(uint period);

[System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
static extern uint TimeEndPeriod(uint period);

static List<(string Id, string Name)> RenderEndpoints()
{
	using NAudio.CoreAudioApi.MMDeviceEnumerator enumerator = new();
	return [.. enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active).Select(d => (d.ID, d.FriendlyName))];
}

// A 440 Hz tone on all four channels (front pair = speaker, rear pair = haptics) of one render endpoint.
static void PlayQuadTone(string endpointId, TimeSpan duration, double hz = 440, double amplitude = 0.3)
{
	using NAudio.CoreAudioApi.MMDeviceEnumerator enumerator = new();
	NAudio.CoreAudioApi.MMDevice device = enumerator.GetDevice(endpointId);
	NAudio.Wave.WaveFormat format = device.AudioClient.MixFormat;
	Console.WriteLine($"endpoint mix format: {format}, volume {device.AudioEndpointVolume.MasterVolumeLevelScalar:0.00}, muted {device.AudioEndpointVolume.Mute}");
	using NAudio.Wave.WasapiOut output = new(device, NAudio.CoreAudioApi.AudioClientShareMode.Shared, false, 40);
	output.Init(new QuadTone(format, hz, amplitude));
	output.Play();
	Thread.Sleep(duration);
	output.Stop();
}

static string? SafeSerial(HidDevice device)
{
	try
	{
		return device.GetSerialNumber();
	}
	catch (Exception)
	{
		return null;
	}
}

// A 440 Hz tone on every channel as 32-bit float bytes in the endpoint's own (extensible) mix format, which shared-mode
// WASAPI requires for more than two channels.
internal sealed class QuadTone(NAudio.Wave.WaveFormat format, double hz = 440, double amplitude = 0.3) : NAudio.Wave.IWaveProvider
{
	private long index;

	public NAudio.Wave.WaveFormat WaveFormat { get; } = format;

	public int Read(byte[] buffer, int offset, int count)
	{
		int channels = WaveFormat.Channels;
		int frames = count / (4 * channels);
		for (int f = 0; f < frames; f++)
		{
			float value = (float)(amplitude * Math.Sin(2 * Math.PI * hz * index++ / WaveFormat.SampleRate));
			for (int c = 0; c < channels; c++)
			{
				BitConverter.TryWriteBytes(buffer.AsSpan(offset + (f * channels + c) * 4), value);
			}
		}
		return frames * 4 * channels;
	}
}

internal static class UsbDescriptors
{
	private const uint DigcfPresent = 0x02;

	private const uint DigcfDeviceInterface = 0x10;

	private const uint IoctlGetNodeInformation = 0x220408;

	private const uint IoctlGetNodeConnectionInformationEx = 0x220448;

	private const uint IoctlGetDescriptorFromNodeConnection = 0x220410;

	private static readonly Guid UsbHubInterface = new("f18a0e88-c30c-11d0-8815-00a0c906bed8");

	public static int Dump(string folder)
	{
		Directory.CreateDirectory(folder);
		int found = 0;
		foreach (string hub in HubPaths())
		{
			using Microsoft.Win32.SafeHandles.SafeFileHandle handle = CreateFileW(hub, 0x40000000, 0x02, 0, 3, 0, 0);
			if (handle.IsInvalid)
			{
				continue;
			}
			byte[] node = new byte[76];
			if (!DeviceIoControl(handle, IoctlGetNodeInformation, node, node.Length, node, node.Length, out _, 0))
			{
				continue;
			}
			int ports = node[4 + 2];
			for (int port = 1; port <= ports; port++)
			{
				byte[] info = new byte[512];
				BitConverter.GetBytes(port).CopyTo(info, 0);
				if (!DeviceIoControl(handle, IoctlGetNodeConnectionInformationEx, info, info.Length, info, info.Length, out _, 0))
				{
					continue;
				}
				int vid = BitConverter.ToUInt16(info, 4 + 8);
				int pid = BitConverter.ToUInt16(info, 4 + 10);
				if (vid != 0x054C || pid is not (0x0CE6 or 0x0DF2))
				{
					continue;
				}
				byte[] device = info[4..22];
				int speed = info[23];
				byte[]? config = Descriptor(handle, port, 0x80, 2, 0, 0, 9) is { } head ? Descriptor(handle, port, 0x80, 2, 0, 0, BitConverter.ToUInt16(head, 2)) : null;
				string serial = Descriptor(handle, port, 0x80, 3, device[16], 0x0409, 255) is { } s3 && device[16] != 0 ? System.Text.Encoding.Unicode.GetString(s3, 2, s3[0] - 2) : "";
				// A virtual pad (VIIPER, DualPulse) reports a serial string; the real one checked here has none.
				Console.WriteLine($"hub {hub}\n  port {port}: {vid:X4}:{pid:X4} speed {speed} serial '{serial}'");
				Console.WriteLine($"  device  {Convert.ToHexString(device)}");
				Console.WriteLine($"  config  {(config == null ? "-" : Convert.ToHexString(config))}");
				for (int index = 1; index <= 3; index++)
				{
					byte[]? text = Descriptor(handle, port, 0x80, 3, index, 0x0409, 255);
					Console.WriteLine($"  string {index} {(text == null || text.Length < 2 ? "-" : System.Text.Encoding.Unicode.GetString(text, 2, text[0] - 2))}");
				}
				byte[]? report = Descriptor(handle, port, 0x81, 0x22, 0, 3, 1024);
				Console.WriteLine($"  hid report (interface 3) {(report == null ? "not readable through the hub" : $"{report.Length} bytes")}");
				string name = $"dualsense-{pid:X4}-hub{found}-port{port}";
				File.WriteAllText(Path.Combine(folder, name + ".txt"),
					$"speed {speed}\nserial {serial}\ndevice {Convert.ToHexString(device)}\nconfig {(config == null ? "" : Convert.ToHexString(config))}\nreport {(report == null ? "" : Convert.ToHexString(report))}\n");
				found++;
			}
		}
		Console.WriteLine($"{found} DualSense(s) found");
		return found > 0 ? 0 : 1;
	}

	private static byte[]? Descriptor(Microsoft.Win32.SafeHandles.SafeFileHandle hub, int port, byte requestType, int type, int index, int wIndex, int length)
	{
		byte[] buffer = new byte[12 + length];
		BitConverter.GetBytes(port).CopyTo(buffer, 0);
		buffer[4] = requestType;
		buffer[5] = 6;
		BitConverter.GetBytes((ushort)((type << 8) | index)).CopyTo(buffer, 6);
		BitConverter.GetBytes((ushort)wIndex).CopyTo(buffer, 8);
		BitConverter.GetBytes((ushort)length).CopyTo(buffer, 10);
		if (!DeviceIoControl(hub, IoctlGetDescriptorFromNodeConnection, buffer, buffer.Length, buffer, buffer.Length, out int returned, 0) || returned <= 12)
		{
			return null;
		}
		return buffer[12..returned];
	}

	private static IEnumerable<string> HubPaths()
	{
		Guid guid = UsbHubInterface;
		nint set = SetupDiGetClassDevsW(ref guid, 0, 0, DigcfPresent | DigcfDeviceInterface);
		try
		{
			for (uint i = 0; ; i++)
			{
				byte[] data = new byte[32];
				BitConverter.GetBytes(32).CopyTo(data, 0);
				if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, data))
				{
					yield break;
				}
				byte[] detail = new byte[1024];
				BitConverter.GetBytes(8).CopyTo(detail, 0);
				if (SetupDiGetDeviceInterfaceDetailW(set, data, detail, detail.Length, out _, 0))
				{
					yield return System.Text.Encoding.Unicode.GetString(detail, 4, detail.Length - 4).Split('\0')[0];
				}
			}
		}
		finally
		{
			SetupDiDestroyDeviceInfoList(set);
		}
	}

	[System.Runtime.InteropServices.DllImport("setupapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
	private static extern nint SetupDiGetClassDevsW(ref Guid guid, nint enumerator, nint parent, uint flags);

	[System.Runtime.InteropServices.DllImport("setupapi.dll", SetLastError = true)]
	private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint info, ref Guid guid, uint index, byte[] data);

	[System.Runtime.InteropServices.DllImport("setupapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
	private static extern bool SetupDiGetDeviceInterfaceDetailW(nint set, byte[] data, byte[] detail, int size, out int required, nint info);

	[System.Runtime.InteropServices.DllImport("setupapi.dll")]
	private static extern bool SetupDiDestroyDeviceInfoList(nint set);

	[System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
	private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

	[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device, uint code, byte[] input, int inputSize, byte[] output, int outputSize, out int returned, nint overlapped);
}

internal static class XInput
{
	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	public struct Vibration
	{
		public ushort Left;

		public ushort Right;
	}

	[System.Runtime.InteropServices.DllImport("xinput1_4.dll")]
	public static extern uint XInputGetState(uint user, out State state);

	[System.Runtime.InteropServices.DllImport("xinput1_4.dll")]
	public static extern uint XInputSetState(uint user, ref Vibration vibration);

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	public struct State
	{
		public uint PacketNumber;

		public ushort Buttons;

		public byte LeftTrigger;

		public byte RightTrigger;

		public short ThumbLX;

		public short ThumbLY;

		public short ThumbRX;

		public short ThumbRY;
	}
}
