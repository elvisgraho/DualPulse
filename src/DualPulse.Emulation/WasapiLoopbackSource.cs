using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using DualPulse.Core.Audio;

namespace DualPulse.Emulation;

// Windows implementation of ISystemAudioSource: WASAPI loopback of an output device (the default one unless an id is
// given) through NAudio. The shared-mode mix format is normally 32-bit float; 16-bit PCM is converted. Failures (no
// output device) make Start return false. A 20 ms buffer keeps the blocks short (NAudio's default of 100 ms delivers
// ~60 ms blocks, which made haptics trail the sound).
[SupportedOSPlatform("windows")]
public sealed class WasapiLoopbackSource : ISystemAudioSource
{
	private readonly object gate = new();

	private readonly string? deviceId;

	private ShortBufferLoopbackCapture? capture;

	private float[] converted = [];

	public WasapiLoopbackSource(string? deviceId = null)
	{
		this.deviceId = deviceId;
	}

	public int SampleRate { get; private set; }

	public int Channels { get; private set; }

	public event AudioBlock? Blocks;

	public bool Start()
	{
		lock (gate)
		{
			if (capture != null)
			{
				return true;
			}
			try
			{
				using MMDeviceEnumerator enumerator = new();
				ShortBufferLoopbackCapture started = new(deviceId == null ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(deviceId));
				SampleRate = started.WaveFormat.SampleRate;
				Channels = started.WaveFormat.Channels;
				started.DataAvailable += OnData;
				started.StartRecording();
				capture = started;
				return true;
			}
			catch (Exception)
			{
				capture = null;
				return false;
			}
		}
	}

	public void Stop()
	{
		lock (gate)
		{
			if (capture == null)
			{
				return;
			}
			capture.DataAvailable -= OnData;
			try
			{
				capture.StopRecording();
			}
			catch (Exception)
			{
			}
			capture.Dispose();
			capture = null;
		}
	}

	public void Dispose() => Stop();

	private void OnData(object? sender, WaveInEventArgs e)
	{
		if (sender is not WasapiCapture source || e.BytesRecorded == 0)
		{
			return;
		}
		WaveFormat format = source.WaveFormat;
		ReadOnlySpan<byte> bytes = e.Buffer.AsSpan(0, e.BytesRecorded);
		if (format.Encoding == WaveFormatEncoding.IeeeFloat || (format.Encoding == WaveFormatEncoding.Extensible && format.BitsPerSample == 32))
		{
			Blocks?.Invoke(MemoryMarshal.Cast<byte, float>(bytes));
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
			Blocks?.Invoke(converted.AsSpan(0, samples.Length));
		}
	}
}

// Loopback capture (as NAudio's WasapiLoopbackCapture) with a 20 ms buffer instead of 100 ms.
[SupportedOSPlatform("windows")]
internal sealed class ShortBufferLoopbackCapture(MMDevice device) : WasapiCapture(device, false, 20)
{
	protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback | base.GetAudioClientStreamFlags();
}

// Active Windows output devices (id and friendly name) and loopback sources for them.
[SupportedOSPlatform("windows")]
public sealed class WasapiLoopbackSourceFactory : ISystemAudioSourceFactory
{
	public IReadOnlyList<AudioDevice> OutputDevices()
	{
		try
		{
			using MMDeviceEnumerator enumerator = new();
			return [.. enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(d => new AudioDevice(d.ID, d.FriendlyName)).OrderBy(d => d.Name)];
		}
		catch (Exception)
		{
			return [];
		}
	}

	public ISystemAudioSource Create(string? deviceId) => new WasapiLoopbackSource(deviceId);
}

