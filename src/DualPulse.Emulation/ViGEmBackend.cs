using System.Runtime.Versioning;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace DualPulse.Emulation;

// Virtual pads through the ViGEmBus driver (Xbox 360 and DualShock 4). Without the driver the backend reports
// itself unavailable instead of throwing, so the app keeps working in native mode.
[SupportedOSPlatform("windows")]
public sealed class ViGEmBackend : IVirtualPadBackend, IDisposable
{
	private readonly object gate = new();

	private ViGEmClient? client;

	private bool probed;

	private string? unavailableReason;

	public bool IsAvailable
	{
		get
		{
			EnsureClient();
			return client != null;
		}
	}

	public string? UnavailableReason
	{
		get
		{
			EnsureClient();
			return unavailableReason;
		}
	}

	public IVirtualPad Create(PadKind kind)
	{
		EnsureClient();
		ViGEmClient vigem = client ?? throw new InvalidOperationException(unavailableReason);
		return kind switch
		{
			PadKind.Xbox360 => new ViGEmXboxPad(vigem.CreateXbox360Controller()),
			PadKind.DualShock4 => new ViGEmDs4Pad(vigem.CreateDualShock4Controller()),
			_ => throw new ArgumentOutOfRangeException(nameof(kind))
		};
	}

	public void Dispose()
	{
		lock (gate)
		{
			client?.Dispose();
			client = null;
		}
	}

	private void EnsureClient()
	{
		lock (gate)
		{
			if (probed)
			{
				return;
			}
			probed = true;
			try
			{
				client = new ViGEmClient();
			}
			catch (VigemBusNotFoundException)
			{
				unavailableReason = "ViGEmBus driver not installed";
			}
			catch (Exception exception)
			{
				unavailableReason = "ViGEmBus unavailable: " + exception.Message;
			}
		}
	}
}
