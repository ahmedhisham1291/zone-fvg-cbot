using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-010: in backtests events come only from the embedded history; no network call is made.
/// A counting transport and a counting store prove that neither is touched.
/// </summary>
public class NewsServiceBacktestTests
{
    private const string History = "2026-11-10 13:30,USD,High,CPI m/m\n2026-11-12 07:00,GBP,High,GDP m/m\n2026-11-11 00:00,USD,Holiday,Bank Holiday\n";

    private static NewsService Backtest(MemoryStore store, FakeTransport http, string history = History, NewsParameters? parameters = null)
    {
        return new NewsService(parameters ?? Parameters(), new[] { ForexFactoryFeed() }, store, http, true, history);
    }

    [Fact]
    [Trait("Req", "NWS-010")]
    public void Backtest_AWeekOfRefreshesAndChecks_MakesNoTransportOrStoreCall()
    {
        var store = new MemoryStore();
        var http = new FakeTransport(Ok(Sample));
        NewsService service = Backtest(store, http);

        for (DateTime t = Utc(2026, 11, 9, 0, 0); t < Utc(2026, 11, 16, 0, 0); t = t.AddMinutes(15))
        {
            service.Refresh(t);
            service.CheckEntry("EURUSD", t, new Fvg(Side.Buy, 1.1, 1.0, t.AddMinutes(-45), t.AddMinutes(-30), t.AddMinutes(-15)));
            service.CalendarAt(t).NextWindowStart("GBPUSD", t);
        }

        Assert.Equal(0, http.Calls);
        Assert.Equal(0, store.Gets);
        Assert.Equal(0, store.Sets);
    }

    [Fact]
    [Trait("Req", "NWS-010")]
    public void Backtest_WindowsComeFromTheHistory()
    {
        NewsService service = Backtest(new MemoryStore(), new FakeTransport(Ok(Sample)));
        NewsUpdate update = service.Refresh(Utc(2026, 11, 10, 0, 0));

        Assert.Equal(NewsStatus.Available, update.Status);
        Assert.Equal(new[] { new NewsWindow(Utc(2026, 11, 10, 13, 0), Utc(2026, 11, 10, 14, 0)) }, update.Calendar.WindowsFor("EURUSD"));
        Assert.Equal(2, update.Calendar.WindowsFor("GBPUSD").Count);
        Assert.Equal("Bank Holiday", Assert.Single(update.Calendar.EventsWithoutWindow).Title);
        Assert.Equal(NewsBlockReason.InsideWindow, service.CheckEntry("EURUSD", Utc(2026, 11, 10, 13, 30), new Fvg(Side.Buy, 1.1, 1.0, Utc(2026, 11, 10, 12, 0), Utc(2026, 11, 10, 12, 15), Utc(2026, 11, 10, 12, 30))).Reason);
    }

    [Fact]
    [Trait("Req", "NWS-010")]
    public void Backtest_HistoryReportedOnceAtStart()
    {
        NewsService service = Backtest(new MemoryStore(), new FakeTransport(Ok(Sample)));

        NewsUpdate start = service.Refresh(Utc(2026, 11, 10, 0, 0));
        NewsUpdate next = service.Refresh(Utc(2026, 11, 10, 0, 1));

        NewsFeedReport report = Assert.Single(start.Reports);
        Assert.Equal("EmbeddedHistory", report.Source);
        Assert.Equal(NewsFeedOutcome.HistoryLoaded, report.Outcome);
        Assert.Equal("3 events", report.Detail);
        Assert.Equal(3, report.Events.Count);
        Assert.Empty(next.Reports);
        Assert.True(start.CalendarChanged);
        Assert.False(next.CalendarChanged);
    }

    [Theory]
    [Trait("Req", "NWS-010")]
    [InlineData(2026, 11, 8, 23, 59, NewsStatus.Unavailable)]
    [InlineData(2026, 11, 9, 0, 0, NewsStatus.Available)]
    [InlineData(2026, 11, 15, 23, 59, NewsStatus.Available)]
    [InlineData(2026, 11, 16, 0, 0, NewsStatus.Unavailable)]
    public void Backtest_OutsideTheHistory_Unavailable(int year, int month, int day, int hour, int minute, NewsStatus expected)
    {
        NewsService service = Backtest(new MemoryStore(), new FakeTransport(Ok(Sample)));

        Assert.Equal(expected, service.Refresh(Utc(year, month, day, hour, minute)).Status);
    }

    [Fact]
    [Trait("Req", "NWS-010")]
    public void Backtest_MalformedHistory_RejectedUnavailableAndAlerted()
    {
        var http = new FakeTransport(Ok(Sample));
        NewsService service = Backtest(new MemoryStore(), http, "2026-11-10 13:30,USD,High,CPI\nbroken");

        NewsUpdate update = service.Refresh(Utc(2026, 11, 10, 0, 0));

        NewsFeedReport report = Assert.Single(update.Reports);
        Assert.Equal(NewsFeedOutcome.HistoryRejected, report.Outcome);
        Assert.Equal("Embedded history line 2 is malformed: \"broken\". Expected \"yyyy-MM-dd HH:mm,CUR,Impact,Title\".", report.Detail);
        Assert.Empty(report.Events);
        Assert.Equal(NewsStatus.Unavailable, update.Status);
        Assert.True(update.AlertRaised);
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    [Trait("Req", "NWS-010")]
    public void Backtest_WithoutFeeds_IsAccepted()
    {
        var service = new NewsService(Parameters(), Array.Empty<NewsFeed>(), new MemoryStore(), new FakeTransport(Ok(Sample)), true, History);

        Assert.Equal(NewsStatus.Available, service.Refresh(Utc(2026, 11, 10, 0, 0)).Status);
    }

    [Fact]
    [Trait("Req", "NWS-010")]
    [Trait("Req", "NWS-011")]
    public void Backtest_ExtraBlackoutsAreAdded()
    {
        NewsService service = Backtest(new MemoryStore(), new FakeTransport(Ok(Sample)), parameters: Parameters("2026-11-11 09:00 EUR"));

        NewsCalendar calendar = service.Refresh(Utc(2026, 11, 10, 0, 0)).Calendar;

        Assert.True(calendar.IsInWindow("EURGBP", Utc(2026, 11, 11, 9, 0)));
    }

    [Fact]
    [Trait("Req", "NWS-010")]
    public void Live_WithoutFeeds_Throws()
    {
        Assert.Throws<ArgumentException>(() => new NewsService(Parameters(), Array.Empty<NewsFeed>(), new MemoryStore(), new FakeTransport(Ok(Sample)), false, string.Empty));
    }

    [Fact]
    [Trait("Req", "NWS-010")]
    public void Constructor_InvalidArguments_Throw()
    {
        var store = new MemoryStore();
        var http = new FakeTransport(Ok(Sample));
        NewsFeed[] feeds = { ForexFactoryFeed() };

        Assert.Throws<ArgumentNullException>(() => new NewsService(null!, feeds, store, http, false, string.Empty));
        Assert.Throws<ArgumentNullException>(() => new NewsService(Parameters(), null!, store, http, false, string.Empty));
        Assert.Throws<ArgumentNullException>(() => new NewsService(Parameters(), feeds, null!, http, false, string.Empty));
        Assert.Throws<ArgumentNullException>(() => new NewsService(Parameters(), feeds, store, null!, false, string.Empty));
        Assert.Throws<ArgumentNullException>(() => new NewsService(Parameters(), feeds, store, http, false, null!));
        Assert.Throws<ArgumentException>(() => new NewsService(Parameters(), new NewsFeed[] { null! }, store, http, false, string.Empty));
        Assert.Throws<ArgumentException>(() => new NewsService(Parameters(), new[] { ForexFactoryFeed(), ForexFactoryFeed() }, store, http, false, string.Empty));
    }
}
