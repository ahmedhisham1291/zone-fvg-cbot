using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.News;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-014: a breakout signal is checked against the calendar (NWS-008), the current time
/// (NWS-009) and its one confirming bar, from its close minus 15 minutes to its close, with the
/// overlap rule of NWS-009 (the bar's close excluded, both ends of a window included). News
/// windows: NewsBeforeMin and NewsAfterMin 30. Backtest-mode news service fed from text; the
/// day is Wednesday 2026-01-14, the confirming bar runs from 09:45 to 10:00 and now is 10:00.
/// </summary>
public class NewsBreakoutBarTests
{
    private static readonly DateTime Now = ExecutionData.Now;

    private static NewsService Service(string news)
    {
        return new NewsService(new NewsParameters(), new[] { NewsTestData.ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(NewsTestData.Failed()), true, news);
    }

    private static DateTime At(int hour, int minute)
    {
        return ExecutionData.At(14, hour, minute);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void CheckBreakoutEntry_CalendarUnavailable_IsBlocked()
    {
        // The only event is in February, so the history does not cover 2026-01-14.
        NewsDecision decision = Service("2026-02-18 18:00,USD,High,Event\n").CheckBreakoutEntry("EURUSD", Now, Now);

        Assert.Equal(NewsBlockReason.CalendarUnavailable, decision.Reason);
        Assert.False(decision.IsAllowed);
        Assert.Null(decision.BarOpenUtc);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void CheckBreakoutEntry_NowInsideAWindow_IsBlocked()
    {
        // Event at 10:30: the window 10:00 to 11:00 contains now; the bar 09:45 to 10:00 ends at its start.
        NewsDecision decision = Service("2026-01-14 10:30,USD,High,Event\n").CheckBreakoutEntry("EURUSD", Now, Now);

        Assert.Equal(NewsBlockReason.InsideWindow, decision.Reason);
        Assert.Null(decision.BarOpenUtc);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void CheckBreakoutEntry_ConfirmingBarOverlapsAWindow_IsBlockedNamingTheBar()
    {
        // Event at 09:20: the window 08:50 to 09:50 overlaps the bar 09:45 to 10:00; now 10:00 is outside it.
        NewsDecision decision = Service("2026-01-14 09:20,USD,High,Event\n").CheckBreakoutEntry("EURUSD", Now, Now);

        Assert.Equal(NewsBlockReason.BreakoutBarInWindow, decision.Reason);
        Assert.False(decision.IsAllowed);
        Assert.Equal(At(9, 45), decision.BarOpenUtc);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void CheckBreakoutEntry_WindowEndingAtTheBarOpen_Overlaps()
    {
        // Event at 09:15: the window 08:45 to 09:45 ends at 09:45:00, the first instant of the bar.
        // Both ends of a window are inside it (NWS-001), as in CheckEntry_BarOpeningAtTheWindowEnd_Overlaps.
        NewsDecision decision = Service("2026-01-14 09:15,USD,High,Event\n").CheckBreakoutEntry("EURUSD", Now, Now);

        Assert.Equal(NewsBlockReason.BreakoutBarInWindow, decision.Reason);
        Assert.Equal(At(9, 45), decision.BarOpenUtc);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void CheckBreakoutEntry_WindowEndingOneMinuteBeforeTheBar_IsAllowed()
    {
        // Event at 09:14: the window 08:44 to 09:44 ends before the bar opens.
        NewsDecision decision = Service("2026-01-14 09:14,USD,High,Event\n").CheckBreakoutEntry("EURUSD", Now, Now);

        Assert.True(decision.IsAllowed);
        Assert.Equal(NewsBlockReason.None, decision.Reason);
        Assert.Null(decision.BarOpenUtc);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void CheckBreakoutEntry_WindowStartingOneMinuteAfterTheBarClose_IsAllowed()
    {
        // Event at 10:31: the window starts at 10:01, after the bar's close and after now.
        Assert.True(Service("2026-01-14 10:31,USD,High,Event\n").CheckBreakoutEntry("EURUSD", Now, Now).IsAllowed);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void CheckBreakoutEntry_OnlyTheConfirmingBarIsChecked_NotTheThreeBarsOfAGap()
    {
        // Event at 09:00: the window 08:30 to 09:30 overlaps the bars 09:15 and 09:30 of a gap
        // ending at 10:00, but not the confirming bar 09:45 to 10:00.
        NewsService service = Service("2026-01-14 09:00,USD,High,Event\n");
        var gap = new Fvg(Side.Buy, 1.1005, 1.0995, At(9, 15), At(9, 30), At(9, 45));

        Assert.True(service.CheckBreakoutEntry("EURUSD", Now, Now).IsAllowed);
        Assert.Equal(NewsBlockReason.FvgBarInWindow, service.CheckEntry("EURUSD", Now, gap).Reason);
        Assert.Equal(At(9, 15), service.CheckEntry("EURUSD", Now, gap).BarOpenUtc);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void CheckBreakoutEntry_UsesTheConfirmationCloseNotNow()
    {
        // Event at 09:16: the window 08:46 to 09:46 overlaps the confirming bar 09:45 to 10:00 but
        // not the 15 minutes before now (09:50 to 10:05, now 10:05: the signal is 5 minutes old).
        NewsDecision decision = Service("2026-01-14 09:16,USD,High,Event\n").CheckBreakoutEntry("EURUSD", At(10, 5), Now);

        Assert.Equal(NewsBlockReason.BreakoutBarInWindow, decision.Reason);
        Assert.Equal(At(9, 45), decision.BarOpenUtc);
    }

    [Theory]
    [Trait("Req", "NWS-014")]
    [InlineData("EUR", false)]
    [InlineData("USD", false)]
    [InlineData("GBP", true)]
    [InlineData("JPY", true)]
    public void CheckBreakoutEntry_OnlyEventsOfTheSymbolsCurrenciesBlock(string currency, bool allowed)
    {
        NewsDecision decision = Service("2026-01-14 09:20," + currency + ",High,Event\n").CheckBreakoutEntry("EURUSD", Now, Now);

        Assert.Equal(allowed, decision.IsAllowed);
        Assert.Equal(allowed ? NewsBlockReason.None : NewsBlockReason.BreakoutBarInWindow, decision.Reason);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void CheckBreakoutEntry_InvalidArguments_Throw()
    {
        NewsService service = Service(ExecutionData.QuietNews);

        Assert.Throws<ArgumentException>(() => service.CheckBreakoutEntry("EUR", Now, Now));
        Assert.Throws<ArgumentException>(() => service.CheckBreakoutEntry("EURUSD", DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), Now));
        Assert.Throws<ArgumentException>(() => service.CheckBreakoutEntry("EURUSD", Now, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void CheckEntry_WithAGap_IsUnchanged()
    {
        // The ZoneFvg rule of NWS-009: the gap's first bar 09:15 to 09:30 overlaps the window 08:50 to 09:50.
        var gap = new Fvg(Side.Buy, 1.1005, 1.0995, At(9, 15), At(9, 30), At(9, 45));

        NewsDecision decision = Service("2026-01-14 09:20,USD,High,Event\n").CheckEntry("EURUSD", Now, gap);

        Assert.Equal(NewsBlockReason.FvgBarInWindow, decision.Reason);
        Assert.Equal(At(9, 15), decision.BarOpenUtc);
    }
}
