using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Tests.SessionRangeData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The void of a setup (SIG-033): a bar that opened at or after RangeEndNy and reached the other
/// edge of the range while the order is pending. The range of every day here is 2000 to 2010.
/// </summary>
public class SessionRangeVoidTests
{
    [Theory]
    [Trait("Req", "SIG-033")]
    [InlineData(2000.0, true)]
    [InlineData(1999.99, true)]
    [InlineData(2000.01, false)]
    public void OnM15Closed_BuySetupAndLowAroundTheRangeLow_VoidsAtOrBelow(double low, bool expectVoid)
    {
        (SessionRangeTracker tracker, SessionRangeSetup setup) = WithSetup(Bias.Long);

        SessionRangeEvent? answer = tracker.OnM15Closed(Span(At(Winter, 3, 0), 2009.0, low), Bias.Long, AtrD1, AtrH4);

        AssertVoid(expectVoid, setup, answer);
    }

    [Theory]
    [Trait("Req", "SIG-033")]
    [InlineData(2010.0, true)]
    [InlineData(2010.01, true)]
    [InlineData(2009.99, false)]
    public void OnM15Closed_SellSetupAndHighAroundTheRangeHigh_VoidsAtOrAbove(double high, bool expectVoid)
    {
        (SessionRangeTracker tracker, SessionRangeSetup setup) = WithSetup(Bias.Short);

        SessionRangeEvent? answer = tracker.OnM15Closed(Span(At(Winter, 3, 0), high, 2001.0), Bias.Short, AtrD1, AtrH4);

        AssertVoid(expectVoid, setup, answer);
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OnM15Closed_BuySetupAndHighAtTheRangeHigh_DoesNotVoid()
    {
        (SessionRangeTracker tracker, _) = WithSetup(Bias.Long);

        Assert.Null(tracker.OnM15Closed(Span(At(Winter, 3, 0), 2015.0, 2001.0), Bias.Long, AtrD1, AtrH4));
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OnM15Closed_SellSetupAndLowAtTheRangeLow_DoesNotVoid()
    {
        (SessionRangeTracker tracker, _) = WithSetup(Bias.Short);

        Assert.Null(tracker.OnM15Closed(Span(At(Winter, 3, 0), 2009.0, 1995.0), Bias.Short, AtrD1, AtrH4));
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OnM15Closed_EvaluatingBarOpenedAt0245AtTheRangeLow_GivesTheSetupAndNoVoid()
    {
        List<Bar> bars = FullRange(Winter);
        bars[^1] = Span(At(Winter, 2, 45), 2006.0, RangeLow);
        SessionRangeTracker tracker = Tracker();

        List<SessionRangeEvent?> answers = Feed(tracker, bars, Bias.Long);

        Assert.Equal(SessionRangeEventKind.Setup, Assert.IsType<SessionRangeEvent>(answers[^1]).Kind);
        Assert.Null(tracker.OnM15Closed(Inside(At(Winter, 3, 0)), Bias.Long, AtrD1, AtrH4));
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OnM15Closed_LaterBarOpenedBefore0300AtTheRangeLow_DoesNotVoid()
    {
        // Bars off the quarter hour: one opens at 02:50 New York, after the evaluating 02:45 bar and before RangeEndNy.
        (SessionRangeTracker tracker, SessionRangeSetup setup) = WithSetup(Bias.Long);

        SessionRangeEvent? early = tracker.OnM15Closed(Span(At(Winter, 2, 50), 2006.0, 1990.0), Bias.Long, AtrD1, AtrH4);
        SessionRangeEvent? onTime = tracker.OnM15Closed(Span(At(Winter, 3, 5), 2006.0, 1990.0), Bias.Long, AtrD1, AtrH4);

        Assert.Null(early);
        AssertVoid(true, setup, onTime);
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OnM15Closed_VoidReported_IsReportedOnce()
    {
        (SessionRangeTracker tracker, SessionRangeSetup setup) = WithSetup(Bias.Long);

        SessionRangeEvent? first = tracker.OnM15Closed(Span(At(Winter, 3, 0), 2006.0, 1995.0), Bias.Long, AtrD1, AtrH4);
        SessionRangeEvent? second = tracker.OnM15Closed(Span(At(Winter, 3, 15), 2006.0, 1990.0), Bias.Long, AtrD1, AtrH4);

        AssertVoid(true, setup, first);
        Assert.Null(second);
        Assert.Same(setup, tracker.Setup);
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OnM15Closed_AfterTheCallerSaidTheOrderIsNoLongerPending_ReportsNoVoid()
    {
        (SessionRangeTracker tracker, SessionRangeSetup setup) = WithSetup(Bias.Long);

        tracker.OrderNoLongerPending(setup.Id);

        Assert.Null(tracker.OnM15Closed(Span(At(Winter, 3, 0), 2006.0, 1990.0), Bias.Long, AtrD1, AtrH4));
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OrderNoLongerPending_OtherId_ChangesNothing()
    {
        (SessionRangeTracker tracker, SessionRangeSetup setup) = WithSetup(Bias.Long);

        tracker.OrderNoLongerPending("RL-20250115");
        tracker.OrderNoLongerPending("RH-20250114");

        AssertVoid(true, setup, tracker.OnM15Closed(Span(At(Winter, 3, 0), 2006.0, 1990.0), Bias.Long, AtrD1, AtrH4));
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OrderNoLongerPending_WithoutASetup_ChangesNothing()
    {
        SessionRangeTracker tracker = Tracker();

        tracker.OrderNoLongerPending("RH-20250115");
        SessionRangeSetup? setup = OnlySetup(Feed(tracker, DayToEvaluation(Winter), Bias.Long));

        Assert.NotNull(setup);
        AssertVoid(true, setup!, tracker.OnM15Closed(Span(At(Winter, 3, 0), 2006.0, 1990.0), Bias.Long, AtrD1, AtrH4));
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OrderNoLongerPending_NullId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Tracker().OrderNoLongerPending(null!));
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OnM15Closed_AfterAVoid_NoSecondSetupThatDay()
    {
        (SessionRangeTracker tracker, SessionRangeSetup setup) = WithSetup(Bias.Long);
        AssertVoid(true, setup, tracker.OnM15Closed(Span(At(Winter, 3, 0), 2006.0, 1990.0), Bias.Long, AtrD1, AtrH4));

        List<SessionRangeEvent?> rest = Feed(tracker, InsideBars(At(Winter, 3, 15), 55), Bias.Short);

        Assert.All(rest, answer => Assert.Null(answer));
        Assert.Same(setup, tracker.Setup);
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OnM15Closed_StillPendingAfterTheExpiry_StillVoids()
    {
        // The tracker knows nothing of orders: until the caller says otherwise the order counts as pending.
        (SessionRangeTracker tracker, SessionRangeSetup setup) = WithSetup(Bias.Long);
        Feed(tracker, InsideBars(At(Winter, 3, 0), 40), Bias.Long);

        AssertVoid(true, setup, tracker.OnM15Closed(Span(At(Winter, 13, 0), 2006.0, 1990.0), Bias.Long, AtrD1, AtrH4));
    }

    [Fact]
    [Trait("Req", "SIG-033")]
    public void OnM15Closed_NextTradingDay_DoesNotVoidTheSetupOfTheDayBefore()
    {
        (SessionRangeTracker tracker, _) = WithSetup(Bias.Long);
        var nextDay = new DateTime(2025, 1, 16);

        SessionRangeEvent? answer = tracker.OnM15Closed(Span(At(nextDay, 17, 0), 2006.0, 1990.0), Bias.Long, AtrD1, AtrH4);

        Assert.Null(answer);
        Assert.Null(tracker.Setup);
        Assert.Equal(nextDay, tracker.TradingDate);
    }

    private static (SessionRangeTracker Tracker, SessionRangeSetup Setup) WithSetup(Bias bias)
    {
        SessionRangeTracker tracker = Tracker();
        SessionRangeSetup? setup = OnlySetup(Feed(tracker, DayToEvaluation(Winter), bias));
        Assert.NotNull(setup);
        return (tracker, setup!);
    }

    private static void AssertVoid(bool expectVoid, SessionRangeSetup setup, SessionRangeEvent? answer)
    {
        if (!expectVoid)
        {
            Assert.Null(answer);
            return;
        }

        SessionRangeEvent notice = Assert.IsType<SessionRangeEvent>(answer);
        Assert.Equal(SessionRangeEventKind.Void, notice.Kind);
        Assert.Same(setup, notice.Setup);
    }
}
