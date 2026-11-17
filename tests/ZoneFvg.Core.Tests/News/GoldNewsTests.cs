using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-013 (owner decision D of 2026-11-17): for XAUUSD the relevant currencies of NWS-001 are XAU
/// and USD. No feed publishes XAU events, so gold is blocked by high-impact USD events, by "All"
/// events and by ExtraBlackouts entries for USD or XAU, and not by EUR, GBP, JPY or AUD events.
/// NewsBeforeMin and NewsAfterMin are 30; both window ends are included (NWS-001).
/// </summary>
/// <remarks>
/// Live feed document fetched on Wednesday 2026-03-04 00:00 UTC (data older than NewsMaxCacheHours,
/// 48, would be unavailable): USD Non-Farm at Wednesday 2026-03-04 08:30 New York time (-05:00), which is 13:30 UTC; EUR CPI at 11:00 Frankfurt time (+01:00), which
/// is 10:00 UTC; an "All" event at 15:00 UTC; and GBP and JPY events at 11:00 and 12:00 UTC.
/// ExtraBlackouts adds XAU at 16:00 UTC.
/// </remarks>
public class GoldNewsTests
{
    private const string Gold = "XAUUSD";

    private static DateTime Wednesday(int hour, int minute, int second = 0)
    {
        return Utc(2026, 3, 4, hour, minute, second);
    }

    private static string Event(string title, string country, string date)
    {
        return "  {\n    \"title\": \"" + title + "\",\n    \"country\": \"" + country + "\",\n    \"date\": \"" + date
            + "\",\n    \"impact\": \"High\",\n    \"forecast\": \"\",\n    \"previous\": \"\"\n  }";
    }

    private static string Document()
    {
        return "[\n" + string.Join(
            ",\n",
            Event("Non-Farm Employment Change", "USD", "2026-03-04T08:30:00-05:00"),
            Event("German CPI", "EUR", "2026-03-04T11:00:00+01:00"),
            Event("Global holiday", "All", "2026-03-04T15:00:00+00:00"),
            Event("GDP m/m", "GBP", "2026-03-04T11:00:00+00:00"),
            Event("BoJ Rate", "JPY", "2026-03-04T21:00:00+09:00")) + "\n]";
    }

    private static NewsService Service()
    {
        NewsService service = LiveService(new MemoryStore(), new FakeTransport(Ok(Document())), Parameters("2026-03-04 16:00 XAU"));
        Assert.Equal(NewsStatus.Available, service.Refresh(Utc(2026, 3, 4, 0, 0)).Status);
        return service;
    }

    private static Fvg EarlyGap()
    {
        // Three M15 bars 06:00 to 06:45, far from every window.
        DateTime c3 = Wednesday(6, 0);
        return new Fvg(Side.Buy, 2650.5, 2650.0, c3, c3.AddMinutes(15.0), c3.AddMinutes(30.0));
    }

    [Theory]
    [Trait("Req", "NWS-013")]
    [InlineData(12, 59, 59, false)]
    [InlineData(13, 0, 0, true)]
    [InlineData(13, 30, 0, true)]
    [InlineData(14, 0, 0, true)]
    [InlineData(14, 0, 1, false)]
    public void IsInWindow_HighImpactUsdAt1330_BlocksGoldFrom1300To1400(int hour, int minute, int second, bool blocked)
    {
        NewsService service = Service();
        DateTime now = Wednesday(hour, minute, second);

        Assert.Equal(blocked, service.CalendarAt(now).IsInWindow(Gold, now));
        Assert.Equal(blocked, !service.CheckEntry(Gold, now, EarlyGap()).IsAllowed);
    }

    [Fact]
    [Trait("Req", "NWS-013")]
    public void CheckEntry_UsdWindow_GoldSkippedAsInsideWindow()
    {
        NewsDecision decision = Service().CheckEntry(Gold, Wednesday(13, 30), EarlyGap());

        Assert.Equal(NewsBlockReason.InsideWindow, decision.Reason);
    }

    [Theory]
    [Trait("Req", "NWS-013")]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 0)]
    public void CheckEntry_EurGbpOrJpyEvent_DoesNotBlockGold(int hour, int minute)
    {
        NewsService service = Service();
        DateTime now = Wednesday(hour, minute);

        Assert.True(service.CheckEntry(Gold, now, EarlyGap()).IsAllowed);
        Assert.False(service.CalendarAt(now).IsInWindow(Gold, now));
    }

    [Fact]
    [Trait("Req", "NWS-013")]
    public void CheckEntry_EurEventAt1000_BlocksEurUsdButNotGold()
    {
        NewsService service = Service();

        Assert.True(service.CalendarAt(Wednesday(10, 0)).IsInWindow("EURUSD", Wednesday(10, 0)));
        Assert.True(service.CheckEntry(Gold, Wednesday(10, 0), EarlyGap()).IsAllowed);
    }

    [Fact]
    [Trait("Req", "NWS-013")]
    public void CheckEntry_AllEventAt1500_BlocksGold()
    {
        NewsService service = Service();

        Assert.Equal(NewsBlockReason.InsideWindow, service.CheckEntry(Gold, Wednesday(15, 0), EarlyGap()).Reason);
        Assert.Contains(new NewsWindow(Wednesday(14, 30), Wednesday(15, 30)), service.CalendarAt(Wednesday(15, 0)).WindowsFor(Gold));
    }

    [Theory]
    [Trait("Req", "NWS-013")]
    [InlineData(15, 29, 59, true)]
    [InlineData(16, 0, 0, true)]
    [InlineData(16, 30, 0, true)]
    [InlineData(16, 30, 1, false)]
    public void IsInWindow_ExtraBlackoutXauAt1600_BlocksGoldFrom1530To1630(int hour, int minute, int second, bool blocked)
    {
        // 15:29:59 is still inside the window of the "All" event (14:30 to 15:30); the two windows merge.
        NewsService service = Service();
        DateTime now = Wednesday(hour, minute, second);

        Assert.Equal(blocked, service.CalendarAt(now).IsInWindow(Gold, now));
    }

    [Fact]
    [Trait("Req", "NWS-013")]
    public void WindowsFor_GoldDay_AreUsdAllAndXauOnly()
    {
        IReadOnlyList<NewsWindow> windows = Service().CalendarAt(Wednesday(0, 0)).WindowsFor(Gold);

        // USD 13:00-14:00; All 14:30-15:30 and XAU 15:30-16:30 touch and merge.
        Assert.Equal(new[] { new NewsWindow(Wednesday(13, 0), Wednesday(14, 0)), new NewsWindow(Wednesday(14, 30), Wednesday(16, 30)) }, windows);
    }

    [Fact]
    [Trait("Req", "NWS-013")]
    public void ExtraBlackoutXau_DoesNotBlockTheForexPairs()
    {
        NewsService service = Service();

        Assert.False(service.CalendarAt(Wednesday(16, 0)).IsInWindow("EURUSD", Wednesday(16, 0)));
        Assert.True(service.CalendarAt(Wednesday(16, 0)).IsInWindow(Gold, Wednesday(16, 0)));
    }

    [Fact]
    [Trait("Req", "NWS-013")]
    public void ExtraBlackoutUsd_BlocksGold()
    {
        NewsService service = LiveService(new MemoryStore(), new FakeTransport(Ok(Document())), Parameters("2026-03-04 18:00 USD"));
        service.Refresh(Utc(2026, 3, 4, 0, 0));

        Assert.True(service.CalendarAt(Wednesday(18, 0)).IsInWindow(Gold, Wednesday(18, 0)));
    }

    [Fact]
    [Trait("Req", "NWS-013")]
    public void Backtest_EmbeddedHistoryUsdLine_BlocksGold()
    {
        const string history = "2026-03-04 13:30,USD,High,Non-Farm Employment Change\n2026-03-04 10:00,EUR,High,German CPI\n";
        var service = new NewsService(Parameters(), new[] { ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(Failed()), true, history);
        Assert.Equal(NewsStatus.Available, service.Refresh(Utc(2026, 3, 4, 0, 0)).Status);

        Assert.Equal(NewsBlockReason.InsideWindow, service.CheckEntry(Gold, Wednesday(13, 30), EarlyGap()).Reason);
        Assert.True(service.CheckEntry(Gold, Wednesday(10, 0), EarlyGap()).IsAllowed);
    }

    [Theory]
    [Trait("Req", "NWS-013")]
    [InlineData("XAUUSD")]
    [InlineData("XAUUSD.r")]
    [InlineData("xauusd")]
    public void NewsSymbol_Gold_IsXauAndUsd(string symbol)
    {
        Assert.Equal("XAU", NewsSymbol.BaseCurrency(symbol));
        Assert.Equal("USD", NewsSymbol.QuoteCurrency(symbol));
    }
}
