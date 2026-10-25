using Microsoft.Extensions.Logging;

namespace DualPulse.App.Logging;

// Writes logs/dualpulse-yyyyMMdd.log in the data folder, kept small: a day's file that reaches maxBytes becomes
// dualpulse-yyyyMMdd-old.log (replacing an older one) and a fresh file starts, and at start files older than 7 days,
// and all but the newest MaxFiles, are removed.
public sealed class FileLoggerProvider : ILoggerProvider
{
	public const int MaxFiles = 10;

	private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

	private readonly object gate = new();

	private readonly string folder;

	private readonly long maxBytes;

	public FileLoggerProvider(string folder, long maxBytes = 2 * 1024 * 1024)
	{
		this.folder = folder;
		this.maxBytes = maxBytes;
		Directory.CreateDirectory(folder);
		IEnumerable<FileInfo> newestFirst = new DirectoryInfo(folder).GetFiles("dualpulse-*.log").OrderByDescending(file => file.LastWriteTime);
		int kept = 0;
		foreach (FileInfo file in newestFirst)
		{
			try
			{
				if (++kept > MaxFiles || DateTime.Now - file.LastWriteTime > Retention)
				{
					file.Delete();
				}
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException)
			{
				// Still open elsewhere (e.g. a second instance); removed on a later start.
			}
		}
	}

	public ILogger CreateLogger(string categoryName)
	{
		return new FileLogger(this, categoryName.Split('.')[^1]);
	}

	public void Dispose()
	{
	}

	private void Write(LogLevel level, string category, string message, Exception? exception)
	{
		string line = $"{DateTime.Now:HH:mm:ss.fff} {level.ToString()[..4].ToUpperInvariant()} {category}: {message}{(exception == null ? "" : Environment.NewLine + exception)}{Environment.NewLine}";
		lock (gate)
		{
			try
			{
				string day = $"dualpulse-{DateTime.Now:yyyyMMdd}";
				string current = Path.Combine(folder, day + ".log");
				FileInfo info = new(current);
				if (info.Exists && info.Length + line.Length > maxBytes)
				{
					File.Move(current, Path.Combine(folder, day + "-old.log"), overwrite: true);
				}
				File.AppendAllText(current, line);
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException)
			{
				// Logging must never take the app down.
			}
		}
	}

	private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		{
			if (IsEnabled(logLevel))
			{
				provider.Write(logLevel, category, formatter(state, exception), exception);
			}
		}
	}
}
