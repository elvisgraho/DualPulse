using Microsoft.Extensions.Logging;

namespace DualPulse.App.Logging;

// Writes logs/dualpulse-yyyyMMdd.log in the data folder and removes log files older than 7 days.
public sealed class FileLoggerProvider : ILoggerProvider
{
	private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

	private readonly object gate = new();

	private readonly string folder;

	public FileLoggerProvider(string folder)
	{
		this.folder = folder;
		Directory.CreateDirectory(folder);
		foreach (string file in Directory.GetFiles(folder, "dualpulse-*.log"))
		{
			try
			{
				if (DateTime.Now - File.GetLastWriteTime(file) > Retention)
				{
					File.Delete(file);
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
				File.AppendAllText(Path.Combine(folder, $"dualpulse-{DateTime.Now:yyyyMMdd}.log"), line);
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
