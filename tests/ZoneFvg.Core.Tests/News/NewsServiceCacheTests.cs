using Xunit;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-007: the last accepted data of each source is stored with its fetch time through the
/// key-value port and reloaded at start; data older than NewsMaxCacheHours (48) without a
/// successful refresh is not trusted. T0 is 2026-11-11 12:00 UTC.
/// </summary>
public class NewsServiceCacheTests
{
    private const string DataKey = "news.ForexFactory.data";
    private const string GoodEvent = "{\"timeUtc\":\"2026-11-10T13:30:00.0000000Z\",\"currency\":\"USD\",\"impact\":\"High\",\"title\":\"CPI\",\"source\":\"ForexFactory\"}";
    private static readonly DateTime T0 = SampleWednesday;

    private static MemoryStore StoreFetchedAt(DateTime fetchUtc)
    {
        var store = new MemoryStore();
        NewsUpdate update = LiveService(store, new FakeTransport(Ok(Sample))).Refresh(fetchUtc);
        Assert.Contains(update.Reports, r => r.Outcome == NewsFeedOutcome.DownloadAccepted);
        return store;
    }

    private static string CacheJson(string fetched, string start, string end, string events)
    {
        return "{\"fetchedUtc\":\"" + fetched + "\",\"coverageStartUtc\":\"" + start + "\",\"coverageEndUtc\":\"" + end + "\",\"events\":" + events + "}";
    }

    private static string GoodCache(string events = "[" + GoodEvent + "]")
    {
        return CacheJson("2026-11-11T12:00:00.0000000Z", "2026-11-09T00:00:00.0000000Z", "2026-11-16T00:00:00.0000000Z", events);
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Restart_CacheRoundTrip_GivesTheSameEventsAndWindows()
    {
        var store = new MemoryStore();
        NewsService first = LiveService(store, new FakeTransport(Ok(Sample)));
        first.Refresh(T0);
        var http = new FakeTransport(Failed());
        NewsService second = LiveService(store, http);

        NewsUpdate update = second.Refresh(T0.AddMinutes(30));

        Assert.Equal(0, http.Calls);
        NewsFeedReport report = Assert.Single(update.Reports);
        Assert.Equal(NewsFeedOutcome.CacheLoaded, report.Outcome);
        Assert.Equal("Cached data fetched at 2026-11-11 12:00:00 UTC.", report.Detail);
        Assert.Equal(106, report.Events.Count);
        Assert.Equal(NewsStatus.Available, update.Status);
        Assert.Equal(first.CalendarAt(T0).Events, second.CalendarAt(T0.AddMinutes(30)).Events);
        Assert.Equal(first.CalendarAt(T0).WindowsFor("GBPUSD"), second.CalendarAt(T0.AddMinutes(30)).WindowsFor("GBPUSD"));
        Assert.Equal(first.CalendarAt(T0).EventsWithoutWindow, second.CalendarAt(T0.AddMinutes(30)).EventsWithoutWindow);
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Cache_EventsKeepTimesImpactsTitlesAndSources()
    {
        var store = new MemoryStore();
        store.Values[DataKey] = GoodCache("[" + GoodEvent + ",{\"timeUtc\":null,\"currency\":\"EUR\",\"impact\":\"Holiday\",\"title\":\"\",\"source\":\"A+B\"}"
            + ",{\"timeUtc\":\"2026-11-12T07:00:00.0000000Z\",\"currency\":\"GBP\",\"impact\":\"Unknown\",\"title\":\"x\",\"source\":\"S\"}]");

        NewsUpdate update = LiveService(store, new FakeTransport(Failed())).Refresh(T0);

        Assert.Equal(
            new[]
            {
                new NewsEvent(null, "EUR", NewsImpact.Holiday, string.Empty, "A+B"),
                new NewsEvent(Utc(2026, 11, 10, 13, 30), "USD", NewsImpact.High, "CPI", "ForexFactory"),
                new NewsEvent(Utc(2026, 11, 12, 7, 0), "GBP", NewsImpact.Unknown, "x", "S"),
            },
            update.Calendar.Events);
        Assert.Equal(NewsStatus.Available, update.Status);
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Cache_NothingStored_IsReported()
    {
        NewsUpdate update = LiveService(new MemoryStore(), new FakeTransport(Failed())).Refresh(T0);

        Assert.Contains(update.Reports, r => r.Outcome == NewsFeedOutcome.CacheMissing && r.Detail == "No cached data.");
    }

    [Theory]
    [Trait("Req", "NWS-007")]
    [InlineData(48 * 60, NewsStatus.Available)]
    [InlineData((48 * 60) - 1, NewsStatus.Available)]
    [InlineData((48 * 60) + 1, NewsStatus.Unavailable)]
    public void Cache_AgeAroundNewsMaxCacheHours_TrustedUpTo48Hours(int minutesOld, NewsStatus expected)
    {
        // Fetched Wednesday 12:00; 48 hours later is Friday 12:00, still in the covered week.
        MemoryStore store = StoreFetchedAt(T0);

        NewsUpdate update = LiveService(store, new FakeTransport(Failed())).Refresh(T0.AddMinutes(minutesOld));

        Assert.Equal(expected, update.Status);
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Cache_OneSecondPast48Hours_IsNotTrusted()
    {
        MemoryStore store = StoreFetchedAt(T0);
        NewsService service = LiveService(store, new FakeTransport(Failed()));

        Assert.Equal(NewsStatus.Available, service.Refresh(T0.AddHours(48)).Status);
        Assert.Equal(NewsStatus.Unavailable, service.StatusAt(T0.AddHours(48).AddSeconds(1)));
        Assert.Empty(service.CalendarAt(T0.AddHours(48).AddSeconds(1)).Events);
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Cache_CustomMaxCacheHours_IsUsed()
    {
        MemoryStore store = StoreFetchedAt(T0);
        NewsService service = LiveService(store, new FakeTransport(Failed()), new NewsParameters { NewsMaxCacheHours = 24 });

        service.Refresh(T0.AddHours(1));

        Assert.Equal(NewsStatus.Available, service.StatusAt(T0.AddHours(24)));
        Assert.Equal(NewsStatus.Unavailable, service.StatusAt(T0.AddHours(24).AddSeconds(1)));
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    [Trait("Req", "NWS-008")]
    public void Cache_YoungButCoverageEnded_IsNotTrusted()
    {
        // Fetched Sunday 2026-11-15 12:00, which is still in the sample's week. At Monday 00:00 the
        // week has ended, although the data is only 12 hours old.
        MemoryStore store = StoreFetchedAt(Utc(2026, 11, 15, 12, 0));
        NewsService service = LiveService(store, new FakeTransport(Failed()));
        service.Refresh(Utc(2026, 11, 15, 13, 0));

        Assert.Equal(NewsStatus.Available, service.StatusAt(Utc(2026, 11, 15, 23, 59, 59)));
        Assert.Equal(NewsStatus.Unavailable, service.StatusAt(Utc(2026, 11, 16, 0, 0)));
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Cache_FetchedAfterServerTime_IsNotTrusted()
    {
        var store = new MemoryStore();
        store.Values[DataKey] = CacheJson("2026-11-11T13:00:00.0000000Z", "2026-11-09T00:00:00.0000000Z", "2026-11-16T00:00:00.0000000Z", "[" + GoodEvent + "]");
        NewsService service = LiveService(store, new FakeTransport(Failed()));

        Assert.Equal(NewsStatus.Unavailable, service.Refresh(T0).Status);
        Assert.Equal(NewsStatus.Available, service.StatusAt(T0.AddHours(1)));
    }

    [Theory]
    [Trait("Req", "NWS-007")]
    [InlineData("garbage")]
    [InlineData("[]")]
    [InlineData("{\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"events\":[]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-16T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\"}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":{}}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[1]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"currency\":\"USD\",\"impact\":\"High\",\"title\":\"CPI\",\"source\":\"S\"}]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"timeUtc\":\"2026-11-10 13:30\",\"currency\":\"USD\",\"impact\":\"High\",\"title\":\"CPI\",\"source\":\"S\"}]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"timeUtc\":null,\"impact\":\"High\",\"title\":\"CPI\",\"source\":\"S\"}]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"timeUtc\":null,\"currency\":\"USD\",\"title\":\"CPI\",\"source\":\"S\"}]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"timeUtc\":null,\"currency\":\"USD\",\"impact\":\"High\",\"source\":\"S\"}]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"timeUtc\":null,\"currency\":\"USD\",\"impact\":\"High\",\"title\":\"CPI\"}]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"timeUtc\":null,\"currency\":\"USD\",\"impact\":\"Severe\",\"title\":\"CPI\",\"source\":\"S\"}]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"timeUtc\":null,\"currency\":\"USD\",\"impact\":\"high\",\"title\":\"CPI\",\"source\":\"S\"}]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"timeUtc\":null,\"currency\":\"USD\",\"impact\":\"0\",\"title\":\"CPI\",\"source\":\"S\"}]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"timeUtc\":null,\"currency\":\" \",\"impact\":\"High\",\"title\":\"CPI\",\"source\":\"S\"}]}")]
    [InlineData("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\",\"coverageStartUtc\":\"2026-11-09T00:00:00.0000000Z\",\"coverageEndUtc\":\"2026-11-16T00:00:00.0000000Z\",\"events\":[{\"timeUtc\":null,\"currency\":\"USD\",\"impact\":\"High\",\"title\":\"CPI\",\"source\":\"\"}]}")]
    public void Cache_Unreadable_IsIgnoredAndReported(string stored)
    {
        var store = new MemoryStore();
        store.Values[DataKey] = stored;

        NewsUpdate update = LiveService(store, new FakeTransport(Failed())).Refresh(T0);

        NewsFeedReport report = Assert.Single(update.Reports, r => r.Outcome == NewsFeedOutcome.CacheUnreadable);
        Assert.StartsWith("Cached data ignored: ", report.Detail, StringComparison.Ordinal);
        Assert.Equal(NewsStatus.Unavailable, update.Status);
        Assert.Empty(update.Calendar.Events);
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Cache_UnreadableThenGoodDownload_IsOverwritten()
    {
        var store = new MemoryStore();
        store.Values[DataKey] = "garbage";

        NewsUpdate update = LiveService(store, new FakeTransport(Ok(Sample))).Refresh(T0);

        Assert.Equal(NewsStatus.Available, update.Status);
        Assert.StartsWith("{\"fetchedUtc\":\"2026-11-11T12:00:00.0000000Z\"", store.Values[DataKey], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Cache_IsReadOnlyAtStart()
    {
        var store = new MemoryStore();
        NewsService service = LiveService(store, new FakeTransport(Failed()));
        service.Refresh(T0);
        store.Values[DataKey] = GoodCache();

        Assert.Equal(NewsStatus.Unavailable, service.Refresh(T0.AddMinutes(1)).Status);
    }
}
