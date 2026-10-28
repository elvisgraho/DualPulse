using DualPulse.Core.Steam;
using Xunit;

namespace DualPulse.Core.Tests;

public sealed class SteamLibraryTests
{
	private const string LibraryFolders = """
		"libraryfolders"
		{
			"0"
			{
				"path"		"C:\Program Files (x86)\Steam"
				"label"		""
				"apps"
				{
					"1605220"		"6119447691"
				}
			}
			"1"
			{
				"path"		"D:\SteamLibrary"
			}
		}
		""";

	private const string Manifest = """
		"AppState"
		{
			"appid"		"1605220"
			"Universe"		"1"
			"name"		"Dune: Spice Wars"
			"StateFlags"		"4"
			"installdir"		"D4X"
		}
		""";

	[Fact]
	public void LibraryPaths_AreReadWithBackslashesUnescaped()
	{
		Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamLibrary.LibraryPaths(LibraryFolders));
	}

	[Fact]
	public void Manifest_GivesIdNameAndFolder()
	{
		Assert.Equal(new SteamApp(1605220, "Dune: Spice Wars", "D4X"), SteamLibrary.ParseManifest(Manifest));
	}

	[Fact]
	public void BrokenManifest_IsNull()
	{
		Assert.Null(SteamLibrary.ParseManifest("\"AppState\" { \"name\" \"No id\" }"));
	}

	[Fact]
	public void GameFor_FindsTheGameWhoseFolderHoldsTheExe()
	{
		SteamApp dune = new(1605220, "Dune: Spice Wars", "D4X");
		SteamApp other = new(1, "Other", "D4");
		(string, SteamApp)[] installed = [(@"D:\SteamLibrary", other), (@"C:\Program Files (x86)\Steam", dune)];

		Assert.Equal(dune, SteamLibrary.GameFor(@"c:\program files (x86)\steam\steamapps\common\d4x\bin\D4X.exe", installed));
		// "D4" must not match a game in "D4X".
		Assert.Null(SteamLibrary.GameFor(@"D:\SteamLibrary\steamapps\common\D4X\game.exe", installed));
		Assert.Null(SteamLibrary.GameFor(@"C:\Games\Rockstar\RDR2.exe", installed));
	}
}
