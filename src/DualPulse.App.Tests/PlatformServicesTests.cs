using Microsoft.Extensions.DependencyInjection;
using DualPulse.App.Platform;
using DualPulse.App.Services;
using DualPulse.Core.Audio;
using DualPulse.Core.Mapping;
using DualPulse.Core.Power;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class PlatformServicesTests
{
	private static ServiceProvider Unsupported()
	{
		ServiceCollection services = new();
		services.AddUnsupportedPlatform();
		return services.BuildServiceProvider();
	}

	[Fact]
	public void Unsupported_VirtualPadsAreUnavailableWithAReason()
	{
		using ServiceProvider provider = Unsupported();
		IVirtualPadBackend backend = provider.GetRequiredService<IVirtualPadBackend>();

		Assert.False(backend.IsAvailable);
		Assert.False(string.IsNullOrWhiteSpace(backend.UnavailableReason));
		Assert.Throws<PlatformNotSupportedException>(() => backend.Create(PadKind.Xbox360));
	}

	[Fact]
	public void Unsupported_HidHideIsNotInstalledAndHidesNothing()
	{
		using ServiceProvider provider = Unsupported();
		IHidHideDriver driver = provider.GetRequiredService<IHidHideDriver>();

		driver.SetHiddenDevices(["HID\\VID_054C"]);
		driver.SetActive(true);

		Assert.False(driver.IsInstalled);
		Assert.False(driver.IsAttached);
		Assert.Empty(driver.GetHiddenDevices());
		Assert.Empty(driver.GetAllowedApps());
		Assert.False(driver.GetActive());
	}

	[Fact]
	public void Unsupported_WindowsAndFocusAreUnknown()
	{
		using ServiceProvider provider = Unsupported();

		Assert.Null(provider.GetRequiredService<IForegroundWatcher>().ForegroundExe);
		Assert.Empty(provider.GetRequiredService<IRunningWindows>().List(Environment.ProcessId));
	}

	[Fact]
	public void Unsupported_MicrophoneAndAudioCaptureAreAbsent()
	{
		using ServiceProvider provider = Unsupported();
		IMicrophone microphone = provider.GetRequiredService<IMicrophone>();
		ISystemAudioSource audio = provider.GetRequiredService<ISystemAudioSource>();

		Assert.Null(microphone.IsMuted);
		Assert.False(microphone.Toggle());
		Assert.False(audio.Start());
		audio.Stop();
	}

	[Fact]
	public void Unsupported_HasNoDefaultOutputToGuard()
	{
		using ServiceProvider provider = Unsupported();
		IDefaultAudioOutput output = provider.GetRequiredService<IDefaultAudioOutput>();

		Assert.Null(output.Current);
		output.Set("anything");
		Assert.Null(provider.GetRequiredService<IControllerUsbAudio>().Open());
	}

	[Fact]
	public void Unsupported_ControllerAudioHasNoDevices()
	{
		using ServiceProvider provider = Unsupported();
		ISystemAudioSourceFactory factory = provider.GetRequiredService<ISystemAudioSourceFactory>();

		Assert.Empty(factory.OutputDevices());
		using ISystemAudioSource source = factory.Create(null);
		Assert.False(source.Start());
	}

	[Fact]
	public void Unsupported_PcOutputIgnoresInput()
	{
		using ServiceProvider provider = Unsupported();
		IPcOutput output = provider.GetRequiredService<IPcOutput>();

		output.MoveMouse(10, -5);
		output.MouseButton(MouseButton.Left, true);
		output.Wheel(1);
		output.Key(PcKey.Enter, true);
	}

	[Fact]
	public void Unsupported_RunAtSignInKeepsNoEntry()
	{
		using ServiceProvider provider = Unsupported();
		IRunRegistry registry = provider.GetRequiredService<IRunRegistry>();

		registry.Set("DualPulse", "dualpulse --minimized");

		Assert.Null(registry.Get("DualPulse"));
		registry.Delete("DualPulse");
	}

	[Fact]
	public void Unsupported_BluetoothRadioRefusesPowerOff()
	{
		using ServiceProvider provider = Unsupported();

		Assert.False(provider.GetRequiredService<IBluetoothRadio>().Disconnect(0x123456789ABC));
	}

	[Fact]
	public void Windows_RegistersTheWindowsImplementations()
	{
		Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
		ServiceCollection services = new();

		services.AddPlatformServices();

		Type? Implementation<T>() => services.Single(d => d.ServiceType == typeof(T)).ImplementationType;
		Assert.Equal(typeof(HidHideDriver), Implementation<IHidHideDriver>());
		Assert.Equal(typeof(ForegroundWatcher), Implementation<IForegroundWatcher>());
		Assert.Equal(typeof(RunningWindows), Implementation<IRunningWindows>());
		Assert.Equal(typeof(SendInputOutput), Implementation<IPcOutput>());
		Assert.Equal(typeof(WindowsMicrophone), Implementation<IMicrophone>());
		Assert.Equal(typeof(WasapiLoopbackSource), Implementation<ISystemAudioSource>());
		Assert.Equal(typeof(WasapiLoopbackSourceFactory), Implementation<ISystemAudioSourceFactory>());
		Assert.Equal(typeof(WindowsDefaultAudioOutput), Implementation<IDefaultAudioOutput>());
		Assert.Equal(typeof(WindowsControllerUsbAudio), Implementation<IControllerUsbAudio>());
		Assert.Equal(typeof(RunRegistry), Implementation<IRunRegistry>());
		Assert.Equal(typeof(WindowsBluetoothRadio), Implementation<IBluetoothRadio>());
		using ServiceProvider provider = services.BuildServiceProvider();
		IVirtualPadBackend pads = provider.GetRequiredService<IVirtualPadBackend>();
		Assert.IsType<CompositePadBackend>(pads);
		// The in-house virtual DualSense: available exactly when usbip-win2 is installed.
		Assert.Equal(new UsbIpWinAttacher().IsAvailable, pads.IsAvailableFor(PadKind.DualSense));
	}

	[Fact]
	public void Unsupported_HasNoVirtualPads_Yet()
	{
		using ServiceProvider provider = Unsupported();
		IVirtualPadBackend pads = provider.GetRequiredService<IVirtualPadBackend>();

		Assert.False(pads.IsAvailableFor(PadKind.Xbox360));
		Assert.False(pads.IsAvailableFor(PadKind.DualSense));
	}
}
