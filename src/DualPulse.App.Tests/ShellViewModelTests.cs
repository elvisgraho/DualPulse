using DualPulse.App.ViewModels;
using Xunit;

namespace DualPulse.App.Tests;

public class ShellViewModelTests
{
	[Fact]
	public void Shell_StartsOnHome_NavigatesBetweenPages()
	{
		ShellViewModel shell = new();

		Assert.Equal(Page.Home, shell.CurrentPage);
		shell.NavigateCommand.Execute(Page.Settings);
		Assert.Equal(Page.Settings, shell.CurrentPage);
		shell.NavigateCommand.Execute(Page.Controller);
		Assert.True(shell.IsController);
		shell.NavigateCommand.Execute(Page.Mapping);
		Assert.True(shell.IsMapping);
		shell.NavigateCommand.Execute(Page.Emulation);
		Assert.True(shell.IsEmulation);
		shell.NavigateCommand.Execute(Page.Mods);
		Assert.True(shell.IsMods);
		Assert.False(shell.IsController);
	}

	[Fact]
	public void ControllerPage_StartsOnLighting_ChipsSwitchSections()
	{
		ControllerPageViewModel page = new();

		Assert.True(page.IsLighting);
		page.SelectCommand.Execute(ControllerSection.Triggers);
		Assert.True(page.IsTriggers);
		Assert.False(page.IsLighting);
		page.SelectCommand.Execute(ControllerSection.Rumble);
		Assert.True(page.IsRumble);
		Assert.Equal(ControllerSection.Rumble, page.CurrentSection);
	}
}
