using Xunit;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-001: a window runs from NewsBeforeMin (30) minutes before to NewsAfterMin (30) minutes
/// after each high-impact event of the symbol's base or quote currency, or of "All", both ends
/// included.
/// Overlapping windows merge. Reference event: USD high impact at 2026-11-10 13:30 UTC, so the
/// window is 13:00:00 to 14:00:00.
/// </summary>
public class NewsWindowTests
{
    private static readonly DateTime CpiTime = Utc(2026, 11, 10, 13, 30);

    [Theory]
    [Trait("Req", "NWS-001")]
    [InlineData(12, 59, 0, false)]
    [InlineData(12, 59, 59, false)]
    [InlineData(13, 0, 0, true)]
    [InlineData(13, 0, 1, true)]
    [InlineData(13, 30, 0, true)]
    [InlineData(13, 59, 59, true)]
    [InlineData(14, 0, 0, true)]
    [InlineData(14, 0, 1, false)]
    [InlineData(14, 1, 0, false)]
    public void IsInWindow_AroundTheEdges_BothEndsIncluded(int hour, int minute, int second, bool expected)
    {
        NewsCalendar calendar = Calendar(Event("USD", CpiTime));

        Assert.Equal(expected, calendar.IsInWindow("EURUSD", Utc(2026, 11, 10, hour, minute, second)));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void WindowsFor_OneEvent_IsMinus30ToPlus30()
    {
        NewsWindow window = Assert.Single(Calendar(Event("USD", CpiTime)).WindowsFor("EURUSD"));

        Assert.Equal(Utc(2026, 11, 10, 13, 0), window.StartUtc);
        Assert.Equal(Utc(2026, 11, 10, 14, 0), window.EndUtc);
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void WindowsFor_CustomBeforeAndAfter_AreUsed()
    {
        var calendar = new NewsCalendar(new IReadOnlyList<NewsEvent>[] { new[] { Event("USD", CpiTime) } }, Array.Empty<NewsEvent>(), 10, 45);

        NewsWindow window = Assert.Single(calendar.WindowsFor("EURUSD"));

        Assert.Equal(Utc(2026, 11, 10, 13, 20), window.StartUtc);
        Assert.Equal(Utc(2026, 11, 10, 14, 15), window.EndUtc);
    }

    [Theory]
    [Trait("Req", "NWS-001")]
    [InlineData("EURUSD", true)]
    [InlineData("USDJPY", true)]
    [InlineData("AUDUSD", true)]
    [InlineData("GBPUSD", true)]
    [InlineData("EURGBP", false)]
    [InlineData("AUDJPY", false)]
    public void IsInWindow_UsdEvent_OnlySymbolsWithUsdAsBaseOrQuote(string symbol, bool expected)
    {
        Assert.Equal(expected, Calendar(Event("USD", CpiTime)).IsInWindow(symbol, CpiTime));
    }

    [Theory]
    [Trait("Req", "NWS-001")]
    [InlineData("EURUSD")]
    [InlineData("eurusd")]
    [InlineData("EURUSD.r")]
    [InlineData("EURUSDm")]
    [InlineData("EURUSD_pro")]
    public void IsInWindow_SuffixAndCase_AreIgnored(string symbol)
    {
        NewsCalendar calendar = Calendar(Event("eur", CpiTime));

        Assert.True(calendar.IsInWindow(symbol, CpiTime));
        Assert.Single(calendar.WindowsFor(symbol));
    }

    [Theory]
    [Trait("Req", "NWS-001")]
    [InlineData(NewsImpact.Medium)]
    [InlineData(NewsImpact.Low)]
    [InlineData(NewsImpact.Holiday)]
    [InlineData(NewsImpact.Unknown)]
    public void WindowsFor_NonHighImpact_NoWindow(NewsImpact impact)
    {
        Assert.Empty(Calendar(Event("USD", CpiTime, impact)).WindowsFor("EURUSD"));
    }

    [Theory]
    [Trait("Req", "NWS-001")]
    [InlineData("EURUSD")]
    [InlineData("GBPUSD")]
    [InlineData("USDJPY")]
    [InlineData("AUDUSD")]
    [InlineData("EURGBP")]
    [InlineData("NZDCHF")]
    public void IsInWindow_HighEventForAll_IsRelevantToEverySymbol(string symbol)
    {
        // Owner decision of 2026-11-14: country "All" applies to every symbol. Case is ignored.
        Assert.True(Calendar(Event("All", CpiTime)).IsInWindow(symbol, CpiTime));
        Assert.True(Calendar(Event("ALL", CpiTime)).IsInWindow(symbol, CpiTime));
        Assert.Single(Calendar(Event("All", CpiTime)).WindowsFor(symbol));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void IsInWindow_LowEventForAll_NoWindow()
    {
        Assert.Empty(Calendar(Event("All", CpiTime, NewsImpact.Low)).WindowsFor("EURUSD"));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void WindowsFor_RealSampleWithItsAllEventsMadeHigh_OpensWindowsForEverySymbol()
    {
        // The sample's two "All" events (G7 Meetings, 04:45 at -05:00 = 09:45 UTC on 11-09 and
        // 11-10) are Low. Raised to High here, they open 09:15-10:15 windows for any pair.
        IReadOnlyList<NewsEvent> events = new ForexFactoryParser().Parse(Sample, SampleWednesday).Events
            .Select(e => e.Currency == "All" ? new NewsEvent(e.TimeUtc, e.Currency, NewsImpact.High, e.Title, e.Source) : e)
            .ToArray();
        var calendar = new NewsCalendar(new[] { events }, Array.Empty<NewsEvent>(), 30, 30);

        Assert.Equal(
            new[]
            {
                new NewsWindow(Utc(2026, 11, 9, 9, 15), Utc(2026, 11, 9, 10, 15)),
                new NewsWindow(Utc(2026, 11, 10, 9, 15), Utc(2026, 11, 10, 10, 15)),
            },
            calendar.WindowsFor("AUDJPY"));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void WindowsFor_OverlappingWindows_Merge()
    {
        // 13:30 gives 13:00-14:00 and 14:15 gives 13:45-14:45: one window 13:00-14:45.
        NewsCalendar calendar = Calendar(Event("USD", CpiTime), Event("EUR", Utc(2026, 11, 10, 14, 15)));

        NewsWindow window = Assert.Single(calendar.WindowsFor("EURUSD"));

        Assert.Equal(Utc(2026, 11, 10, 13, 0), window.StartUtc);
        Assert.Equal(Utc(2026, 11, 10, 14, 45), window.EndUtc);
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void WindowsFor_TouchingWindows_Merge()
    {
        // 13:30 gives 13:00-14:00 and 14:30 gives 14:00-15:00: they share 14:00, so one window.
        NewsCalendar calendar = Calendar(Event("USD", Utc(2026, 11, 10, 14, 30)), Event("USD", CpiTime));

        NewsWindow window = Assert.Single(calendar.WindowsFor("EURUSD"));

        Assert.Equal(Utc(2026, 11, 10, 13, 0), window.StartUtc);
        Assert.Equal(Utc(2026, 11, 10, 15, 0), window.EndUtc);
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void WindowsFor_WindowsOneMinuteApart_StaySeparate()
    {
        // 13:30 gives 13:00-14:00 and 14:31 gives 14:01-15:01.
        NewsCalendar calendar = Calendar(Event("USD", CpiTime), Event("USD", Utc(2026, 11, 10, 14, 31)));

        Assert.Equal(2, calendar.WindowsFor("EURUSD").Count);
        Assert.False(calendar.IsInWindow("EURUSD", Utc(2026, 11, 10, 14, 0, 30)));
        Assert.True(calendar.IsInWindow("EURUSD", Utc(2026, 11, 10, 14, 1)));
        Assert.True(calendar.IsInWindow("EURUSD", Utc(2026, 11, 10, 15, 1)));
        Assert.False(calendar.IsInWindow("EURUSD", Utc(2026, 11, 10, 15, 1, 1)));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void WindowsFor_SameTimeEvents_GiveOneWindow()
    {
        NewsCalendar calendar = Calendar(Event("USD", CpiTime, title: "CPI m/m"), Event("USD", CpiTime, title: "Core CPI m/m"));

        Assert.Single(calendar.WindowsFor("EURUSD"));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void WindowsFor_RealSample_MatchTheHighImpactEventsByHand()
    {
        // High events in the sample (UTC): USD 11-10 13:30 (two), GBP 11-12 07:00, USD 11-13 13:30 (two).
        IReadOnlyList<NewsEvent> events = new ForexFactoryParser().Parse(Sample, SampleWednesday).Events;
        NewsCalendar calendar = new NewsCalendar(new[] { events }, Array.Empty<NewsEvent>(), 30, 30);
        NewsWindow[] usd =
        {
            new NewsWindow(Utc(2026, 11, 10, 13, 0), Utc(2026, 11, 10, 14, 0)),
            new NewsWindow(Utc(2026, 11, 13, 13, 0), Utc(2026, 11, 13, 14, 0)),
        };
        NewsWindow[] gbpUsd =
        {
            usd[0],
            new NewsWindow(Utc(2026, 11, 12, 6, 30), Utc(2026, 11, 12, 7, 30)),
            usd[1],
        };

        Assert.Equal(usd, calendar.WindowsFor("EURUSD"));
        Assert.Equal(usd, calendar.WindowsFor("USDJPY"));
        Assert.Equal(usd, calendar.WindowsFor("AUDUSD"));
        Assert.Equal(gbpUsd, calendar.WindowsFor("GBPUSD"));
        Assert.Equal(new[] { gbpUsd[1] }, calendar.WindowsFor("EURGBP"));
        Assert.Empty(calendar.WindowsFor("AUDJPY"));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void WindowsFor_SameCalendarAskedTwice_GivesTheSameWindows()
    {
        NewsCalendar calendar = Calendar(Event("USD", CpiTime));

        Assert.Same(calendar.WindowsFor("EURUSD"), calendar.WindowsFor("EURUSD.r"));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void WindowsFor_InputOrder_DoesNotChangeTheResult()
    {
        NewsEvent[] events =
        {
            Event("USD", Utc(2026, 11, 10, 16, 0)),
            Event("EUR", CpiTime),
            Event("USD", Utc(2026, 11, 10, 14, 15)),
            Event("JPY", Utc(2026, 11, 10, 1, 0)),
        };

        IReadOnlyList<NewsWindow> forward = Calendar(events).WindowsFor("EURUSD");
        IReadOnlyList<NewsWindow> backward = Calendar(Enumerable.Reverse(events).ToArray()).WindowsFor("EURUSD");

        Assert.Equal(forward, backward);
        Assert.Equal(2, forward.Count);
    }

    [Theory]
    [Trait("Req", "NWS-001")]
    [InlineData(12, 0, 13, 0)]
    [InlineData(12, 15, 13, 0)]
    [InlineData(13, 0, 13, 0)]
    [InlineData(13, 1, 14, 31)]
    [InlineData(14, 0, 14, 31)]
    [InlineData(14, 31, 14, 31)]
    public void NextWindowStart_ReturnsTheFirstWindowStartingAtOrAfterTheInstant(int hour, int minute, int expectedHour, int expectedMinute)
    {
        // Events at 13:30 and 15:01 give the windows 13:00-14:00 and 14:31-15:31.
        NewsCalendar calendar = Calendar(Event("USD", CpiTime), Event("USD", Utc(2026, 11, 10, 15, 1)));

        Assert.Equal(Utc(2026, 11, 10, expectedHour, expectedMinute), calendar.NextWindowStart("EURUSD", Utc(2026, 11, 10, hour, minute)));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void NextWindowStart_NoLaterWindow_IsNull()
    {
        NewsCalendar calendar = Calendar(Event("USD", CpiTime));

        Assert.Null(calendar.NextWindowStart("EURUSD", Utc(2026, 11, 10, 13, 0, 1)));
        Assert.Null(calendar.NextWindowStart("EURGBP", Utc(2026, 11, 10, 0, 0)));
    }

    [Theory]
    [Trait("Req", "NWS-001")]
    [Trait("Req", "NWS-009")]
    [InlineData(12, 45, 13, 0, false)]
    [InlineData(12, 46, 13, 1, true)]
    [InlineData(13, 0, 13, 15, true)]
    [InlineData(13, 45, 14, 0, true)]
    [InlineData(14, 0, 14, 15, true)]
    [InlineData(14, 1, 14, 16, false)]
    [InlineData(11, 0, 15, 0, true)]
    public void OverlapsWindow_PeriodIncludesStartExcludesEnd(int fromHour, int fromMinute, int toHour, int toMinute, bool expected)
    {
        // Window 13:00:00-14:00:00. A bar from 12:45 to 13:00 ends as the window starts: no overlap.
        NewsCalendar calendar = Calendar(Event("USD", CpiTime));

        Assert.Equal(expected, calendar.OverlapsWindow("EURUSD", Utc(2026, 11, 10, fromHour, fromMinute), Utc(2026, 11, 10, toHour, toMinute)));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void OverlapsWindow_EmptyOrReversedPeriod_Throws()
    {
        NewsCalendar calendar = Calendar(Event("USD", CpiTime));

        Assert.Throws<ArgumentException>(() => calendar.OverlapsWindow("EURUSD", CpiTime, CpiTime));
        Assert.Throws<ArgumentException>(() => calendar.OverlapsWindow("EURUSD", CpiTime, CpiTime.AddMinutes(-1)));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void Queries_TimesNotUtc_Throw()
    {
        NewsCalendar calendar = Calendar(Event("USD", CpiTime));
        DateTime local = new DateTime(2026, 11, 10, 13, 30, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => calendar.IsInWindow("EURUSD", local));
        Assert.Throws<ArgumentException>(() => calendar.OverlapsWindow("EURUSD", local, CpiTime.AddHours(1)));
        Assert.Throws<ArgumentException>(() => calendar.OverlapsWindow("EURUSD", CpiTime, local.AddHours(1)));
        Assert.Throws<ArgumentException>(() => calendar.NextWindowStart("EURUSD", local));
    }

    [Theory]
    [Trait("Req", "NWS-001")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("EURUS")]
    [InlineData("EUR/USD")]
    [InlineData("EUR1SD")]
    [InlineData("ÉURUSD")]
    public void Queries_SymbolWithoutSixLeadingLetters_Throw(string? symbol)
    {
        NewsCalendar calendar = Calendar(Event("USD", CpiTime));

        Assert.Throws<ArgumentException>(() => calendar.IsInWindow(symbol!, CpiTime));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void Constructor_InvalidArguments_Throw()
    {
        var none = Array.Empty<NewsEvent>();
        var sources = new IReadOnlyList<NewsEvent>[] { none };

        Assert.Throws<ArgumentNullException>(() => new NewsCalendar(null!, none, 30, 30));
        Assert.Throws<ArgumentNullException>(() => new NewsCalendar(sources, null!, 30, 30));
        Assert.Throws<ArgumentNullException>(() => new NewsCalendar(new IReadOnlyList<NewsEvent>[] { null! }, none, 30, 30));
        Assert.Throws<ArgumentNullException>(() => new NewsCalendar(new IReadOnlyList<NewsEvent>[] { new NewsEvent[] { null! } }, none, 30, 30));
        Assert.Throws<ArgumentNullException>(() => new NewsCalendar(sources, new NewsEvent[] { null! }, 30, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NewsCalendar(sources, none, -1, 30));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NewsCalendar(sources, none, 30, -1));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void Constructor_NoSources_HasNoWindows()
    {
        var calendar = new NewsCalendar(Array.Empty<IReadOnlyList<NewsEvent>>(), Array.Empty<NewsEvent>(), 30, 30);

        Assert.Empty(calendar.Events);
        Assert.False(calendar.IsInWindow("EURUSD", CpiTime));
        Assert.False(calendar.OverlapsWindow("EURUSD", CpiTime, CpiTime.AddMinutes(15)));
        Assert.Null(calendar.NextWindowStart("EURUSD", CpiTime));
    }

    [Fact]
    [Trait("Req", "NWS-011")]
    public void Constructor_ExtraBlackouts_OpenWindowsWithoutMerging()
    {
        // A feed event at 13:30 and a manual blackout at 13:32: the blackout keeps its own time,
        // so the merged window runs 13:00 to 14:02.
        var calendar = new NewsCalendar(
            new IReadOnlyList<NewsEvent>[] { new[] { Event("USD", CpiTime) } },
            ExtraBlackoutsParser.Parse("2026-11-10 13:32 USD").Events,
            30,
            30);

        NewsWindow window = Assert.Single(calendar.WindowsFor("EURUSD"));

        Assert.Equal(Utc(2026, 11, 10, 14, 2), window.EndUtc);
        Assert.Equal(2, calendar.Events.Count);
    }
}
