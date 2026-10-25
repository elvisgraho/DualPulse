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
}
