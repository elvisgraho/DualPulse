using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DualPulse.App.ViewModels;

public enum Page
{
	Home,
	Controller,
	Mapping,
	Emulation,
	Mods,
	Settings
}

public sealed partial class ShellViewModel : ObservableObject
{
	public ShellViewModel(HomeViewModel? home = null, SettingsViewModel? settingsPage = null, ControllerPageViewModel? controllerPage = null,
		EmulationViewModel? emulation = null, ModsViewModel? mods = null, MappingViewModel? mapping = null)
	{
		Home = home;
		Mapping = mapping;
		ControllerPage = controllerPage;
		Emulation = emulation;
		Mods = mods;
		SettingsPage = settingsPage;
		if (home != null)
		{
			StatusText = home.ConnectionText;
			home.PropertyChanged += (_, e) =>
			{
				if (e.PropertyName == nameof(HomeViewModel.ConnectionText))
				{
					StatusText = home.ConnectionText;
				}
			};
		}
	}

	public HomeViewModel? Home { get; }

	public SettingsViewModel? SettingsPage { get; }

	// Lighting, Triggers and Rumble: what the controller itself does.
	public ControllerPageViewModel? ControllerPage { get; }

	// PC control: the controller as mouse and keyboard.
	public MappingViewModel? Mapping { get; }

	public EmulationViewModel? Emulation { get; }

	public ModsViewModel? Mods { get; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsHome), nameof(IsController), nameof(IsMapping), nameof(IsEmulation), nameof(IsMods), nameof(IsSettings))]
	public partial Page CurrentPage { get; set; } = Page.Home;

	[ObservableProperty]
	public partial string StatusText { get; set; } = "Not connected";

	public bool IsHome => CurrentPage == Page.Home;

	public bool IsController => CurrentPage == Page.Controller;

	public bool IsMapping => CurrentPage == Page.Mapping;

	public bool IsEmulation => CurrentPage == Page.Emulation;

	public bool IsMods => CurrentPage == Page.Mods;

	public bool IsSettings => CurrentPage == Page.Settings;

	[RelayCommand]
	private void Navigate(Page page)
	{
		CurrentPage = page;
	}
}
