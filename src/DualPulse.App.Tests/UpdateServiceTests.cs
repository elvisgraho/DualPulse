using DualPulse.App.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace DualPulse.App.Tests;

public sealed class UpdateServiceTests
{
	private const string Latest = """{ "tag_name": "v0.2.0", "html_url": "https://github.com/elvisgraho/DualPulse/releases/tag/v0.2.0", "draft": false }""";

	private readonly FakeSettingsService settings = new();

	private readonly FakeReleaseFeed feed = new();

	private readonly FakeNotifier notifier = new();

	private readonly FakeTimeProvider time = new();

	private UpdateService CreateService(string current = "0.1.0") => new(settings, feed, notifier, time, Version.Parse(current));

	[Fact]
	public void Release_ParsesTheTagAndPage()
	{
		ReleaseInfo? release = ReleaseInfo.Parse(Latest);

		Assert.Equal(new Version(0, 2, 0), release!.Version);
		Assert.Equal("https://github.com/elvisgraho/DualPulse/releases/tag/v0.2.0", release.Url);
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("""{ "tag_name": "nightly", "html_url": "x" }""")]
	[InlineData("""{ "html_url": "x" }""")]
	public void Release_UnreadableAnswers_AreIgnored(string json)
	{
		Assert.Null(ReleaseInfo.Parse(json));
	}

	[Fact]
	public async Task Off_NeverAsks()
	{
		feed.Json = Latest;
		using UpdateService service = CreateService();

		await service.CheckAsync();

		Assert.Equal(0, feed.Requests);
		Assert.Null(service.Available);
	}

	[Fact]
	public async Task On_ANewerRelease_IsShownOnce()
	{
		feed.Json = Latest;
		settings.Update(s => s with { CheckForUpdates = true });
		using UpdateService service = CreateService();

		await service.CheckAsync();
		await service.CheckAsync();

		Assert.Equal(new Version(0, 2, 0), service.Available!.Version);
		Assert.Equal(1, notifier.Count);
		Assert.Contains("0.2.0", notifier.Shown[0].Title);
	}

	[Theory]
	[InlineData("0.2.0")]
	[InlineData("0.3.1")]
	public async Task On_TheSameOrAnOlderRelease_SaysNothing(string current)
	{
		feed.Json = Latest;
		settings.Update(s => s with { CheckForUpdates = true });
		using UpdateService service = CreateService(current);

		await service.CheckAsync();

		Assert.Null(service.Available);
		Assert.Equal(0, notifier.Count);
	}

	[Fact]
	public async Task On_NoAnswer_SaysNothing()
	{
		feed.Json = null;
		settings.Update(s => s with { CheckForUpdates = true });
		using UpdateService service = CreateService();

		await service.CheckAsync();

		Assert.Null(service.Available);
	}

	[Fact]
	public async Task TurningItOn_ChecksRightAway()
	{
		feed.Json = Latest;
		using UpdateService service = CreateService();

		settings.Update(s => s with { CheckForUpdates = true });

		await feed.WaitForRequests(1);
	}

	[Fact]
	public async Task Checks_ShortlyAfterStart_ThenDaily()
	{
		feed.Json = Latest;
		settings.Update(s => s with { CheckForUpdates = true });
		using UpdateService service = CreateService();

		time.Advance(UpdateService.FirstCheck);
		await feed.WaitForRequests(1);
		time.Advance(UpdateService.Interval);
		await feed.WaitForRequests(2);
	}
}

internal sealed class FakeReleaseFeed : IReleaseFeed
{
	private int requests;

	public string? Json { get; set; }

	public int Requests => Volatile.Read(ref requests);

	public Task<string?> LatestAsync(CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref requests);
		return Task.FromResult(Json);
	}

	public async Task WaitForRequests(int count)
	{
		for (int i = 0; i < 200 && Requests < count; i++)
		{
			await Task.Delay(10);
		}
		Assert.True(Requests >= count, $"expected {count} requests, saw {Requests}");
	}
}
