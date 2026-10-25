using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace DualPulse.App.Services;

// The newest published release: its version (from a tag like v0.2.0) and its download page.
public sealed record ReleaseInfo(Version Version, string Url)
{
	// GitHub's "latest release" answer; null when it can't be read or the tag isn't a version.
	public static ReleaseInfo? Parse(string json)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (!root.TryGetProperty("tag_name", out JsonElement tag) || !root.TryGetProperty("html_url", out JsonElement url))
			{
				return null;
			}
			string text = (tag.GetString() ?? "").TrimStart('v', 'V').Split('-', '+')[0];
			return Version.TryParse(text, out Version? version) && url.GetString() is { } page ? new ReleaseInfo(version, page) : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}

// Where the latest release is looked up; null when there is no answer.
public interface IReleaseFeed
{
	Task<string?> LatestAsync(CancellationToken cancellationToken);
}

public interface IUpdateService
{
	// A release newer than this build, once one has been found.
	ReleaseInfo? Available { get; }

	event Action? AvailableChanged;
}

// Opt-in (CheckForUpdates): shortly after start and then once a day, asks GitHub for the latest release and, when it is
// newer than this build, says so once with a pop-up and on the Settings page. Nothing is downloaded.
public sealed class UpdateService : IHostedService, IUpdateService, IDisposable
{
	public static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(30);

	public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

	private readonly ISettingsService settings;

	private readonly IReleaseFeed feed;

	private readonly INotifier notifier;

	private readonly Version current;

	private readonly ITimer timer;

	private readonly CancellationTokenSource stopping = new();

	private ReleaseInfo? available;

	private bool wasOn;

	public UpdateService(ISettingsService settings, IReleaseFeed feed, INotifier notifier, TimeProvider time, Version current)
	{
		this.settings = settings;
		this.feed = feed;
		this.notifier = notifier;
		this.current = current;
		wasOn = settings.Current.CheckForUpdates;
		settings.Changed += OnSettingsChanged;
		timer = time.CreateTimer(_ => _ = CheckAsync(), null, FirstCheck, Interval);
	}

	public ReleaseInfo? Available => Volatile.Read(ref available);

	public event Action? AvailableChanged;

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken)
	{
		Dispose();
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		settings.Changed -= OnSettingsChanged;
		timer.Dispose();
		stopping.Cancel();
	}

	// Turned on: checks now rather than at the next daily check.
	private void OnSettingsChanged(AppSettings current)
	{
		bool turnedOn = current.CheckForUpdates && !wasOn;
		wasOn = current.CheckForUpdates;
		if (turnedOn)
		{
			_ = CheckAsync();
		}
	}

	public async Task CheckAsync()
	{
		if (!settings.Current.CheckForUpdates)
		{
			return;
		}
		string? json;
		try
		{
			json = await feed.LatestAsync(stopping.Token).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
		{
			return;
		}
		if (json == null || ReleaseInfo.Parse(json) is not { } release || release.Version <= current
			|| (Available is { } known && release.Version <= known.Version))
		{
			return;
		}
		Volatile.Write(ref available, release);
		notifier.Show($"DualPulse {release.Version} is out", "Download it from Settings.");
		AvailableChanged?.Invoke();
	}
}

// GitHub's public API for the project's latest release (drafts and pre-releases are left out there).
public sealed class GitHubReleaseFeed : IReleaseFeed, IDisposable
{
	private const string LatestUrl = "https://api.github.com/repos/elvisgraho/DualPulse/releases/latest";

	private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };

	public GitHubReleaseFeed(Version current)
	{
		// GitHub refuses requests without a user agent.
		client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DualPulse", current.ToString()));
		client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
	}

	public async Task<string?> LatestAsync(CancellationToken cancellationToken)
	{
		using HttpResponseMessage response = await client.GetAsync(LatestUrl, cancellationToken).ConfigureAwait(false);
		return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : null;
	}

	public void Dispose() => client.Dispose();
}
