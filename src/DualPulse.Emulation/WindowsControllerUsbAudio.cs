using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using DualPulse.Core.Audio;

namespace DualPulse.Emulation;

// The real DualSense's own speaker device on Windows (USB): the active render endpoint named DualSense whose USB
// audio interface does not belong to DualPulse's virtual pad (the parent USB device's instance id carries the virtual
// serial). Played through shared-mode WASAPI with little latency; samples arriving faster than the device plays are
// dropped beyond a short buffer, so haptics never lag behind the game.
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
			output = new WasapiOut(device, AudioClientShareMode.Shared, true, 20);
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
