using System.Runtime.Versioning;
using DualPulse.Core.Mapping;
using DualPulse.Emulation;
using Xunit;

namespace DualPulse.App.Tests;

[SupportedOSPlatform("windows")]
public sealed class SendInputOutputTests
{
	[Fact]
	public void EveryKey_HasAVirtualKeyCode()
	{
		foreach (PcKey key in Enum.GetValues<PcKey>())
		{
			Assert.NotEqual(0, SendInputOutput.VirtualKey(key).Code);
		}
	}

	[Theory]
	[InlineData(PcKey.Enter, 0x0D, false)]
	[InlineData(PcKey.Escape, 0x1B, false)]
	[InlineData(PcKey.Up, 0x26, true)]
	[InlineData(PcKey.Windows, 0x5B, true)]
	[InlineData(PcKey.Alt, 0x12, false)]
	[InlineData(PcKey.PlayPause, 0xB3, false)]
	[InlineData(PcKey.VolumeMute, 0xAD, false)]
	public void Keys_MapToWindowsCodes(PcKey key, int code, bool extended)
	{
		Assert.Equal(((ushort)code, extended), SendInputOutput.VirtualKey(key));
	}
}
