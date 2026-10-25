using DualPulse.App.Logging;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class FileLoggerProviderTests : IDisposable
{
	private readonly string folder = Path.Combine(Path.GetTempPath(), "dualpulse-logs-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(folder))
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void Logger_OldFileLocked_StartsAnyway()
	{
		Directory.CreateDirectory(folder);
		string old = Path.Combine(folder, "dualpulse-20200101.log");
		File.WriteAllText(old, "old");
		File.SetLastWriteTime(old, DateTime.Now.AddDays(-30));
		using FileStream locked = new(old, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

		using FileLoggerProvider provider = new(folder);
	}

	[Fact]
	public void Logger_FullDay_StartsAFreshFileAndKeepsOneOld()
	{
		using FileLoggerProvider provider = new(folder, maxBytes: 1000);
		Microsoft.Extensions.Logging.ILogger logger = provider.CreateLogger("Test");

		for (int i = 0; i < 60; i++)
		{
			Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger, "line {Number} with some padding text", i);
		}

		string[] files = Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
		Assert.Equal([$"dualpulse-{DateTime.Now:yyyyMMdd}-old.log", $"dualpulse-{DateTime.Now:yyyyMMdd}.log"], files);
		Assert.All(Directory.GetFiles(folder), file => Assert.True(new FileInfo(file).Length <= 1100));
		Assert.Contains("line 59", File.ReadAllText(Path.Combine(folder, $"dualpulse-{DateTime.Now:yyyyMMdd}.log")));
	}

	[Fact]
	public void Logger_KeepsOnlyTheNewestFiles()
	{
		Directory.CreateDirectory(folder);
		for (int day = 1; day <= 14; day++)
		{
			string file = Path.Combine(folder, $"dualpulse-202610{day:00}.log");
			File.WriteAllText(file, "x");
			File.SetLastWriteTime(file, DateTime.Now.AddHours(day - 15));
		}

		using FileLoggerProvider provider = new(folder);

		string[] left = Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
		Assert.Equal(FileLoggerProvider.MaxFiles, left.Length);
		Assert.Equal("dualpulse-20261014.log", left[^1]);
		Assert.DoesNotContain("dualpulse-20261001.log", left);
	}
}
