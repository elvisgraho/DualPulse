using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualPulse.App.Services;
using DualPulse.Core.Devices;
using DualPulse.Core.Features;
using DualPulse.Core.Power;
using DualPulse.Core.Protocol;

namespace DualPulse.App.ViewModels;

public sealed record DeviceInfoRow(string Label, string Value);

// Everything known about the connected controller, for the device info window: connection, report rate, Bluetooth
// address (read once per connection), battery, firmware, hardware and how long it has
// been connected. Copy puts one "Label: value" line per row on the clipboard.
public sealed partial class DeviceInfoViewModel : ObservableObject
{
	private readonly IControllerService controller;

	private readonly TimeProvider time;

	private readonly Func<string, Task>? copy;

	private ControllerSession? addressFor;

	private long? address;

	private bool reading;

	public DeviceInfoViewModel(IControllerService controller, TimeProvider time, Func<string, Task>? copy = null)
	{
		this.controller = controller;
		this.time = time;
		this.copy = copy;
	}

	[ObservableProperty]
	public partial IReadOnlyList<DeviceInfoRow> Rows { get; private set; } = [];

	[ObservableProperty]
	public partial string CopyStatus { get; private set; } = "";

	public bool IsConnected => Rows.Count > 0;

	// Reads the Bluetooth address (a feature report) off the UI thread, then fills the rows.
	public async Task LoadAsync()
	{
		if (controller.Session is { } session && session != addressFor && !reading)
		{
			reading = true;
			address = await Task.Run(session.ReadBluetoothAddress);
			addressFor = session;
			reading = false;
		}
		Refresh();
	}

	public void Refresh()
	{
		ControllerSession? session = controller.Session;
		Rows = session == null ? [] : RowsFor(session, session == addressFor ? address : null);
		OnPropertyChanged(nameof(IsConnected));
		CopyCommand.NotifyCanExecuteChanged();
	}

	private List<DeviceInfoRow> RowsFor(ControllerSession session, long? bluetoothAddress)
	{
		List<DeviceInfoRow> rows = [new("Connection", session.Transport == Transport.Bluetooth ? "Bluetooth" : "USB")];
		if (session.LinkQuality is { } link)
		{
			rows.Add(new("Report rate", $"{link.ReportsPerSecond:0} Hz"));
			if (link.LossPercent is { } loss)
			{
				rows.Add(new("Lost reports", $"{loss:0.#}%"));
			}
		}
		if (bluetoothAddress is { } mac)
		{
			rows.Add(new("Bluetooth address", BluetoothPower.FormatMacAddress(mac)));
		}
		DualSenseBattery battery = session.Battery;
		string state = battery.State switch
		{
			DualSenseChargingState.Charging => ", charging",
			DualSenseChargingState.Full => ", full",
			_ => ""
		};
		rows.Add(new("Battery", $"{battery.Level}%{state}"));
		if (session.Firmware is { } firmware)
		{
			rows.Add(new("Firmware", firmware.UpdateVersionText));
			rows.Add(new("Firmware build", firmware.BuildDate));
			rows.Add(new("Hardware", $"0x{firmware.HardwareVersion:X4}"));
		}
		rows.Add(new("Connected for", Duration(time.GetUtcNow().UtcDateTime - session.ConnectedAtUtc)));
		return rows;
	}

	private bool CanCopy() => copy != null && Rows.Count > 0;

	[RelayCommand(CanExecute = nameof(CanCopy))]
	private async Task Copy()
	{
		try
		{
			await copy!(string.Join(Environment.NewLine, Rows.Select(r => $"{r.Label}: {r.Value}")));
			CopyStatus = "Copied.";
		}
		catch (Exception)
		{
			CopyStatus = "Couldn't copy. Try again.";
		}
	}

	public static string Duration(TimeSpan span)
	{
		if (span.TotalMinutes < 1)
		{
			return "under a minute";
		}
		if (span.TotalHours < 1)
		{
			return $"{(int)span.TotalMinutes} min";
		}
		if (span.TotalDays < 1)
		{
			return span.Minutes == 0 ? $"{(int)span.TotalHours} h" : $"{(int)span.TotalHours} h {span.Minutes} min";
		}
		return span.Hours == 0 ? $"{(int)span.TotalDays} d" : $"{(int)span.TotalDays} d {span.Hours} h";
	}
}
