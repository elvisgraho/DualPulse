using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using DualPulse.Core.Devices;
using DualPulse.Core.Usb;
using DualPulse.Core.UsbIp;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class VirtualDualSenseTests
{
	private readonly VirtualDualSense device = new("1-1");

	private uint seqnum;

	private UrbSubmit Control(byte requestType, byte request, ushort value, ushort index, ushort length, byte[]? data = null)
	{
		byte[] setup = new byte[8];
		setup[0] = requestType;
		setup[1] = request;
		BinaryPrimitives.WriteUInt16LittleEndian(setup.AsSpan(2), value);
		BinaryPrimitives.WriteUInt16LittleEndian(setup.AsSpan(4), index);
		BinaryPrimitives.WriteUInt16LittleEndian(setup.AsSpan(6), length);
		return new UrbSubmit(++seqnum, 0, (requestType & 0x80) != 0, 0, 0, length, 0, 0, 0, setup, data ?? [], []);
	}

	private UrbResult Run(UrbSubmit submit)
	{
		UrbResult? result = null;
		device.Submit(submit, r => result = r);
		Assert.NotNull(result);
		return result;
	}

	[Fact]
	public void Export_IsADualSense_WithItsFourInterfaces()
	{
		Assert.Equal("1-1", device.Export.BusId);
		Assert.Equal(DualSenseUsbDescriptors.VendorId, device.Export.VendorId);
		Assert.Equal(DualSenseUsbDescriptors.ProductId, device.Export.ProductId);
		Assert.Equal([(1, 1, 0), (1, 2, 0), (1, 2, 0), (3, 0, 0)], device.Export.Interfaces);
	}

	[Fact]
	public void WithoutSound_IsOnlyTheController()
	{
		// For people who don't want its sound device in Windows: games then keep triggers and lights, not haptics.
		VirtualDualSense controllerOnly = new("1-1", withSound: false);

		Assert.Equal([(3, 0, 0)], controllerOnly.Export.Interfaces);
		byte[] config = RunOn(controllerOnly, Control(0x80, 6, 0x0200, 0, 255)).Data;
		Assert.Equal(config.Length, BinaryPrimitives.ReadUInt16LittleEndian(config.AsSpan(2)));
		Assert.Equal(1, config[4]);
		// One interface, number 0, HID, with its HID descriptor and both interrupt endpoints.
		Assert.Equal(new byte[] { 9, 4, 0, 0, 2, 3, 0, 0, 0 }, config[9..18]);
		Assert.Equal(9 + 9 + 9 + 7 + 7, config.Length);
		Assert.Equal(DualSenseUsbDescriptors.HidReport.ToArray(), RunOn(controllerOnly, Control(0x81, 6, 0x2200, 0, 1024)).Data);
	}

	[Fact]
	public void WithoutSound_HasASerialOfItsOwn_StillKnownAsDualPulses()
	{
		// Windows remembers a device's layout by its serial number.
		VirtualDualSense controllerOnly = new("1-1", withSound: false);

		string serial = Text(RunOn(controllerOnly, Control(0x80, 6, 0x0303, 0x0409, 255)).Data);

		Assert.NotEqual(ControllerDiscovery.VirtualSerialNumber, serial);
		Assert.True(ControllerDiscovery.IsVirtualSerial(serial));
	}

	private UrbResult RunOn(VirtualDualSense target, UrbSubmit submit)
	{
		UrbResult? result = null;
		target.Submit(submit, r => result = r);
		Assert.NotNull(result);
		return result;
	}

	[Fact]
	public void DeviceDescriptor()
	{
		UrbResult result = Run(Control(0x80, 6, 0x0100, 0, 18));

		Assert.Equal(DualSenseUsbDescriptors.Device.ToArray(), result.Data);
		Assert.Equal(3, result.Data[16]);
	}

	[Fact]
	public void ConfigurationDescriptor_TruncatedToTheRequestedLength()
	{
		Assert.Equal(9, Run(Control(0x80, 6, 0x0200, 0, 9)).Data.Length);
		Assert.Equal(227, Run(Control(0x80, 6, 0x0200, 0, 255)).Data.Length);
	}

	[Fact]
	public void Endpoints_UseFullSpeedIntervals_MatchingTheExportedSpeed()
	{
		// The real controller is high speed (bInterval counts 2^(n-1) microframes); the virtual one is exported at full
		// speed, where isochronous endpoints must poll every frame. With the high-speed values Windows never streams audio.
		Assert.Equal(UsbIpSpeed.Full, device.Export.Speed);
		byte[] config = Run(Control(0x80, 6, 0x0200, 0, 255)).Data;
		Dictionary<byte, byte> intervals = [];
		for (int i = 0; i < config.Length; i += config[i])
		{
			if (config[i + 1] == 5)
			{
				intervals[config[i + 2]] = config[i + 6];
			}
		}
		Assert.Equal(new Dictionary<byte, byte> { [0x01] = 1, [0x82] = 1, [0x84] = 1, [0x03] = 1 }, intervals);
	}

	[Fact]
	public void Strings_LanguageManufacturerProductAndTheVirtualSerial()
	{
		Assert.Equal(new byte[] { 4, 3, 0x09, 0x04 }, Run(Control(0x80, 6, 0x0300, 0, 255)).Data);
		Assert.Equal("Sony Interactive Entertainment", Text(Run(Control(0x80, 6, 0x0301, 0x0409, 255)).Data));
		Assert.Equal(ControllerDiscovery.VirtualSerialNumber, Text(Run(Control(0x80, 6, 0x0303, 0x0409, 255)).Data));
	}

	private static string Text(byte[] descriptor) => Encoding.Unicode.GetString(descriptor, 2, descriptor[0] - 2);

	[Fact]
	public void HidReportDescriptor_OnTheHidInterface()
	{
		UrbResult result = Run(Control(0x81, 6, 0x2200, 3, 1024));

		Assert.Equal(DualSenseUsbDescriptors.HidReport.ToArray(), result.Data);
	}

	[Fact]
	public void FeatureReports_CalibrationAndPairing()
	{
		byte[] calibration = Run(Control(0xA1, 1, 0x0305, 3, 64)).Data;
		byte[] pairing = Run(Control(0xA1, 1, 0x0309, 3, 64)).Data;

		Assert.Equal(DualSenseUsbDescriptors.FeatureCalibration.ToArray(), calibration);
		Assert.Equal(new byte[] { 0x09, 0x01, 0x4E, 0x4F, 0x53, 0x45, 0x52 }, pairing[..7]);
	}

	[Fact]
	public void UnknownFeature_IsAnswered_UnknownRequest_Stalls()
	{
		byte[] unknown = Run(Control(0xA1, 1, 0x03F0, 3, 64)).Data;

		Assert.Equal(0xF0, unknown[0]);
		Assert.Equal(UrbResult.Stall, Run(Control(0x80, 0x55, 0, 0, 8)));
	}

	[Fact]
	public void SetConfigurationAndInterface_AreAccepted()
	{
		Assert.Equal(0, Run(Control(0x00, 9, 1, 0, 0)).Status);
		Assert.Equal(0, Run(Control(0x01, 11, 1, 1, 0)).Status);
		Assert.Equal(new byte[] { 1 }, Run(Control(0x81, 10, 0, 1, 1)).Data);
	}

	[Fact]
	public void AudioSampleRate_Is48kHz()
	{
		byte[] rate = Run(Control(0xA2, 0x81, 0x0100, DualSenseUsbDescriptors.SpeakerEndpoint, 3)).Data;

		Assert.Equal(new byte[] { 0x80, 0xBB, 0x00 }, rate);
	}

	private UrbSubmit InputTransfer() => new(++seqnum, 0, true, 4, 0, 64, 0, 0, 0, new byte[8], [], []);

	[Fact]
	public void InputReports_CompletePendingInterruptTransfers()
	{
		UrbResult? first = null;
		device.Submit(InputTransfer(), r => first = r);
		Assert.Null(first);

		byte[] report = new byte[64];
		report[0] = 0x01;
		report[1] = 42;
		device.SetInput(report);

		Assert.NotNull(first);
		Assert.Equal(42, first.Data[1]);
	}

	[Fact]
	public void CancelledTransfers_NeverComplete()
	{
		UrbResult? result = null;
		UrbSubmit pending = InputTransfer();
		device.Submit(pending, r => result = r);

		Assert.True(device.Cancel(pending.Seqnum));
		device.SetInput(new byte[64]);

		Assert.Null(result);
		Assert.False(device.Cancel(pending.Seqnum));
	}

	[Fact]
	public void GameOutputReport_RaisesTheBlock()
	{
		byte[]? block = null;
		device.OutputBlock += b => block = b;
		byte[] output = new byte[48];
		output[0] = 0x02;
		output[1 + 44] = 0xAB;

		Assert.Equal(0, Run(new UrbSubmit(++seqnum, 0, false, 3, 0, 48, 0, 0, 0, new byte[8], output, [])).Status);

		Assert.Equal(47, block!.Length);
		Assert.Equal(0xAB, block[44]);
	}

	[Fact]
	public void GameAudio_SplitsIntoSpeakerAndHaptics()
	{
		short[]? speaker = null;
		short[]? haptics = null;
		device.Speaker += (samples, channels) => speaker = channels == 4 ? samples.ToArray() : null;
		device.Haptics += (samples, channels) => haptics = channels == 2 ? samples.ToArray() : null;
		// Two 4-channel frames: (1, 2, 3, 4), (5, 6, 7, 8).
		byte[] pcm = new byte[16];
		for (int i = 0; i < 8; i++)
		{
			BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(i + 1));
		}

		device.Submit(new UrbSubmit(++seqnum, 0, false, 1, 0, 16, 0, 2, 1, new byte[8], pcm, [(0, 8), (8, 8)]), _ => { });

		Assert.Equal(new short[] { 1, 2, 3, 4, 5, 6, 7, 8 }, speaker);
		Assert.Equal(new short[] { 3, 4, 7, 8 }, haptics);
	}

	[Fact]
	public void GameAudio_CompletesAtTheFrameRate_WithConsecutiveStartFrames()
	{
		// Windows queues several 10 ms transfers; each must take its 10 frames, or the host races ahead and resets the pipe.
		List<UrbResult> done = [];
		pacedDevice.Submit(AudioTransfer(10), r => done.Add(r));
		pacedDevice.Submit(AudioTransfer(10), r => done.Add(r));

		time.Advance(TimeSpan.FromMilliseconds(9));
		Assert.Empty(done);
		time.Advance(TimeSpan.FromMilliseconds(1));
		Assert.Single(done);
		time.Advance(TimeSpan.FromMilliseconds(10));
		Assert.Equal(2, done.Count);
		Assert.All(done, r => Assert.Equal(0, r.Status));
		Assert.Equal(done[0].StartFrame + 10, done[1].StartFrame);
	}

	[Fact]
	public void GameAudio_AfterAGap_StartsAtTheCurrentFrame()
	{
		// A host that pauses must not get a backlog of instantly completed transfers when it resumes.
		List<UrbResult> done = [];
		pacedDevice.Submit(AudioTransfer(10), r => done.Add(r));
		time.Advance(TimeSpan.FromMilliseconds(500));

		pacedDevice.Submit(AudioTransfer(10), r => done.Add(r));
		time.Advance(TimeSpan.FromMilliseconds(9));
		Assert.Single(done);
		time.Advance(TimeSpan.FromMilliseconds(1));
		Assert.Equal(2, done.Count);
		Assert.Equal(done[0].StartFrame + 500, done[1].StartFrame);
	}

	[Fact]
	public void GameAudio_Unlinked_NeverCompletes()
	{
		UrbResult? result = null;
		UrbSubmit transfer = AudioTransfer(10);
		pacedDevice.Submit(transfer, r => result = r);

		Assert.True(pacedDevice.Cancel(transfer.Seqnum));
		time.Advance(TimeSpan.FromMilliseconds(20));

		Assert.Null(result);
	}

	[Fact]
	public void Microphone_NothingFed_CompletesWithSilence_AtTheFrameRate()
	{
		UrbResult? result = null;
		pacedDevice.Submit(MicTransfer(10, 196), r => result = r);

		time.Advance(TimeSpan.FromMilliseconds(9));
		Assert.Null(result);
		time.Advance(TimeSpan.FromMilliseconds(1));
		Assert.Equal(new byte[1920], result!.Data);
		Assert.Equal(Enumerable.Repeat(192, 10), result.IsoActualLengths!);
	}

	[Fact]
	public void Microphone_CarriesWhatWasFed_48FramesPerPacket()
	{
		// 48 kHz stereo: one 1 ms packet is 48 frames (192 bytes), whatever room the host leaves.
		short[] fed = [.. Enumerable.Range(1, 2 * 1100).Select(i => (short)i)];
		pacedDevice.WriteMicrophone(fed);
		UrbResult? result = null;

		pacedDevice.Submit(MicTransfer(12, 196), r => result = r);
		time.Advance(TimeSpan.FromMilliseconds(12));

		Assert.Equal(Enumerable.Repeat(192, 12), result!.IsoActualLengths!);
		Assert.Equal(fed[..(2 * 576)], Samples(result));
	}

	[Fact]
	public void Microphone_TransfersQueuedAhead_CarrySoundThatArrivesBeforeTheirTime()
	{
		// The host queues transfers ahead; the microphone's sound for later ones arrives while earlier ones play.
		pacedDevice.WriteMicrophone(Enumerable.Repeat((short)500, 2 * 480).ToArray());
		UrbResult? first = null, second = null;
		pacedDevice.Submit(MicTransfer(10, 196), r => first = r);
		pacedDevice.Submit(MicTransfer(10, 196), r => second = r);

		pacedDevice.WriteMicrophone(Enumerable.Repeat((short)1000, 2 * 480).ToArray());
		time.Advance(TimeSpan.FromMilliseconds(10));
		pacedDevice.WriteMicrophone(Enumerable.Repeat((short)2000, 2 * 480).ToArray());
		time.Advance(TimeSpan.FromMilliseconds(10));

		// Oldest first: the 10 ms cushion, then what arrived while the first transfer played.
		Assert.All(Samples(first!), s => Assert.Equal(500, s));
		Assert.All(Samples(second!), s => Assert.Equal(1000, s));
	}

	[Fact]
	public void Microphone_AfterRunningDry_WaitsForATransferPlus10ms()
	{
		// Starting again on a few samples would run dry at once and crackle.
		pacedDevice.WriteMicrophone(Enumerable.Repeat((short)1000, 2 * 200).ToArray());
		UrbResult? waiting = null, playing = null;

		pacedDevice.Submit(MicTransfer(10, 196), r => waiting = r);
		time.Advance(TimeSpan.FromMilliseconds(10));
		pacedDevice.WriteMicrophone(Enumerable.Repeat((short)1000, 2 * 760).ToArray());
		pacedDevice.Submit(MicTransfer(10, 196), r => playing = r);
		time.Advance(TimeSpan.FromMilliseconds(10));

		Assert.All(Samples(waiting!), s => Assert.Equal(0, s));
		Assert.All(Samples(playing!), s => Assert.Equal(1000, s));
	}

	[Fact]
	public void Microphone_KeepsA10msCushion_SoALateBlockDoesntGap()
	{
		// The real microphone delivers 10 ms blocks; one arriving a little late must not leave a transfer short.
		UrbResult? waiting = null, playing = null, late = null;
		pacedDevice.WriteMicrophone(Enumerable.Repeat((short)1000, 2 * 480).ToArray());
		pacedDevice.Submit(MicTransfer(10, 196), r => waiting = r);
		time.Advance(TimeSpan.FromMilliseconds(10));

		pacedDevice.WriteMicrophone(Enumerable.Repeat((short)1000, 2 * 480).ToArray());
		pacedDevice.Submit(MicTransfer(10, 196), r => playing = r);
		time.Advance(TimeSpan.FromMilliseconds(10));
		pacedDevice.Submit(MicTransfer(10, 196), r => late = r);
		time.Advance(TimeSpan.FromMilliseconds(10));

		Assert.All(Samples(waiting!), s => Assert.Equal(0, s));
		Assert.All(Samples(playing!), s => Assert.Equal(1000, s));
		Assert.All(Samples(late!), s => Assert.Equal(1000, s));
	}

	[Fact]
	public void Microphone_HostReconfiguring_StopsRecording()
	{
		List<bool> changes = [];
		device.MicrophoneOpened += changes.Add;
		Run(Control(0x01, 11, 1, 2, 0));

		Run(Control(0x00, 9, 1, 0, 0));

		Assert.Equal([true, false], changes);
		Assert.Equal(new byte[] { 0 }, Run(Control(0x81, 10, 0, 2, 1)).Data);
	}

	private static short[] Samples(UrbResult result)
	{
		short[] samples = new short[result.Data.Length / 2];
		Buffer.BlockCopy(result.Data, 0, samples, 0, result.Data.Length);
		return samples;
	}

	[Fact]
	public void Microphone_OpenedAndClosed_ByTheHostsInterfaceChoice()
	{
		List<bool> changes = [];
		device.MicrophoneOpened += changes.Add;

		Run(Control(0x01, 11, 1, 2, 0));
		Run(Control(0x01, 11, 1, 1, 0));
		Run(Control(0x01, 11, 0, 2, 0));

		Assert.Equal([true, false], changes);
	}

	private readonly FakeTimeProvider time = new();

	private VirtualDualSense? paced;

	private VirtualDualSense pacedDevice => paced ??= new VirtualDualSense("1-1", time);

	private UrbSubmit MicTransfer(int packets, int packetLength) =>
		new(++seqnum, 0, true, 2, 0, packets * packetLength, 0, packets, 1, new byte[8], [], [.. Enumerable.Range(0, packets).Select(i => (i * packetLength, packetLength))]);

	private UrbSubmit AudioTransfer(int packets) =>
		new(++seqnum, 0, false, 1, 0, packets * 384, 0, packets, 1, new byte[8], new byte[packets * 384], [.. Enumerable.Range(0, packets).Select(i => (i * 384, 384))]);
}
