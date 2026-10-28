using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using DualPulse.Core.Audio;

namespace DualPulse.Emulation;

// The real DualSense's own speaker device on Windows (USB): the active render endpoint named DualSense whose USB
// audio interface does not belong to DualPulse's virtual pad (the parent USB device's instance id carries the virtual
// serial). Played through shared-mode WASAPI with little latency; samples arriving faster than the device plays are
// dropped beyond a short buffer, so haptics never lag behind the game. Its microphone is the capture endpoint found
// the same way, recorded through shared-mode WASAPI, event-driven with a 10 ms buffer (even blocks for the waveform).
[SupportedOSPlatform("windows")]
public sealed partial class WindowsControllerUsbAudio : IControllerUsbAudio
{
	// The endpoint's audio interface: "{1}.USB\VID_054C&PID_0CE6&MI_00\...".
	private static readonly PropertyKey InterfaceInstanceKey = new(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);

	private const string VirtualSerialMark = "DUALPULSE-VIRTUAL";

	public IControllerAudioStream? Open()
	{
		try
		{
			using MMDeviceEnumerator enumerator = new();
			foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
			{
				if (device.FriendlyName.Contains("DualSense", StringComparison.OrdinalIgnoreCase) && !IsVirtual(device))
				{
					return new Stream(device);
				}
				device.Dispose();
			}
		}
		catch (COMException)
		{
		}
		return null;
	}

	public (string? Real, string? Virtual) MicrophoneNames()
	{
		string? real = null, virtualName = null;
		try
		{
			using MMDeviceEnumerator enumerator = new();
			foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
			{
				using (device)
				{
					if (!device.FriendlyName.Contains("DualSense", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					if (IsVirtual(device))
					{
						virtualName ??= device.FriendlyName;
					}
					else
					{
						real ??= device.FriendlyName;
					}
				}
			}
		}
		catch (COMException)
		{
		}
		return (real, virtualName);
	}

	public int? MicrophoneVolume()
	{
		using MMDevice? device = RealMicrophone();
		try
		{
			return device == null ? null : (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
		}
		catch (COMException)
		{
			return null;
		}
	}

	public bool SetMicrophoneVolume(int percent)
	{
		using MMDevice? device = RealMicrophone();
		try
		{
			if (device == null)
			{
				return false;
			}
			device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f;
			return true;
		}
		catch (COMException)
		{
			return false;
		}
	}

	private static MMDevice? RealMicrophone()
	{
		try
		{
			using MMDeviceEnumerator enumerator = new();
			foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
			{
				if (device.FriendlyName.Contains("DualSense", StringComparison.OrdinalIgnoreCase) && !IsVirtual(device))
				{
					return device;
				}
				device.Dispose();
			}
		}
		catch (COMException)
		{
		}
		return null;
	}

	public IDisposable? OpenMicrophone(MicrophoneBlock blocks)
	{
		MMDevice? device = RealMicrophone();
		return device == null ? null : new Microphone(device, blocks);
	}

	// Public for the probe: whether this endpoint belongs to DualPulse's virtual DualSense.
	public static bool IsVirtual(MMDevice device)
	{
		string? interfaceId = null;
		try
		{
			interfaceId = device.Properties[InterfaceInstanceKey]?.Value as string;
		}
		catch (COMException)
		{
		}
		if (interfaceId == null)
		{
			return false;
		}
		int dot = interfaceId.IndexOf('.', StringComparison.Ordinal);
		string instance = dot >= 0 && interfaceId.StartsWith('{') ? interfaceId[(dot + 1)..] : interfaceId;
		string? parent = ParentOf(instance);
		return parent != null && parent.Contains(VirtualSerialMark, StringComparison.OrdinalIgnoreCase);
	}

	private static string? ParentOf(string instanceId)
	{
		if (CM_Locate_DevNodeW(out uint node, instanceId, 0) != 0 || CM_Get_Parent(out uint parent, node, 0) != 0)
		{
			return null;
		}
		char[] buffer = new char[400];
		return CM_Get_Device_IDW(parent, buffer, buffer.Length, 0) == 0 ? new string(buffer).TrimEnd('\0') : null;
	}

	[LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int CM_Locate_DevNodeW(out uint node, string deviceId, uint flags);

	[LibraryImport("cfgmgr32.dll")]
	private static partial int CM_Get_Parent(out uint parent, uint node, uint flags);

	[LibraryImport("cfgmgr32.dll", StringMarshalling = StringMarshalling.Utf16)]
	private static partial int CM_Get_Device_IDW(uint node, [Out] char[] buffer, int length, uint flags);

	private sealed class Microphone : IDisposable
	{
		private readonly MMDevice device;

		private readonly WasapiCapture capture;

		private readonly MicrophoneConverter converter;

		private readonly MicrophoneBlock blocks;

		private float[] converted = [];

		public Microphone(MMDevice device, MicrophoneBlock blocks)
		{
			this.device = device;
			this.blocks = blocks;
			capture = new WasapiCapture(device, true, 10);
			converter = new MicrophoneConverter(capture.WaveFormat.SampleRate, capture.WaveFormat.Channels);
			capture.DataAvailable += OnData;
			capture.StartRecording();
		}

		private void OnData(object? sender, WaveInEventArgs e)
		{
			WaveFormat format = capture.WaveFormat;
			ReadOnlySpan<byte> bytes = e.Buffer.AsSpan(0, e.BytesRecorded);
			if (format.Encoding == WaveFormatEncoding.IeeeFloat || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
			{
				converter.Process(MemoryMarshal.Cast<byte, float>(bytes), blocks);
			}
			else if (format.BitsPerSample == 16)
			{
				ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(bytes);
				if (converted.Length < samples.Length)
				{
					converted = new float[samples.Length];
				}
				for (int i = 0; i < samples.Length; i++)
				{
					converted[i] = samples[i] / 32768f;
				}
				converter.Process(converted.AsSpan(0, samples.Length), blocks);
			}
		}

		public void Dispose()
		{
			capture.DataAvailable -= OnData;
			try
			{
				capture.StopRecording();
			}
			catch (Exception)
			{
			}
			capture.Dispose();
			device.Dispose();
		}
	}

	private sealed class Stream : IControllerAudioStream
	{
		// Queued audio beyond this is dropped, so a capture clock running ahead never builds up delay.
		private static readonly TimeSpan MaxBuffered = TimeSpan.FromMilliseconds(40);

		private readonly MMDevice device;

		private readonly WasapiOut output;

		private readonly BufferedWaveProvider buffer;

		private readonly int channels;

		private byte[] bytes = [];

		public Stream(MMDevice device)
		{
			this.device = device;
			WaveFormat format = device.AudioClient.MixFormat;
			channels = format.Channels;
			buffer = new BufferedWaveProvider(format) { DiscardOnBufferOverflow = true, BufferDuration = TimeSpan.FromMilliseconds(200), ReadFully = true };
			// 15 ms: 10 kept up in testing, the extra 5 is headroom for busier machines.
			output = new WasapiOut(device, AudioClientShareMode.Shared, true, 15);
			output.Init(buffer);
			output.Play();
		}

		// The mix format is 32-bit float with the device's channel count (4 for a DualSense).
		public void Write(ReadOnlySpan<short> samples, int sourceChannels)
		{
			int frames = samples.Length / sourceChannels;
			int needed = frames * channels * 4;
			if (bytes.Length < needed)
			{
				bytes = new byte[needed];
			}
			for (int frame = 0; frame < frames; frame++)
			{
				for (int c = 0; c < channels; c++)
				{
					float value = c < sourceChannels ? samples[frame * sourceChannels + c] / 32768f : 0;
					BitConverter.TryWriteBytes(bytes.AsSpan((frame * channels + c) * 4), value);
				}
			}
			if (buffer.BufferedDuration > MaxBuffered)
			{
				buffer.ClearBuffer();
			}
			buffer.AddSamples(bytes, 0, needed);
		}

		public void Dispose()
		{
			try
			{
				output.Stop();
			}
			catch (COMException)
			{
			}
			output.Dispose();
			device.Dispose();
		}
	}
}
