using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using DualPulse.Core.Devices;
using DualPulse.Core.Features;
using DualPulse.Core.Protocol;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class ControllerSessionTests : IDisposable
{
	private readonly FakeHidTransport transport = new();

	private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));

	private ControllerSession? session;

	public void Dispose() => session?.Dispose();

	private ControllerSession StartSession(FakeDevice device)
	{
		session = new ControllerSession(device.Info, transport, BluetoothLayout.Sequenced, NullLogger.Instance, time);
		session.Start();
		return session;
	}

	[Fact]
	public void Session_ParsesReports_UpdatesLatestBatteryAndLinkQuality()
	{
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		ControllerSession s = StartSession(device);
		FakeConnection connection = device.Latest;
		connection.OnRead = () => time.Advance(TimeSpan.FromMilliseconds(4));
		int received = 0;
		s.InputReceived += _ => Interlocked.Increment(ref received);

		for (int i = 0; i < 300; i++)
		{
			connection.Feed(TestReports.BluetoothWithSequence("idle", i));
		}
		TestReports.WaitUntil(() => received == 300);

		Assert.Equal(Buttons.None, s.Latest!.Value.Buttons);
		Assert.Equal(60, s.Battery.Level);
		Assert.Equal(DualSenseChargingState.Charging, s.Battery.State);
		Assert.InRange(s.LinkQuality!.ReportsPerSecond, 240, 260);
		Assert.Equal(0.0, s.LinkQuality.LossPercent);
	}

	[Theory]
	[InlineData((ushort)0x0630, true)]
	[InlineData((ushort)0x0223, false)]
	public void Session_ReadsFirmware_EnablesImprovedRumbleFrom0x0224(ushort updateVersion, bool improved)
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		device.FirmwareReport = TestReports.Firmware(updateVersion);

		ControllerSession s = StartSession(device);

		Assert.Equal(updateVersion, s.Firmware!.UpdateVersion);
		Assert.Equal(improved, s.Output.ImprovedRumble);
		// The rumble selection goes out while the motors run.
		s.Apply(state => state with { LeftMotor = 100 });
		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0 && device.Latest.Writes[^1][1 + 3] == 100);
		byte flag2 = device.Latest.Writes[^1][39];
		Assert.Equal(improved, (flag2 & 0x04) != 0);
	}

	[Fact]
	public void Session_FirmwareUnreadableAtFirst_ReadsItWhileSettling()
	{
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		device.FirmwareReport = null;

		ControllerSession s = StartSession(device);
		Assert.Null(s.Firmware);

		device.FirmwareReport = TestReports.Firmware0630;
		time.Advance(TimeSpan.FromMilliseconds(500));

		TestReports.WaitUntil(() => s.Firmware != null);
		Assert.Equal((ushort)0x0630, s.Firmware!.UpdateVersion);
		Assert.True(s.Output.ImprovedRumble);
	}

	[Fact]
	public void Session_Apply_CoalescesToNewestReport()
	{
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		ControllerSession s = StartSession(device);

		for (byte i = 0; i < 50; i++)
		{
			byte red = i;
			s.Apply(state => state with { Lightbar = new Rgb(red, 0, 0) });
		}

		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0 && device.Latest.Writes[^1][47] == 49);
		byte[] last = device.Latest.Writes[^1];
		Assert.Equal(new byte[] { 49, 0, 0 }, last[47..50]);
		Assert.Equal(547, last.Length);
	}

	[Fact]
	public void Session_Stop_EndsBothThreads()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		ControllerSession s = StartSession(device);
		s.Apply(state => state with { Lightbar = new Rgb(1, 2, 3) });

		s.Stop("test");

		TestReports.WaitUntil(() => !s.IsReading && !s.IsWriting);
		Assert.True(device.Latest.IsDisposed);
	}

	[Fact]
	public void Session_Latest_NeverMixesTwoReports()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		ControllerSession s = StartSession(device);
		// Report A: left stick X 0, timestamp 0. Report B: left stick X 255, timestamp 0xFFFFFFFF. A copy that mixes
		// the two shows one field from each.
		byte[] a = TestReports.Fixture("idle-usb");
		byte[] b = (byte[])a.Clone();
		a[1] = 0;
		b[1] = 255;
		a.AsSpan(1 + 27, 4).Fill(0x00);
		b.AsSpan(1 + 27, 4).Fill(0xFF);
		for (int i = 0; i < 200_000; i++)
		{
			device.Latest.Feed(i % 2 == 0 ? a : b);
		}

		int torn = 0;
		while (device.Latest.PendingReports > 0)
		{
			if (s.Latest is { } latest && (latest.LeftX == 0) != (latest.SensorTimestamp == 0))
			{
				torn++;
			}
		}

		Assert.Equal(0, torn);
	}

	[Fact]
	public void Session_Stop_ResetsTriggersMotorsAndLightbar()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		ControllerSession s = StartSession(device);
		s.Apply(state => state with { LeftTrigger = TriggerEffects.Weapon(2, 6, 8), RightTrigger = TriggerEffects.Weapon(2, 6, 8), LeftMotor = 200, RightMotor = 100, Lightbar = new Rgb(9, 9, 9) });
		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0 && device.Latest.Writes[^1][1 + 3] == 200);

		s.Stop("test");

		// USB common block at 1: motors at +2/+3, right trigger mode at +10, left at +21, RGB at +44.
		byte[] last = device.Latest.Writes[^1];
		Assert.Equal(0, last[1 + 2]);
		Assert.Equal(0, last[1 + 3]);
		Assert.Equal(0x05, last[1 + 10]);
		Assert.Equal(0x05, last[1 + 21]);
		Assert.Equal(new byte[] { 0, 0, 0 }, last[(1 + 44)..(1 + 47)]);
		Assert.Equal(0x0C, last[1] & 0x0C);
	}

	[Fact]
	public void Session_WriteFailures_LoggedOnce_AndRecoveryLogged()
	{
		ListLogger<ControllerSession> log = new();
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		session = new ControllerSession(device.Info, transport, BluetoothLayout.Sequenced, log, time);
		session.Start();
		device.Latest.FailWrites = true;

		for (byte i = 1; i <= 5; i++)
		{
			byte red = i;
			session.Apply(state => state with { Lightbar = new Rgb(red, 0, 0) });
			Thread.Sleep(20);
		}
		device.Latest.FailWrites = false;
		session.Apply(state => state with { Lightbar = new Rgb(99, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0 && device.Latest.Writes[^1][1 + 44] == 99);

		lock (log.Entries)
		{
			Assert.Single(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("rejected", StringComparison.Ordinal));
			Assert.Single(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("Writes to", StringComparison.Ordinal));
		}
	}

	[Fact]
	public void Session_SendsTriggerEffects_OnlyWhenTheyChange()
	{
		// In native mode a game drives the triggers itself; every report carrying DualPulse's own (Off) effect would
		// switch the game's resistance off again, so other changes (lightbar, LEDs) leave the triggers alone.
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		ControllerSession s = StartSession(device);
		s.Apply(state => state with { Lightbar = new Rgb(1, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0 && device.Latest.Writes[^1][1 + 44] == 1);
		Assert.Equal(0x0C, device.Latest.Writes[0][1] & 0x0C);

		s.Apply(state => state with { Lightbar = new Rgb(2, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes[^1][1 + 44] == 2);
		Assert.Equal(0, device.Latest.Writes[^1][1] & 0x0C);

		s.Apply(state => state with { RightTrigger = TriggerEffects.Weapon(2, 6, 8), Lightbar = new Rgb(3, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes[^1][1 + 44] == 3);
		Assert.Equal(0x0C, device.Latest.Writes[^1][1] & 0x0C);
	}

	[Fact]
	public void Session_SelectsClassicRumble_OnlyWhileTheMotorsRun()
	{
		// The selection switches a DualSense's haptics off until it is power-cycled; on USB every lightbar or LED update
		// carried it, so a game's haptics never played on the cable.
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		ControllerSession s = StartSession(device);
		s.Apply(state => state with { Lightbar = new Rgb(1, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0 && device.Latest.Writes[^1][1 + 44] == 1);
		Assert.All(device.Latest.Writes, w => Assert.Equal(0, w[1] & 0x03));
		Assert.All(device.Latest.Writes, w => Assert.Equal(0, w[1 + 38] & 0x04));

		s.Apply(state => state with { LeftMotor = 200, Lightbar = new Rgb(2, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes[^1][1 + 44] == 2);
		Assert.NotEqual(0, device.Latest.Writes[^1][1] & 0x03);

		// Stopping the motors takes the selection once more (with zero), then it stays out.
		s.Apply(state => state with { LeftMotor = 0, Lightbar = new Rgb(3, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes[^1][1 + 44] == 3);
		Assert.NotEqual(0, device.Latest.Writes[^1][1] & 0x03);
		Assert.Equal(0, device.Latest.Writes[^1][1 + 3]);
		s.Apply(state => state with { Lightbar = new Rgb(4, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes[^1][1 + 44] == 4);
		Assert.Equal(0, device.Latest.Writes[^1][1] & 0x03);
	}

	[Fact]
	public void Session_SendsItsOwnTriggerEffectsAgain_AfterAGameLetsGo()
	{
		// While a game drives the controller its trigger effects are what the controller has; DualPulse's own (unchanged)
		// ones must go out again when the game's block goes away.
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		ControllerSession s = StartSession(device);
		byte[] game = new byte[OutputState.CommonBlockLength];
		game[0] = 0x0C;
		game[1] = 0x04;
		game[10] = 0x26;
		game[44] = 7;
		s.Apply(state => state with { GameBlock = game });
		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0 && device.Latest.Writes[^1][1 + 44] == 7);

		s.Apply(state => state with { GameBlock = null, Lightbar = new Rgb(9, 0, 0) });

		TestReports.WaitUntil(() => device.Latest.Writes[^1][1 + 44] == 9);
		Assert.Equal(0x0C, device.Latest.Writes[^1][1] & 0x0C);
		Assert.Equal(0x05, device.Latest.Writes[^1][1 + 10]);
	}

	[Fact]
	public void Session_AppliesTriggerChanges_WhileAGameOnlySetsTheLights()
	{
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		ControllerSession s = StartSession(device);
		byte[] game = new byte[OutputState.CommonBlockLength];
		game[1] = 0x04;
		game[44] = 7;
		s.Apply(state => state with { GameBlock = game });
		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0 && device.Latest.Writes[^1][1 + 44] == 7);

		TriggerEffect weapon = TriggerEffects.Weapon(2, 6, 8);
		s.Apply(state => state with { RightTrigger = weapon });
		TestReports.WaitUntil(() => device.Latest.Writes[^1][1 + 10] == weapon.Bytes[0] && (device.Latest.Writes[^1][1] & 0x04) != 0);

		// Unchanged, they stay out of the game's later blocks.
		byte[] lights = [.. game];
		lights[44] = 8;
		s.Apply(state => state with { GameBlock = lights });
		TestReports.WaitUntil(() => device.Latest.Writes[^1][1 + 44] == 8);
		Assert.Equal(0, device.Latest.Writes[^1][1] & 0x0C);
	}

	[Fact]
	public void Session_ResetsAGamesPowerSettings_AfterItLetsGo()
	{
		// A game can switch on power saving (muting the speaker, mic or haptics) or reduce motor power; DualPulse never
		// flags those bytes itself, so they would stay.
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		ControllerSession s = StartSession(device);
		byte[] game = new byte[OutputState.CommonBlockLength];
		game[1] = 0x42 | 0x04;
		game[9] = 0x1F;
		game[36] = 0x55;
		game[44] = 7;
		s.Apply(state => state with { GameBlock = game });
		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0 && device.Latest.Writes[^1][1 + 44] == 7);

		s.Apply(state => state with { GameBlock = null, Lightbar = new Rgb(9, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes[^1][1 + 44] == 9);

		byte[] restored = device.Latest.Writes[^1];
		Assert.Equal(0x42, restored[1 + 1] & 0x42);
		Assert.Equal(0, restored[1 + 9]);
		Assert.Equal(0, restored[1 + 36]);
		s.Apply(state => state with { Lightbar = new Rgb(10, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes[^1][1 + 44] == 10);
		Assert.Equal(0, device.Latest.Writes[^1][1 + 1] & 0x42);
	}

	[Fact]
	public void Session_ResendsTheOutput_WhileTheControllerSettles_ThenStops()
	{
		// Right after a USB/Bluetooth switch the controller can ignore the first report (it shows its own blue
		// connection light), and output is otherwise only sent when it changes.
		FakeDevice device = transport.Add(FakeHidTransport.UsbPath);
		StartSession(device);
		session!.Apply(state => state with { Lightbar = new Rgb(255, 0, 0) });
		TestReports.WaitUntil(() => device.Latest.Writes.Count > 0);
		Thread.Sleep(50);
		int initial = device.Latest.Writes.Count;

		// One re-send at a time (0.5, 1.5, 2.5 s), so the writer can't merge them into one write.
		int count = initial;
		foreach (int step in new[] { 500, 1000, 1000 })
		{
			time.Advance(TimeSpan.FromMilliseconds(step));
			TestReports.WaitUntil(() => device.Latest.Writes.Count > count);
			count = device.Latest.Writes.Count;
		}
		Assert.Equal(255, device.Latest.Writes[^1][1 + 44]);
		Thread.Sleep(50);
		int settled = device.Latest.Writes.Count;

		time.Advance(TimeSpan.FromSeconds(10));
		Thread.Sleep(100);

		Assert.Equal(settled, device.Latest.Writes.Count);
	}

	[Fact]
	public void Session_HapticRumble_StreamsReport0x32OverBluetooth_ThenStops()
	{
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		StartSession(device);
		int Haptics() => device.Latest.Writes.Count(w => w[0] == 0x32);

		session!.SetHapticRumble(200, 50);
		for (int i = 0; i < 5; i++)
		{
			TestReports.WaitUntil(() => Haptics() > i);
			time.Advance(HapticsReport.Interval);
		}
		session.SetHapticRumble(0, 0);
		for (int i = 0; i < 40; i++)
		{
			time.Advance(HapticsReport.Interval);
			Thread.Sleep(5);
		}
		Thread.Sleep(50);
		int stopped = Haptics();
		time.Advance(TimeSpan.FromSeconds(1));
		Thread.Sleep(50);

		Assert.True(stopped >= 5);
		Assert.Equal(stopped, Haptics());
		Assert.True(device.Latest.Writes.First(w => w[0] == 0x32).Length >= 142);
	}

	[Fact]
	public void Session_PushedHapticAudio_StreamsOverBluetooth()
	{
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		StartSession(device);

		for (int i = 0; i < 64; i++)
		{
			session!.HapticMixer.Push(40, 40);
		}
		session!.WakeHaptics();

		TestReports.WaitUntil(() => device.Latest.Writes.Any(w => w[0] == 0x32 && (sbyte)w[13] == 40));
	}

	[Fact]
	public void Session_SpeakerAudio_StreamsReport0x39_PrimedWithSilence_ThenStops()
	{
		FakeDevice device = transport.Add(FakeHidTransport.BluetoothPath);
		StartSession(device);
		int Audio() => device.Latest.Writes.Count(w => w[0] == 0x39);
		float[] tone = new float[48000 / 10 * 2];
		for (int i = 0; i < tone.Length / 2; i++)
		{
			tone[2 * i] = tone[2 * i + 1] = (float)(0.3 * Math.Sin(2 * Math.PI * 440 * i / 48000));
		}

		session!.Speaker.Push(tone, 48000, 2);
		session.WakeAudio();
		for (int i = 0; i < 6; i++)
		{
			TestReports.WaitUntil(() => Audio() > i);
			time.Advance(AudioReport.Interval);
		}
		byte[][] audio = device.Latest.Writes.Where(w => w[0] == 0x39).ToArray();
		Assert.All(audio, w => Assert.Equal(547, w.Length));
		Assert.Equal(new byte[] { 0, 2, 4 }, audio.Take(3).Select(w => w[9]).ToArray());

		for (int i = 0; i < 80; i++)
		{
			time.Advance(AudioReport.Interval);
			Thread.Sleep(3);
		}
		Thread.Sleep(50);
		int stopped = Audio();
		time.Advance(TimeSpan.FromSeconds(1));
		Thread.Sleep(50);
		Assert.Equal(stopped, Audio());
	}
}
