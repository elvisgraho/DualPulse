using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using DualPulse.Core.Devices;
using DualPulse.Core.UsbIp;

namespace DualPulse.Core.Usb;

// Interleaved 16-bit PCM at 48 kHz from the host.
public delegate void PcmBlock(ReadOnlySpan<short> samples, int channels);

// A USB device served over USB/IP. Submit may complete at once or later (on another thread); Cancel withdraws a
// pending transfer (an unlink) and returns whether it was still pending, in which case it never completes.
public interface IUsbIpDevice
{
	UsbIpExport Export { get; }

	void Submit(UrbSubmit urb, Action<UrbResult> complete);

	bool Cancel(uint seqnum);

	// The host let go of the device (connection closed): pending transfers are dropped.
	void Detached();
}

// A USB DualSense made from the real controller's descriptors (DualSenseUsbDescriptors):
// - Control: standard descriptor, configuration and interface requests; HID get/set report (feature reports from the
//   captured defaults, input on request, output 0x02); audio class requests (sample rate, volume, mute) stored and
//   answered.
// - Interrupt IN 0x84: input reports (SetInput) complete the host's pending transfers.
// - Interrupt OUT 0x03: the game's output report; its 47-byte block is raised as OutputBlock.
// - Isochronous OUT 0x01: 4-channel game audio, raised as Speaker (all four) and Haptics (channels 3-4).
// - Isochronous IN 0x82: microphone, 48 kHz stereo; each 1 ms packet carries 48 frames of what WriteMicrophone was
//   given, taken when the transfer completes (the host queues transfers ahead). After running dry it waits for 10 ms
//   of sound before playing again, so small timing differences don't crackle. MicrophoneOpened tells when the host starts and stops recording
//   (its interface choice), so the real microphone is only used while something records.
// Isochronous transfers run on a 1 ms frame clock, like a real bus: each starts where the endpoint's previous one ended
// (or at the current frame after a gap) and completes once its packets' frames have passed, so the host streams at the
// real rate. Anything else stalls.
public sealed class VirtualDualSense : IUsbIpDevice
{
	private const int InputLength = 64;

	private const int OutputBlockLength = 47;

	private readonly object gate = new();

	private readonly Queue<(uint Seqnum, Action<UrbResult> Complete)> pendingInput = new();

	// Isochronous transfers waiting for their frames, and where each endpoint's schedule has reached.
	private readonly Dictionary<uint, ITimer> pendingIsochronous = [];

	private readonly Dictionary<int, long> nextFrame = [];

	private readonly TimeProvider time;

	private readonly long origin;

	private bool fineTimers;

	private readonly Dictionary<int, byte> alternateSettings = [];

	private readonly Dictionary<(int Request, int Value, int Index), byte[]> audioControls = [];

	private byte[] latestInput = NeutralInput();

	private bool freshInput;

	public VirtualDualSense(string busId, TimeProvider? time = null)
	{
		this.time = time ?? TimeProvider.System;
		origin = this.time.GetTimestamp();
		string[] parts = busId.Split('-');
		uint busNumber = parts.Length > 0 && uint.TryParse(parts[0], out uint b) ? b : 1;
		uint deviceNumber = parts.Length > 1 && uint.TryParse(parts[1], out uint d) ? d : 1;
		ReadOnlySpan<byte> device = DualSenseUsbDescriptors.Device;
		Export = new UsbIpExport($"/dualpulse/{busId}", busId, busNumber, deviceNumber, UsbIpSpeed.Full, DualSenseUsbDescriptors.VendorId,
			DualSenseUsbDescriptors.ProductId, BinaryPrimitives.ReadUInt16LittleEndian(device[12..]), device[4], device[5], device[6], 1, device[17],
			Interfaces());
	}

	public UsbIpExport Export { get; }

	public event Action<byte[]>? OutputBlock;

	public event PcmBlock? Speaker;

	public event PcmBlock? Haptics;

	// True when the host starts recording from the microphone, false when it stops. Raised on the USB/IP thread.
	public event Action<bool>? MicrophoneOpened;

	private const int MicrophoneFramesPerPacket = 48;

	// At most 60 ms of microphone sound waits; older sound is dropped so the delay stays short.
	private readonly short[] microphone = new short[2 * 48 * 60];

	private int microphoneStart;

	private int microphoneCount;

	// False after running dry, until 10 ms of sound is waiting.
	private bool microphonePrimed;

	private const int MicrophonePrimeSamples = 2 * 480;

	// Interleaved 16-bit stereo at 48 kHz from the real microphone; called from the capture thread.
	// Diagnostics: frames sent as silence because the buffer ran short, frames dropped because it was full, and how
	// many frames wait now.
	public (long ShortFrames, long DroppedFrames, int BufferedFrames) MicrophoneStats
	{
		get
		{
			lock (microphone)
			{
				return (microphoneShort, microphoneDropped, microphoneCount / 2);
			}
		}
	}

	private long microphoneShort;

	private long microphoneDropped;

	public void WriteMicrophone(ReadOnlySpan<short> stereo)
	{
		lock (microphone)
		{
			foreach (short sample in stereo)
			{
				if (microphoneCount == microphone.Length)
				{
					microphoneStart = (microphoneStart + 2) % microphone.Length;
					microphoneCount -= 2;
					microphoneDropped++;
				}
				microphone[(microphoneStart + microphoneCount) % microphone.Length] = sample;
				microphoneCount++;
			}
		}
	}

	// One transfer's worth: 48 frames per packet (fewer if the host leaves less room), packed, with their lengths.
	private UrbResult MicrophoneTransfer(UrbSubmit urb)
	{
		int[] lengths = [.. urb.IsoPackets.Select(p => Math.Min(MicrophoneFramesPerPacket, p.Length / 4) * 4)];
		byte[] data = new byte[lengths.Sum()];
		Span<short> samples = MemoryMarshal.Cast<byte, short>(data.AsSpan());
		lock (microphone)
		{
			if (!microphonePrimed && microphoneCount >= MicrophonePrimeSamples)
			{
				microphonePrimed = true;
			}
			int take = microphonePrimed ? Math.Min(microphoneCount, samples.Length) : 0;
			if (microphonePrimed && take < samples.Length)
			{
				microphonePrimed = false;
			}
			for (int i = 0; i < take; i++)
			{
				samples[i] = microphone[(microphoneStart + i) % microphone.Length];
			}
			microphoneStart = (microphoneStart + take) % microphone.Length;
			microphoneCount -= take;
			microphoneShort += (samples.Length - take) / 2;
		}
		return UrbResult.Ok(data) with { IsoActualLengths = lengths };
	}

	// Diagnostics: each control request with its answer, and the first transfer on each other endpoint.
	public Action<string>? Trace { get; set; }

	private readonly HashSet<(int, bool)> tracedEndpoints = [];

	// The newest 64-byte USB input report (0x01); completes the oldest pending interrupt transfer, if any.
	public void SetInput(ReadOnlySpan<byte> report)
	{
		byte[] copy = report[..InputLength].ToArray();
		Action<UrbResult>? complete = null;
		lock (gate)
		{
			latestInput = copy;
			if (pendingInput.Count > 0)
			{
				complete = pendingInput.Dequeue().Complete;
			}
			else
			{
				freshInput = true;
			}
		}
		complete?.Invoke(UrbResult.Ok(copy));
	}

	public void Submit(UrbSubmit urb, Action<UrbResult> complete)
	{
		if (Trace != null && urb.Endpoint != 0)
		{
			bool first;
			lock (gate)
			{
				first = tracedEndpoints.Add((urb.Endpoint, urb.In));
			}
			if (first)
			{
				Trace($"first transfer on endpoint {urb.Endpoint} {(urb.In ? "IN" : "OUT")}: length {urb.Length}, packets {urb.IsoPackets.Length}, data {urb.Data.Length}");
			}
		}
		switch (urb.Endpoint, urb.In)
		{
		case (0, _):
			UrbResult control = Control(urb);
			Trace?.Invoke($"control {Convert.ToHexString(urb.Setup)} data {Convert.ToHexString(urb.Data)} -> {(control.Status == 0 ? Convert.ToHexString(control.Data.Length > 24 ? control.Data[..24] : control.Data) + (control.Data.Length > 24 ? $"... ({control.Data.Length} bytes)" : "") : "STALL")}");
			complete(control);
			return;
		case (DualSenseUsbDescriptors.InputEndpoint & 0x0F, true):
			SubmitInput(urb, complete);
			return;
		case (DualSenseUsbDescriptors.OutputEndpoint, false):
			RaiseOutput(urb.Data);
			complete(UrbResult.Ok([]));
			return;
		case (DualSenseUsbDescriptors.SpeakerEndpoint, false):
			RaiseAudio(urb);
			SubmitIsochronous(urb, complete, static () => UrbResult.Ok([]));
			return;
		case (DualSenseUsbDescriptors.MicrophoneEndpoint & 0x0F, true):
			SubmitIsochronous(urb, complete, () => MicrophoneTransfer(urb));
			return;
		default:
			complete(UrbResult.Stall);
			return;
		}
	}

	public bool Cancel(uint seqnum)
	{
		lock (gate)
		{
			if (pendingIsochronous.Remove(seqnum, out ITimer? timer))
			{
				timer.Dispose();
				return true;
			}
			int before = pendingInput.Count;
			(uint, Action<UrbResult>)[] kept = [.. pendingInput.Where(p => p.Seqnum != seqnum)];
			if (kept.Length == before)
			{
				return false;
			}
			pendingInput.Clear();
			foreach ((uint, Action<UrbResult>) item in kept)
			{
				pendingInput.Enqueue(item);
			}
			return true;
		}
	}

	public void Detached()
	{
		lock (gate)
		{
			pendingInput.Clear();
			foreach (ITimer timer in pendingIsochronous.Values)
			{
				timer.Dispose();
			}
			pendingIsochronous.Clear();
			nextFrame.Clear();
			if (fineTimers)
			{
				fineTimers = false;
				TimerResolution.End();
			}
		}
	}

	private void SubmitInput(UrbSubmit urb, Action<UrbResult> complete)
	{
		byte[]? now = null;
		lock (gate)
		{
			if (freshInput)
			{
				freshInput = false;
				now = latestInput;
			}
			else
			{
				pendingInput.Enqueue((urb.Seqnum, complete));
			}
		}
		if (now != null)
		{
			complete(UrbResult.Ok(now));
		}
	}

	// result: built when the transfer completes.
	private void SubmitIsochronous(UrbSubmit urb, Action<UrbResult> complete, Func<UrbResult> result)
	{
		lock (gate)
		{
			if (!fineTimers)
			{
				// Default Windows timers tick every 15.6 ms, coarser than a 10 ms audio transfer.
				fineTimers = true;
				TimerResolution.Begin();
			}
			TimeSpan elapsed = time.GetElapsedTime(origin);
			long now = (long)elapsed.TotalMilliseconds;
			long start = Math.Max(now, nextFrame.GetValueOrDefault(urb.Endpoint, now));
			long end = start + Math.Max(1, urb.IsoPackets.Length);
			nextFrame[urb.Endpoint] = end;
			int startFrame = unchecked((int)start);
			pendingIsochronous[urb.Seqnum] = time.CreateTimer(_ => CompleteIsochronous(urb.Seqnum, complete, () => result() with { StartFrame = startFrame }), null,
				TimeSpan.FromMilliseconds(end) - elapsed, Timeout.InfiniteTimeSpan);
		}
	}

	private void CompleteIsochronous(uint seqnum, Action<UrbResult> complete, Func<UrbResult> result)
	{
		lock (gate)
		{
			if (!pendingIsochronous.Remove(seqnum, out ITimer? timer))
			{
				return;
			}
			timer.Dispose();
		}
		complete(result());
	}

	private void RaiseOutput(byte[] report)
	{
		if (report.Length >= 1 + OutputBlockLength && report[0] == 0x02)
		{
			OutputBlock?.Invoke(report[1..(1 + OutputBlockLength)]);
		}
	}

	private void RaiseAudio(UrbSubmit urb)
	{
		int length = urb.IsoPackets.Length > 0 ? Math.Min(urb.Data.Length, urb.IsoPackets.Sum(p => p.Length)) : urb.Data.Length;
		ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(urb.Data.AsSpan(0, length & ~7));
		if (samples.Length == 0)
		{
			return;
		}
		Speaker?.Invoke(samples, 4);
		if (Haptics != null)
		{
			short[] rear = new short[samples.Length / 2];
			for (int frame = 0; frame < samples.Length / 4; frame++)
			{
				rear[2 * frame] = samples[4 * frame + 2];
				rear[2 * frame + 1] = samples[4 * frame + 3];
			}
			Haptics(rear, 2);
		}
	}

	private UrbResult Control(UrbSubmit urb)
	{
		byte requestType = urb.Setup[0];
		byte request = urb.Setup[1];
		ushort value = BinaryPrimitives.ReadUInt16LittleEndian(urb.Setup.AsSpan(2));
		ushort index = BinaryPrimitives.ReadUInt16LittleEndian(urb.Setup.AsSpan(4));
		ushort length = BinaryPrimitives.ReadUInt16LittleEndian(urb.Setup.AsSpan(6));
		byte[]? answer = (requestType, request) switch
		{
			(0x80, 6) => DeviceDescriptor(value),
			(0x81, 6) => InterfaceDescriptor(value, index),
			(0x80, 0) or (0x81, 0) or (0x82, 0) => [0, 0],
			(0x80, 8) => [1],
			(0x81, 10) => [Alternate(index)],
			(0x00, 9) or (0x00, 1) or (0x01, 1) or (0x02, 1) or (0x00, 3) or (0x01, 3) or (0x02, 3) => [],
			(0x01, 11) => SetAlternate(index, value),
			(0xA1, 1) => GetReport(value),
			(0x21, 9) => SetReport(value, urb.Data),
			(0x21, 0x0A) => [],
			(0x21, _) or (0x22, _) when request is >= 1 and <= 4 => SetAudio(request, value, index, urb.Data),
			(0xA1, _) or (0xA2, _) when request is >= 0x81 and <= 0x84 => GetAudio(request, value, index),
			_ => null
		};
		if (answer == null)
		{
			return UrbResult.Stall;
		}
		return UrbResult.Ok(answer.Length > length ? answer[..length] : answer);
	}

	private static byte[]? DeviceDescriptor(ushort value)
	{
		int type = value >> 8;
		int index = value & 0xFF;
		return type switch
		{
			1 => DualSenseUsbDescriptors.Device.ToArray(),
			2 => DualSenseUsbDescriptors.Configuration.ToArray(),
			3 => index switch
			{
				0 => [4, 3, 0x09, 0x04],
				1 => StringDescriptor(DualSenseUsbDescriptors.Manufacturer),
				2 => StringDescriptor(DualSenseUsbDescriptors.Product),
				3 => StringDescriptor(ControllerDiscovery.VirtualSerialNumber),
				_ => null
			},
			_ => null
		};
	}

	private static byte[]? InterfaceDescriptor(ushort value, ushort index)
	{
		if (index != DualSenseUsbDescriptors.HidInterface)
		{
			return null;
		}
		return (value >> 8) switch
		{
			0x22 => DualSenseUsbDescriptors.HidReport.ToArray(),
			0x21 => HidDescriptor(),
			_ => null
		};
	}

	// The 9-byte HID class descriptor inside the configuration descriptor.
	private static byte[]? HidDescriptor()
	{
		ReadOnlySpan<byte> config = DualSenseUsbDescriptors.Configuration;
		for (int i = 0; i + 1 < config.Length && config[i] > 0; i += config[i])
		{
			if (config[i + 1] == 0x21)
			{
				return config.Slice(i, config[i]).ToArray();
			}
		}
		return null;
	}

	private static byte[] StringDescriptor(string text)
	{
		byte[] body = Encoding.Unicode.GetBytes(text);
		return [(byte)(body.Length + 2), 3, .. body];
	}

	private byte Alternate(ushort index)
	{
		lock (gate)
		{
			return alternateSettings.TryGetValue(index, out byte alternate) ? alternate : (byte)0;
		}
	}

	private byte[] SetAlternate(ushort index, ushort value)
	{
		bool changed;
		lock (gate)
		{
			changed = Alternate(index) != (byte)value;
			alternateSettings[index] = (byte)value;
		}
		if (changed && index == DualSenseUsbDescriptors.MicrophoneInterface)
		{
			if (value == 0)
			{
				lock (microphone)
				{
					microphoneCount = 0;
					microphonePrimed = false;
				}
			}
			MicrophoneOpened?.Invoke(value != 0);
		}
		return [];
	}

	private byte[] GetReport(ushort value)
	{
		int type = value >> 8;
		byte id = (byte)value;
		if (type == 1)
		{
			lock (gate)
			{
				return latestInput;
			}
		}
		return id switch
		{
			0x05 => DualSenseUsbDescriptors.FeatureCalibration.ToArray(),
			0x09 => DualSenseUsbDescriptors.FeaturePairing.ToArray(),
			0x20 => DualSenseUsbDescriptors.FeatureFirmware.ToArray(),
			0x22 => DualSenseUsbDescriptors.FeatureHardware.ToArray(),
			// Other feature reports answer with their id and zeros (the host truncates to the declared size).
			_ => [id, .. new byte[63]]
		};
	}

	private byte[] SetReport(ushort value, byte[] data)
	{
		if (value >> 8 == 2)
		{
			RaiseOutput(data);
		}
		return [];
	}

	private byte[] SetAudio(byte request, ushort value, ushort index, byte[] data)
	{
		lock (gate)
		{
			audioControls[(request, value, index)] = data;
		}
		return [];
	}

	private byte[] GetAudio(byte request, ushort value, ushort index)
	{
		lock (gate)
		{
			if (audioControls.TryGetValue((request - 0x80, value, index), out byte[]? stored))
			{
				return stored;
			}
		}
		int control = value >> 8;
		// Endpoint sampling frequency: 48 kHz, 3 bytes.
		if (control == 1 && (index & 0x7F) is DualSenseUsbDescriptors.SpeakerEndpoint or (DualSenseUsbDescriptors.MicrophoneEndpoint & 0x7F))
		{
			return [0x80, 0xBB, 0x00];
		}
		// Feature unit: mute off; volume range -60..0 dB in 1 dB steps, at 0 dB.
		return (control, request) switch
		{
			(1, _) => [0],
			(2, 0x82) => [0x00, 0xC4],
			(2, 0x84) => [0x00, 0x01],
			(2, _) => [0x00, 0x00],
			_ => [0, 0]
		};
	}

	private static IReadOnlyList<(byte, byte, byte)> Interfaces()
	{
		List<(byte, byte, byte)> interfaces = [];
		ReadOnlySpan<byte> config = DualSenseUsbDescriptors.Configuration;
		for (int i = 0; i + 8 < config.Length && config[i] > 0; i += config[i])
		{
			// Interface descriptors with alternate setting 0.
			if (config[i + 1] == 4 && config[i + 3] == 0)
			{
				interfaces.Add((config[i + 5], config[i + 6], config[i + 7]));
			}
		}
		return interfaces;
	}

	private static byte[] NeutralInput()
	{
		byte[] report = new byte[InputLength];
		report[0] = 0x01;
		report[1] = report[2] = report[3] = report[4] = 0x80;
		report[8] = 0x08;
		return report;
	}
}
