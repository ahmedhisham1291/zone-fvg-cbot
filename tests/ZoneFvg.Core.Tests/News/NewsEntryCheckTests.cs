using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-009: a signal is skipped if the current time is inside a news window of its symbol, or if
/// any of its three FVG bars overlaps one. The sample is fetched on Thursday 2026-11-12 12:00 UTC;
/// USD PPI on Friday 2026-11-13 at 08:30 New York time (-05:00) is 13:30 UTC, so the EURUSD
/// window is 13:00:00 to 14:00:00. Each M15 bar spans its open time to its open time plus
/// 15 minutes, the close excluded.
/// </summary>
public class NewsEntryCheckTests
{
    private static NewsService Service()
    {
        NewsService service = LiveService(new MemoryStore(), new FakeTransport(Ok(Sample)));
        Assert.Equal(NewsStatus.Available, service.Refresh(Utc(2026, 11, 12, 12, 0)).Status);
        return service;
    }

    private static DateTime Friday(int hour, int minute, int second = 0)
    {
        return Utc(2026, 11, 13, hour, minute, second);
    }

    private static Fvg Gap(DateTime c3, DateTime c2, DateTime c1)
    {
        return new Fvg(Side.Buy, 1.1, 1.0, c3, c2, c1);
    }

    private static Fvg ContiguousGap(int hour, int minute)
    {
        DateTime c3 = Friday(hour, minute);
        return Gap(c3, c3.AddMinutes(15), c3.AddMinutes(30));
    }

    [Theory]
    [Trait("Req", "NWS-009")]
    [InlineData(13, 0, 0)]
    [InlineData(13, 30, 0)]
    [InlineData(14, 0, 0)]
    public void CheckEntry_NowInsideAWindow_IsBlocked(int hour, int minute, int second)
    {
        NewsDecision decision = Service().CheckEntry("EURUSD", Friday(hour, minute, second), ContiguousGap(11, 0));

        Assert.False(decision.IsAllowed);
        Assert.Equal(NewsBlockReason.InsideWindow, decision.Reason);
    }

    [Fact]
    [Trait("Req", "NWS-009")]
    public void CheckEntry_FvgBarOverlappingAWindow_IsBlockedNamingTheBar()
    {
        // c3 13:45-14:00 lies in the window; now 14:30 is outside it.
        NewsDecision decision = Service().CheckEntry("EURUSD", Friday(14, 30), ContiguousGap(13, 45));

        Assert.False(decision.IsAllowed);
        Assert.Equal(NewsBlockReason.FvgBarInWindow, decision.Reason);
        Assert.Equal(Friday(13, 45), decision.BarOpenUtc);
    }

    [Fact]
    [Trait("Req", "NWS-009")]
    public void CheckEntry_BarOpeningAtTheWindowEnd_Overlaps()
    {
        // c3 opens at 14:00:00, the last instant of the window.
        NewsDecision decision = Service().CheckEntry("EURUSD", Friday(14, 45), ContiguousGap(14, 0));

        Assert.Equal(NewsBlockReason.FvgBarInWindow, decision.Reason);
        Assert.Equal(Friday(14, 0), decision.BarOpenUtc);
    }

    [Fact]
    [Trait("Req", "NWS-009")]
    public void CheckEntry_FirstBarAfterTheWindow_IsAllowed()
    {
        NewsDecision decision = Service().CheckEntry("EURUSD", Friday(15, 0), ContiguousGap(14, 15));

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.BarOpenUtc);
    }

    [Fact]
    [Trait("Req", "NWS-009")]
    public void CheckEntry_BarsBeforeTheWindow_AreAllowed()
    {
        // c1 12:30-12:45 ends before 13:00; now 12:45 is outside the window.
        NewsDecision decision = Service().CheckEntry("EURUSD", Friday(12, 45), ContiguousGap(12, 0));

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    [Trait("Req", "NWS-009")]
    public void CheckEntry_LastBarClosingAtTheWindowStart_NowIsInside()
    {
        // c1 12:45-13:00 does not overlap the window, but its close 13:00 is inside it.
        NewsService service = Service();

        Assert.False(service.CalendarAt(Friday(13, 0)).OverlapsWindow("EURUSD", Friday(12, 45), Friday(13, 0)));
        Assert.Equal(NewsBlockReason.InsideWindow, service.CheckEntry("EURUSD", Friday(13, 0), ContiguousGap(12, 15)).Reason);
    }

    [Theory]
    [Trait("Req", "NWS-009")]
    [InlineData(0, 13, 45)]
    [InlineData(1, 13, 50)]
    [InlineData(2, 12, 46)]
    public void CheckEntry_EachOfTheThreeBars_IsChecked(int bar, int hour, int minute)
    {
        DateTime outside = Friday(9, 0);
        DateTime inside = Friday(hour, minute);
        Fvg gap = bar switch
        {
            0 => Gap(inside, outside.AddMinutes(15), outside.AddMinutes(30)),
            1 => Gap(outside, inside, outside.AddMinutes(30)),
            _ => Gap(outside, outside.AddMinutes(15), inside),
        };

        NewsDecision decision = Service().CheckEntry("EURUSD", Friday(15, 0), gap);

        Assert.Equal(NewsBlockReason.FvgBarInWindow, decision.Reason);
        Assert.Equal(inside, decision.BarOpenUtc);
    }

    [Fact]
    [Trait("Req", "NWS-009")]
    public void CheckEntry_SymbolWithoutTheEventCurrency_IsAllowed()
    {
        Assert.True(Service().CheckEntry("EURGBP", Friday(13, 30), ContiguousGap(13, 0)).IsAllowed);
    }

    [Fact]
    [Trait("Req", "NWS-009")]
    public void CheckEntry_InvalidArguments_Throw()
    {
        NewsService service = Service();

        Assert.Throws<ArgumentNullException>(() => service.CheckEntry("EURUSD", Friday(15, 0), null!));
        Assert.Throws<ArgumentException>(() => service.CheckEntry("EUR", Friday(15, 0), ContiguousGap(14, 15)));
    }
}
