using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-008: if no source has trusted data covering the current time the status is Unavailable,
/// no entries are allowed (NewsFailMode Block), and one alert is raised per outage. The second
/// source is an in-test list (owner decision of 2026-11-14: Forex Factory alone in production).
/// </summary>
public class NewsServiceStatusTests
{
    private const string SecondUrl = "https://second.invalid/calendar";
    private static readonly DateTime T0 = SampleWednesday;
    private static readonly Fvg Gap = new Fvg(Side.Buy, 1.1, 1.0, T0.AddMinutes(-60), T0.AddMinutes(-45), T0.AddMinutes(-30));

    private static NewsService TwoFeedService(MemoryStore store, FakeTransport http)
    {
        var second = new ListParser("Second", new[] { Event("JPY", Utc(2026, 11, 12, 0, 0), source: "Second") }, Utc(2026, 11, 9, 0, 0), Utc(2026, 11, 16, 0, 0));
        return new NewsService(Parameters(), new[] { ForexFactoryFeed(), new NewsFeed(SecondUrl, second) }, store, http, false, string.Empty);
    }

    private static Func<string, HttpResult> Feeds(bool forexFactoryUp, bool secondUp)
    {
        return url => url == FeedUrl
            ? (forexFactoryUp ? Ok(Sample) : Failed())
            : (secondUp ? Ok("ok") : Failed());
    }

    [Fact]
    [Trait("Req", "NWS-008")]
    public void BothSourcesDownAndNoCache_UnavailableWithExactlyOneAlertUntilRecovery()
    {
        var http = new FakeTransport(Failed()) { Respond = Feeds(false, false) };
        NewsService service = TwoFeedService(new MemoryStore(), http);

        NewsUpdate first = service.Refresh(T0);
        NewsUpdate second = service.Refresh(T0.AddMinutes(1));
        NewsUpdate third = service.Refresh(T0.AddHours(4));
        http.Respond = Feeds(true, false);
        NewsUpdate recovered = service.Refresh(T0.AddHours(8));
        NewsUpdate after = service.Refresh(T0.AddHours(8).AddMinutes(1));

        Assert.Equal(NewsStatus.Unavailable, first.Status);
        Assert.True(first.AlertRaised);
        Assert.False(second.AlertRaised);
        Assert.Equal(NewsStatus.Unavailable, third.Status);
        Assert.False(third.AlertRaised);
        Assert.Equal(NewsStatus.Available, recovered.Status);
        Assert.True(recovered.Recovered);
        Assert.False(recovered.AlertRaised);
        Assert.False(after.Recovered);
        Assert.False(after.AlertRaised);
        Assert.False(first.Recovered);
    }

    [Fact]
    [Trait("Req", "NWS-008")]
    public void SecondOutage_RaisesASecondAlert()
    {
        var http = new FakeTransport(Ok(Sample));
        NewsService service = LiveService(new MemoryStore(), http);
        Assert.False(service.Refresh(T0).AlertRaised);
        http.Respond = _ => Failed();

        // Data fetched at T0 is trusted until T0 + 48 h; the refreshes in between fail.
        var alerts = new List<bool>();
        for (int hour = 1; hour <= 60; hour++)
        {
            alerts.Add(service.Refresh(T0.AddHours(hour)).AlertRaised);
        }

        Assert.Single(alerts, a => a);
        Assert.True(alerts[48]);
    }

    [Fact]
    [Trait("Req", "NWS-008")]
    public void SingleFeedDownAndCacheStale_Unavailable()
    {
        var store = new MemoryStore();
        LiveService(store, new FakeTransport(Ok(Sample))).Refresh(T0);
        NewsService restarted = LiveService(store, new FakeTransport(Failed()));

        NewsUpdate update = restarted.Refresh(T0.AddHours(49));

        Assert.Equal(NewsStatus.Unavailable, update.Status);
        Assert.True(update.AlertRaised);
        Assert.Contains(update.Reports, r => r.Outcome == NewsFeedOutcome.CacheLoaded);
    }

    [Theory]
    [Trait("Req", "NWS-008")]
    [InlineData(true, false, 106)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 107)]
    public void OneSourceUp_AvailableWithItsEventsOnly(bool forexFactoryUp, bool secondUp, int eventCount)
    {
        var http = new FakeTransport(Failed()) { Respond = Feeds(forexFactoryUp, secondUp) };
        NewsService service = TwoFeedService(new MemoryStore(), http);

        NewsUpdate update = service.Refresh(T0);

        Assert.Equal(NewsStatus.Available, update.Status);
        Assert.False(update.AlertRaised);
        Assert.Equal(eventCount, update.Calendar.Events.Count);
    }

    [Fact]
    [Trait("Req", "NWS-008")]
    public void NoRefreshYet_Unavailable()
    {
        NewsService service = LiveService(new MemoryStore(), new FakeTransport(Ok(Sample)));

        Assert.Equal(NewsStatus.Unavailable, service.StatusAt(T0));
        Assert.Equal(NewsBlockReason.CalendarUnavailable, service.CheckEntry("EURUSD", T0, Gap).Reason);
    }

    [Fact]
    [Trait("Req", "NWS-008")]
    public void Unavailable_EntryBlockedForEverySymbol()
    {
        NewsService service = LiveService(new MemoryStore(), new FakeTransport(Failed()));
        service.Refresh(T0);

        foreach (string symbol in new[] { "EURUSD", "GBPUSD", "USDJPY", "AUDUSD" })
        {
            NewsDecision decision = service.CheckEntry(symbol, T0, Gap);
            Assert.False(decision.IsAllowed);
            Assert.Equal(NewsBlockReason.CalendarUnavailable, decision.Reason);
            Assert.Null(decision.BarOpenUtc);
        }
    }

    [Fact]
    [Trait("Req", "NWS-008")]
    public void Available_EntryOutsideWindowsAllowed()
    {
        NewsService service = LiveService(new MemoryStore(), new FakeTransport(Ok(Sample)));
        service.Refresh(T0);

        NewsDecision decision = service.CheckEntry("EURUSD", T0, Gap);

        Assert.True(decision.IsAllowed);
        Assert.Equal(NewsBlockReason.None, decision.Reason);
    }

    [Fact]
    [Trait("Req", "NWS-008")]
    [Trait("Req", "NWS-011")]
    public void Unavailable_CalendarStillHoldsTheExtraBlackouts()
    {
        NewsService service = LiveService(new MemoryStore(), new FakeTransport(Failed()), Parameters("2026-11-11 13:00 USD"));

        NewsUpdate update = service.Refresh(T0);

        Assert.Equal(NewsStatus.Unavailable, update.Status);
        Assert.True(update.Calendar.IsInWindow("EURUSD", Utc(2026, 11, 11, 12, 30)));
    }

    [Fact]
    [Trait("Req", "NWS-008")]
    public void CalendarChanged_OnlyWhenTheTrustedDataChanges()
    {
        var http = new FakeTransport(Ok(Sample));
        NewsService service = LiveService(new MemoryStore(), http);

        NewsUpdate first = service.Refresh(T0);
        NewsUpdate same = service.Refresh(T0.AddMinutes(1));
        NewsUpdate refetched = service.Refresh(T0.AddHours(4));

        Assert.True(first.CalendarChanged);
        Assert.False(same.CalendarChanged);
        Assert.Same(first.Calendar, same.Calendar);
        Assert.True(refetched.CalendarChanged);
    }

    [Fact]
    [Trait("Req", "NWS-008")]
    public void Queries_TimesNotUtc_Throw()
    {
        NewsService service = LiveService(new MemoryStore(), new FakeTransport(Failed()));
        var local = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => service.Refresh(local));
        Assert.Throws<ArgumentException>(() => service.StatusAt(local));
        Assert.Throws<ArgumentException>(() => service.CalendarAt(local));
        Assert.Throws<ArgumentException>(() => service.CheckEntry("EURUSD", local, Gap));
    }
}
