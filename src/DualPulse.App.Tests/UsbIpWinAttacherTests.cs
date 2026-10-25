using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class UsbIpWinAttacherTests : IDisposable
{
	private readonly string root = Directory.CreateTempSubdirectory("dualpulse-usbip-").FullName;

	public void Dispose() => Directory.Delete(root, true);

	private string MakeTool(string folder)
	{
		string directory = Path.Combine(root, folder);
		Directory.CreateDirectory(directory);
		string tool = Path.Combine(directory, "usbip.exe");
		File.WriteAllBytes(tool, []);
		return tool;
	}

	[Fact]
	public void Find_PrefersTheInstallFolder_ThenPath()
	{
		string onPath = MakeTool("bin");
		Assert.Equal(onPath, UsbIpWinAttacher.Find(Path.Combine(root, "none"), $"{Path.Combine(root, "empty")};{Path.Combine(root, "bin")}"));

		string installed = MakeTool(Path.Combine("Program Files", "USBip"));
		Assert.Equal(installed, UsbIpWinAttacher.Find(Path.Combine(root, "Program Files"), Path.Combine(root, "bin")));
	}

	[Fact]
	public void ToolInstalledLater_IsFound()
	{
		string? tool = null;
		UsbIpWinAttacher attacher = new(() => tool);
		Assert.False(attacher.IsAvailable);

		tool = @"C:\Program Files\USBip\usbip.exe";

		Assert.True(attacher.IsAvailable);
		Assert.Null(attacher.UnavailableReason);
	}

	[Fact]
	public void NoTool_IsUnavailable_WithAReason()
	{
		UsbIpWinAttacher attacher = new(tool: null);

		Assert.False(attacher.IsAvailable);
		Assert.Contains("usbip-win2", attacher.UnavailableReason, StringComparison.Ordinal);
		Assert.Throws<UsbIpException>(() => attacher.Attach(3240, "1-1"));
	}

	[Theory]
	[InlineData("2\r\n", 0, 2)]
	[InlineData("11", 0, 11)]
	public void HubPort_IsWhatTerseAttachPrints(string output, int exitCode, int port)
	{
		Assert.Equal(port, UsbIpWinAttacher.HubPort(output, exitCode));
	}

	[Theory]
	[InlineData("usbip: error: cannot connect", 1)]
	[InlineData("", 0)]
	public void FailedAttach_ThrowsWithWhatTheToolSaid(string output, int exitCode)
	{
		UsbIpException error = Assert.Throws<UsbIpException>(() => UsbIpWinAttacher.HubPort(output, exitCode));

		Assert.Contains(output.Length > 0 ? output : "no port", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void HubPort_IgnoresWarningsOnStandardError()
	{
		// A warning on stderr used to be parsed with the port and failed an attach that had worked.
		Assert.Equal(2, UsbIpWinAttacher.HubPort("2\r\n", 0, "usbip: warning: something"));
	}

	[Fact]
	public void FailedAttach_SaysWhatTheToolPrintedOnStandardError()
	{
		UsbIpException error = Assert.Throws<UsbIpException>(() => UsbIpWinAttacher.HubPort("", 1, "usbip: error: cannot connect"));

		Assert.Contains("cannot connect", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ToolThatCannotStart_IsAUsbIpException()
	{
		UsbIpWinAttacher attacher = new(Path.Combine(root, "missing", "usbip.exe"));

		Assert.Throws<UsbIpException>(() => attacher.Attach(3240, "1-1"));
	}
}
