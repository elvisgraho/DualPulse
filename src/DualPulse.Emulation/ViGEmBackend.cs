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

	// While ViGEmBus is missing it is looked for again at most this often, so installing it needs no restart.
	private static readonly long RetryMilliseconds = 5000;

	private long probedAt;

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
			if (probed && (client != null || Environment.TickCount64 - probedAt < RetryMilliseconds))
			{
				return;
			}
			probed = true;
			probedAt = Environment.TickCount64;
			try
			{
				client = new ViGEmClient();
				unavailableReason = null;
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
