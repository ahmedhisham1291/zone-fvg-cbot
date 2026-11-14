using System.Text.Json;
using Xunit;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-005: a download is accepted only if it parses, contains events and covers the current
/// time; a rejected download never replaces accepted data. NWS-006: each source is fetched at
/// start and every NewsRefreshHours, never more often than once per 60 minutes, also across
/// restarts with the same store. T0 is the sample's Wednesday, 2026-11-11 12:00 UTC.
/// </summary>
public class NewsServiceDownloadTests
{
    private static readonly DateTime T0 = SampleWednesday;

    [Fact]
    [Trait("Req", "NWS-005")]
    public void Refresh_GoodDownload_IsAcceptedAndUsed()
    {
        var http = new FakeTransport(Ok(Sample));
        NewsService service = LiveService(new MemoryStore(), http);

        NewsUpdate update = service.Refresh(T0);

        Assert.Equal(NewsStatus.Available, update.Status);
        NewsFeedReport fetch = Assert.Single(update.Reports, r => r.Outcome == NewsFeedOutcome.DownloadAccepted);
        Assert.Equal("ForexFactory", fetch.Source);
        Assert.Equal(106, fetch.Events.Count);
        Assert.Equal("Download accepted: 106 events.", fetch.Detail);
        Assert.Equal(106, update.Calendar.Events.Count);
        Assert.Equal(new[] { FeedUrl }, http.Urls);
        Assert.Equal(new[] { NewsService.HttpTimeoutSeconds }, http.Timeouts);
    }

    [Theory]
    [Trait("Req", "NWS-005")]
    [InlineData("failed", "Download failed (status 503); earlier data kept.")]
    [InlineData("html", "Download rejected: The document is an HTML page")]
    [InlineData("empty", "Download rejected: The document contains no events.")]
    [InlineData("truncated", "Download rejected: The document is not valid JSON")]
    [InlineData("stale", "Download rejected: The document has no event in the current week")]
    public void Refresh_BadDownloadAfterAGoodOne_KeepsTheGoodData(string kind, string detail)
    {
        var store = new MemoryStore();
        var http = new FakeTransport(Ok(Sample));
        NewsService service = LiveService(store, http);
        service.Refresh(T0);
        IReadOnlyList<NewsWindow> before = service.CalendarAt(T0).WindowsFor("EURUSD");
        string cached = store.Values["news.ForexFactory.data"];
        http.Respond = _ => kind switch
        {
            "failed" => Failed(503),
            "html" => Ok("<html><body>Request Denied</body></html>"),
            "empty" => Ok("[]"),
            "truncated" => Ok(Sample.Substring(0, 1000)),
            _ => Ok(OneEventDocument("Old", "USD", "2026-11-03T08:30:00-05:00", "High")),
        };

        NewsUpdate update = service.Refresh(T0.AddHours(4));

        Assert.Equal(2, http.Calls);
        NewsFeedReport report = Assert.Single(update.Reports);
        Assert.Equal(NewsFeedOutcome.DownloadRejected, report.Outcome);
        Assert.StartsWith(detail, report.Detail, StringComparison.Ordinal);
        Assert.Empty(report.Events);
        Assert.Equal(NewsStatus.Available, update.Status);
        Assert.Equal(before, service.CalendarAt(T0.AddHours(4)).WindowsFor("EURUSD"));
        Assert.Equal(cached, store.Values["news.ForexFactory.data"]);
    }

    [Fact]
    [Trait("Req", "NWS-005")]
    public void Refresh_ValidDocumentNotCoveringNow_IsRejected()
    {
        // A parser whose document is valid but covers only last week: NewsService checks coverage itself.
        var parser = new ListParser("ForexFactory", new[] { Event("USD", T0.AddDays(-7)) }, Utc(2026, 11, 2, 0, 0), Utc(2026, 11, 9, 0, 0));
        var service = new NewsService(Parameters(), new[] { new NewsFeed(FeedUrl, parser) }, new MemoryStore(), new FakeTransport(Ok("ok")), false, string.Empty);

        NewsUpdate update = service.Refresh(T0);

        NewsFeedReport report = Assert.Single(update.Reports, r => r.Outcome == NewsFeedOutcome.DownloadRejected);
        Assert.Equal("Download rejected: the document does not cover the current time. Earlier data kept.", report.Detail);
        Assert.Equal(NewsStatus.Unavailable, update.Status);
    }

    [Fact]
    [Trait("Req", "NWS-005")]
    public void Refresh_LaterGoodDownload_ReplacesTheEarlierData()
    {
        var store = new MemoryStore();
        var http = new FakeTransport(Ok(OneEventDocument("First", "USD", "2026-11-11T08:30:00-05:00", "High")));
        NewsService service = LiveService(store, http);
        service.Refresh(T0);
        http.Respond = _ => Ok(Sample);

        NewsUpdate update = service.Refresh(T0.AddHours(4));

        Assert.Equal(NewsFeedOutcome.DownloadAccepted, Assert.Single(update.Reports).Outcome);
        Assert.Equal(106, update.Calendar.Events.Count);
        Assert.True(update.CalendarChanged);
        Assert.Contains("CPI m/m", store.Values["news.ForexFactory.data"], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "NWS-005")]
    public void Refresh_FirstDownloadFails_NoDataAndUnavailable()
    {
        NewsService service = LiveService(new MemoryStore(), new FakeTransport(Failed(0)));

        NewsUpdate update = service.Refresh(T0);

        Assert.Equal(NewsStatus.Unavailable, update.Status);
        Assert.Contains(update.Reports, r => r.Detail == "Download failed (status 0); earlier data kept.");
        Assert.Empty(update.Calendar.Events);
    }

    [Fact]
    [Trait("Req", "NWS-005")]
    public void Refresh_TransportReturnsNull_IsAFailedDownload()
    {
        var http = new FakeTransport(Failed()) { Respond = _ => null! };
        NewsService service = LiveService(new MemoryStore(), http);

        NewsUpdate update = service.Refresh(T0);

        Assert.Contains(update.Reports, r => r.Detail == "Download failed (no result); earlier data kept.");
        Assert.Equal(NewsStatus.Unavailable, update.Status);
    }

    [Fact]
    [Trait("Req", "NWS-005")]
    public void Refresh_OkResultWithNullBody_IsRejected()
    {
        var http = new FakeTransport(new HttpResult(true, 200, null!));
        NewsService service = LiveService(new MemoryStore(), http);

        NewsUpdate update = service.Refresh(T0);

        Assert.Contains(update.Reports, r => r.Detail == "Download rejected: The document is empty. Earlier data kept.");
    }

    [Fact]
    [Trait("Req", "NWS-006")]
    public void Refresh_Start_FetchesOnceAndNotAgainWithin4Hours()
    {
        var http = new FakeTransport(Ok(Sample));
        NewsService service = LiveService(new MemoryStore(), http);

        service.Refresh(T0);
        service.Refresh(T0.AddMinutes(1));
        service.Refresh(T0.AddMinutes(59));
        service.Refresh(T0.AddMinutes(60));
        NewsUpdate quiet = service.Refresh(T0.AddHours(4).AddMinutes(-1));

        Assert.Equal(1, http.Calls);
        Assert.Empty(quiet.Reports);
    }

    [Fact]
    [Trait("Req", "NWS-006")]
    public void Refresh_AfterNewsRefreshHours_FetchesAgain()
    {
        var http = new FakeTransport(Ok(Sample));
        NewsService service = LiveService(new MemoryStore(), http);
        service.Refresh(T0);

        service.Refresh(T0.AddHours(4));
        service.Refresh(T0.AddHours(8).AddSeconds(-1));
        service.Refresh(T0.AddHours(8));

        Assert.Equal(3, http.Calls);
    }

    [Fact]
    [Trait("Req", "NWS-006")]
    public void Refresh_CustomRefreshHours_AreUsed()
    {
        var http = new FakeTransport(Ok(Sample));
        NewsService service = LiveService(new MemoryStore(), http, new NewsParameters { NewsRefreshHours = 2 });
        service.Refresh(T0);

        service.Refresh(T0.AddHours(2).AddSeconds(-1));
        Assert.Equal(1, http.Calls);
        service.Refresh(T0.AddHours(2));
        Assert.Equal(2, http.Calls);
    }

    [Fact]
    [Trait("Req", "NWS-006")]
    public void Refresh_FailedAttempts_AlsoCountForTheInterval()
    {
        var http = new FakeTransport(Failed());
        NewsService service = LiveService(new MemoryStore(), http);

        service.Refresh(T0);
        service.Refresh(T0.AddMinutes(30));
        service.Refresh(T0.AddHours(4).AddSeconds(-1));

        Assert.Equal(1, http.Calls);
    }

    [Theory]
    [Trait("Req", "NWS-006")]
    [InlineData(1, 0)]
    [InlineData(59, 0)]
    [InlineData(60, 1)]
    [InlineData(61, 1)]
    public void Refresh_RestartWithTheSameStore_NoFetchWithin60Minutes(int minutesLater, int expectedCalls)
    {
        var store = new MemoryStore();
        LiveService(store, new FakeTransport(Ok(Sample))).Refresh(T0);
        var http = new FakeTransport(Ok(Sample));

        LiveService(store, http).Refresh(T0.AddMinutes(minutesLater));

        Assert.Equal(expectedCalls, http.Calls);
    }

    [Fact]
    [Trait("Req", "NWS-006")]
    public void Refresh_RestartLoop_NeverFetchesMoreThanOncePer60Minutes()
    {
        // A new instance every 10 minutes for 3 hours: fetches at T0, T0+60, T0+120 and T0+180 only.
        var store = new MemoryStore();
        var http = new FakeTransport(Ok(Sample));
        for (int minute = 0; minute <= 180; minute += 10)
        {
            LiveService(store, http).Refresh(T0.AddMinutes(minute));
        }

        Assert.Equal(4, http.Calls);
    }

    [Fact]
    [Trait("Req", "NWS-006")]
    public void Refresh_RestartInsideTheHour_NextFetchAfterRefreshHoursFromTheLastAttempt()
    {
        var store = new MemoryStore();
        LiveService(store, new FakeTransport(Ok(Sample))).Refresh(T0);
        var http = new FakeTransport(Ok(Sample));
        NewsService restarted = LiveService(store, http);

        restarted.Refresh(T0.AddMinutes(30));
        restarted.Refresh(T0.AddMinutes(90));
        restarted.Refresh(T0.AddHours(4).AddSeconds(-1));
        Assert.Equal(0, http.Calls);
        restarted.Refresh(T0.AddHours(4));
        Assert.Equal(1, http.Calls);
    }

    [Fact]
    [Trait("Req", "NWS-006")]
    public void Refresh_AttemptTimeIsStoredBeforeTheRequestAsJsonUnderANewsKey()
    {
        var store = new MemoryStore();
        string? storedDuringRequest = null;
        var http = new FakeTransport(Failed());
        http.Respond = _ =>
        {
            storedDuringRequest = store.Values.GetValueOrDefault("news.ForexFactory.lastAttempt");
            return Failed();
        };

        LiveService(store, http).Refresh(T0);

        Assert.Equal("{\"attemptUtc\":\"2026-11-11T12:00:00.0000000Z\"}", storedDuringRequest);
        Assert.All(store.Values.Keys, key => Assert.StartsWith("news.", key, StringComparison.Ordinal));
        Assert.All(store.Values.Values, value => JsonDocument.Parse(value).Dispose());
    }

    [Fact]
    [Trait("Req", "NWS-006")]
    public void Refresh_StoredAttemptInTheFuture_ReportedAtStartAndWaitsForTheIntervalFromIt()
    {
        var store = new MemoryStore();
        LiveService(store, new FakeTransport(Ok(Sample))).Refresh(T0.AddHours(2));
        var http = new FakeTransport(Ok(Sample));
        NewsService service = LiveService(store, http);

        NewsUpdate start = service.Refresh(T0);
        NewsUpdate later = service.Refresh(T0.AddHours(1));
        service.Refresh(T0.AddHours(6).AddSeconds(-1));
        Assert.Equal(0, http.Calls);
        service.Refresh(T0.AddHours(6));

        Assert.Equal(1, http.Calls);
        Assert.Contains(start.Reports, r => r.Outcome == NewsFeedOutcome.AttemptTimeInFuture && r.Detail == "Last attempt stored as 2026-11-11 14:00:00 UTC, after server time 2026-11-11 12:00:00 UTC.");
        Assert.DoesNotContain(later.Reports, r => r.Outcome == NewsFeedOutcome.AttemptTimeInFuture);
    }

    [Theory]
    [Trait("Req", "NWS-006")]
    [InlineData("garbage")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"attemptUtc\":12}")]
    [InlineData("{\"attemptUtc\":\"2026-11-11 12:00\"}")]
    [InlineData("{\"attemptUtc\":\"2026-11-11T12:00:00.0000000\"}")]
    [InlineData("{\"attemptUtc\":\"2026-11-11T12:00:00.0000000+01:00\"}")]
    public void Refresh_UnreadableStoredAttempt_IsReportedAndTheSourceIsFetched(string stored)
    {
        var store = new MemoryStore();
        store.Values["news.ForexFactory.lastAttempt"] = stored;
        var http = new FakeTransport(Ok(Sample));

        NewsUpdate update = LiveService(store, http).Refresh(T0);

        Assert.Equal(1, http.Calls);
        NewsFeedReport report = Assert.Single(update.Reports, r => r.Outcome == NewsFeedOutcome.DownloadAccepted);
        Assert.StartsWith("Stored attempt time ignored: ", report.Detail, StringComparison.Ordinal);
        Assert.EndsWith("Download accepted: 106 events.", report.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "NWS-006")]
    public void Refresh_TwoFeeds_EachHasItsOwnAttemptTime()
    {
        var store = new MemoryStore();
        var second = new ListParser("Second", new[] { Event("USD", T0, source: "Second") }, Utc(2026, 11, 9, 0, 0), Utc(2026, 11, 16, 0, 0));
        var http = new FakeTransport(Ok(Sample));
        var service = new NewsService(Parameters(), new[] { ForexFactoryFeed(), new NewsFeed("https://second.invalid/calendar", second) }, store, http, false, string.Empty);

        service.Refresh(T0);

        Assert.Equal(new[] { FeedUrl, "https://second.invalid/calendar" }, http.Urls);
        Assert.True(store.Values.ContainsKey("news.ForexFactory.lastAttempt"));
        Assert.True(store.Values.ContainsKey("news.Second.lastAttempt"));
    }
}
