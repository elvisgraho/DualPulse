using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;

namespace DualPulse.App.ViewModels;

// The Mods page: the DSX-compatible mod server's switch, port and status, what mods sent recently, and handing
// control back to the user's settings.
public sealed partial class ModsViewModel : ObservableObject
{
	private readonly ISettingsService settings;

	private readonly IModServer server;

	private readonly TimeProvider time;

	private readonly UiContext ui = new();

	private string portText;

	public ModsViewModel(ISettingsService settings, IModServer server, TimeProvider time)
	{
		this.settings = settings;
		this.server = server;
		this.time = time;
		portText = settings.Current.EffectiveModServer.Port.ToString(CultureInfo.InvariantCulture);
		settings.Changed += _ => ui.Run(() =>
		{
			OnPropertyChanged(nameof(Enabled));
			OnPropertyChanged(nameof(WritePortFiles));
		});
		server.Changed += () => ui.Run(Refresh);
		Refresh();
	}

	[ObservableProperty]
	public partial string StatusText { get; private set; } = "";

	[ObservableProperty]
	public partial string LastPacketText { get; private set; } = "";

	[ObservableProperty]
	public partial string CountsText { get; private set; } = "";

	[ObservableProperty]
	public partial IReadOnlyList<string> RecentInstructions { get; private set; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(GiveControlBackCommand))]
	public partial bool ModActive { get; private set; }

	[ObservableProperty]
	public partial bool PortInvalid { get; private set; }

	private ModServerSettings Options => settings.Current.EffectiveModServer;

	public bool Enabled
	{
		get => Options.Enabled;
		set => Update(Options with { Enabled = value });
	}

	public bool WritePortFiles
	{
		get => Options.WritePortFiles;
		set => Update(Options with { WritePortFiles = value });
	}

	// 1-65535; anything else is flagged and not saved.
	public string PortText
	{
		get => portText;
		set
		{
			if (!SetProperty(ref portText, value))
			{
				return;
			}
			bool valid = int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= 65535;
			PortInvalid = !valid;
			if (valid)
			{
				Update(Options with { Port = port });
			}
		}
	}

	public void Refresh()
	{
		StatusText = server.State switch
		{
			ModServerState.Listening => $"Ready for mods (port {server.BoundPort})",
			ModServerState.PortInUse => $"Port {Options.Port} is used by another program",
			ModServerState.Error => "Couldn't start. See the log.",
			_ => "Off"
		};
		LastPacketText = server.LastPacketAt is { } last
			? $"Last mod command {Ago(time.GetUtcNow() - last)} ago"
			: "No mod commands yet";
		CountsText = $"{server.PacketsReceived} packets, {server.RejectedInstructions} rejected";
		RecentInstructions = server.RecentInstructions;
		ModActive = server.ModActive;
	}

	[RelayCommand(CanExecute = nameof(ModActive))]
	private void GiveControlBack()
	{
		server.GiveControlBack();
		Refresh();
	}

	private static string Ago(TimeSpan elapsed)
	{
		return elapsed.TotalSeconds < 60 ? $"{Math.Max(0, (int)elapsed.TotalSeconds)} s"
			: elapsed.TotalMinutes < 60 ? $"{(int)elapsed.TotalMinutes} min"
			: $"{(int)elapsed.TotalHours} h";
	}

	private void Update(ModServerSettings updated)
	{
		if (updated != Options)
		{
			settings.Update(s => s with { ModServer = updated });
		}
	}
}
