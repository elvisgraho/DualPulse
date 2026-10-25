using DualPulse.App.Services;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class StartupServiceTests
{
	private const string Exe = @"C:\Apps\DualPulse\DualPulse.exe";

	private readonly FakeRunRegistry registry = new();

	private readonly FakeSettingsService settings = new();

	private StartupService CreateService(string exe = Exe) => new(registry, settings, exe);

	[Fact]
	public void Enabling_WritesAQuotedEntryThatStartsMinimized()
	{
		using StartupService service = CreateService();

		settings.Update(s => s with { StartWithWindows = true });

		Assert.Equal("\"C:\\Apps\\DualPulse\\DualPulse.exe\" --minimized", registry.Values["DualPulse"]);
	}

	[Fact]
	public void Disabling_RemovesOnlyItsOwnEntry()
	{
		registry.Values["DualPulse"] = "\"C:\\Apps\\DualPulse\\DualPulse.exe\" --minimized";
		registry.Values["SomethingElse"] = "other.exe";
		settings.Update(s => s with { StartWithWindows = true });
		using StartupService service = CreateService();

		settings.Update(s => s with { StartWithWindows = false });

		Assert.False(registry.Values.ContainsKey("DualPulse"));
		Assert.Equal("other.exe", registry.Values["SomethingElse"]);
	}

	[Fact]
	public void Start_UpdatesTheEntryWhenDualPulseMoved()
	{
		registry.Values["DualPulse"] = "\"C:\\Old\\DualPulse.exe\" --minimized";
		settings.Update(s => s with { StartWithWindows = true });
		using StartupService service = CreateService(@"D:\New\DualPulse.exe");

		service.Apply();

		Assert.Equal("\"D:\\New\\DualPulse.exe\" --minimized", registry.Values["DualPulse"]);
	}

	[Fact]
	public void Start_RemovesALeftoverEntryWhenTheSettingIsOff()
	{
		registry.Values["DualPulse"] = "\"C:\\Old\\DualPulse.exe\" --minimized";
		using StartupService service = CreateService();

		service.Apply();

		Assert.False(registry.Values.ContainsKey("DualPulse"));
	}

	[Fact]
	public void Start_ReplacesTheEntryFromBeforeTheRename()
	{
		registry.Values["Resonance"] = "\"C:\\Old\\Resonance.exe\" --minimized";
		settings.Update(s => s with { StartWithWindows = true });
		using StartupService service = CreateService();

		service.Apply();

		Assert.False(registry.Values.ContainsKey("Resonance"));
		Assert.True(registry.Values.ContainsKey("DualPulse"));
	}

	[Fact]
	public void RegistryErrors_DoNotThrow()
	{
		registry.Fail = true;
		using StartupService service = CreateService();

		settings.Update(s => s with { StartWithWindows = true });
	}

	[Theory]
	[InlineData(new[] { "--minimized" }, true)]
	[InlineData(new[] { "--MINIMIZED" }, true)]
	[InlineData(new string[0], false)]
	public void MinimizedArgument_IsRecognised(string[] args, bool minimized)
	{
		Assert.Equal(minimized, StartupService.StartedMinimized(args));
	}
}
