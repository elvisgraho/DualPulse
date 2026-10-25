using System.Text;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class HidHideEncodingTests
{
	[Fact]
	public void MultiSz_EncodesWideStringsWithDoubleTerminator()
	{
		byte[] bytes = HidHideEncoding.ToMultiSz(["AB", "C"]);

		Assert.Equal(Encoding.Unicode.GetBytes("AB\0C\0\0"), bytes);
		Assert.Equal(0, bytes.Length % 2);
	}

	[Fact]
	public void MultiSz_EmptyListIsTwoTerminators()
	{
		Assert.Equal(Encoding.Unicode.GetBytes("\0\0"), HidHideEncoding.ToMultiSz([]));
	}

	[Fact]
	public void MultiSz_DecodesUpToTheEmptyEntry()
	{
		byte[] buffer = Encoding.Unicode.GetBytes(@"HID\VID_054C&PID_0CE6\1" + "\0" + @"HID\X" + "\0\0" + "garbage");

		Assert.Equal([@"HID\VID_054C&PID_0CE6\1", @"HID\X"], HidHideEncoding.FromMultiSz(buffer));
		Assert.Empty(HidHideEncoding.FromMultiSz(Encoding.Unicode.GetBytes("\0\0")));
		Assert.Empty(HidHideEncoding.FromMultiSz([]));
	}

	[Fact]
	public void DosDeviceName_ReplacesTheDriveLetter()
	{
		string? Query(string drive) => drive == "C:" ? @"\Device\HarddiskVolume3" : null;

		Assert.Equal(@"\Device\HarddiskVolume3\Tools\DualPulse.exe", HidHideEncoding.ToDosDeviceName(@"C:\Tools\DualPulse.exe", Query));
		Assert.Null(HidHideEncoding.ToDosDeviceName(@"D:\Tools\DualPulse.exe", Query));
		Assert.Null(HidHideEncoding.ToDosDeviceName(@"\\server\share\DualPulse.exe", Query));
	}
}
