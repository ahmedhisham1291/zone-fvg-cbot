using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Tests.BreakoutData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Latest confirmation time (SIG-029). Worked example of <see cref="BreakoutData"/>: reference high
/// 1.0850 and low 1.0780, reference bar closing 2026-03-03 22:00; the M15 bars are on Wednesday
/// 2026-03-04 and are named by their close time (open time plus 15 minutes). Window 07:00 to
/// 17:00 unless stated.
/// </summary>
public class BreakoutLastConfirmTests
{
    private const double BuyClose = 1.0858;
    private const double SellClose = 1.0770;

    private static readonly TimeSpan Eight = TimeSpan.FromHours(8.0);

    /// <summary>An M15 bar of 2026-03-04 that closes at the given time of day.</summary>
    private static Bar ClosingAt(int hour, int minute, double close)
    {
        return Flat(Utc(2026, 3, 4, hour, minute).AddMinutes(-15.0), close);
    }

    private static BreakoutTracker WithLastConfirm(TimeSpan lastConfirm)
    {
        return Armed(new BreakoutParameters { LastConfirmUtc = lastConfirm });
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_BuyBarClosing0745_Confirms()
    {
        BreakoutTracker tracker = WithLastConfirm(Eight);

        BreakoutSetup setup = Require(Feed(tracker, ClosingAt(7, 45, BuyClose), Bias.Long));

        Assert.Equal(Side.Buy, setup.Side);
        Assert.Equal(Utc(2026, 3, 4, 7, 45), setup.ConfirmationCloseUtc);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_BuyBarClosingExactlyAt0800_ConfirmsNothingAndFinishesTheReference()
    {
        BreakoutTracker tracker = WithLastConfirm(Eight);

        Assert.Null(Feed(tracker, ClosingAt(8, 0, BuyClose), Bias.Long));
        AssertDone(tracker);
        Assert.Equal(new BreakoutReference(DayOpen, High, Low, false), tracker.Reference);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_BuyBarClosingAfter0800_ConfirmsNothingAndFinishesTheReference()
    {
        BreakoutTracker tracker = WithLastConfirm(Eight);

        Assert.Null(Feed(tracker, ClosingAt(8, 15, BuyClose), Bias.Long));
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_SellBarClosing0745_ConfirmsAndAt0800FinishesTheReference()
    {
        BreakoutTracker early = WithLastConfirm(Eight);
        BreakoutTracker late = WithLastConfirm(Eight);

        BreakoutSetup setup = Require(Feed(early, ClosingAt(7, 45, SellClose), Bias.Short));
        BreakoutSetup? none = Feed(late, ClosingAt(8, 0, SellClose), Bias.Short);

        Assert.Equal(Side.Sell, setup.Side);
        AssertDone(early);
        Assert.Null(none);
        AssertDone(late);
    }

    [Theory]
    [Trait("Req", "SIG-029")]
    [InlineData(1.0820, 1)] // close inside the range, long bias
    [InlineData(BuyClose, 0)] // close above the high, neutral bias
    [InlineData(SellClose, 1)] // close below the low, long bias (the side the bias does not trade)
    public void OnM15Closed_CheckedBarAt0800ThatWouldNotConfirm_StillFinishesTheReference(double close, int bias)
    {
        BreakoutTracker tracker = WithLastConfirm(Eight);

        Assert.Null(Feed(tracker, ClosingAt(8, 0, close), bias == 1 ? Bias.Long : Bias.Neutral));
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_AfterTheReferenceIsFinished_ALaterBreakoutOfTheSameDayIsNotTraded()
    {
        BreakoutTracker tracker = WithLastConfirm(Eight);
        Assert.Null(Feed(tracker, ClosingAt(8, 0, 1.0820), Bias.Long));

        Assert.Null(Feed(tracker, ClosingAt(8, 15, BuyClose), Bias.Long));
        Assert.Null(Feed(tracker, ClosingAt(16, 45, BuyClose + 0.0020), Bias.Long));
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnD1Closed_AfterAFinishedDay_TheNextReferenceIsArmedAndCanConfirmBefore0800()
    {
        BreakoutTracker tracker = WithLastConfirm(Eight);
        Assert.Null(Feed(tracker, ClosingAt(8, 0, 1.0820), Bias.Long));

        tracker.OnD1Closed(BarBuilder.One(TimeFrame.D1, DayOpen.AddDays(1.0), 1.0840, 1.0870, 1.0820, 1.0860));
        AssertArmed(tracker);
        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 3, 5, 7, 30), 1.0880), Bias.Long));

        Assert.Equal("BH-20260303-2200", setup.Id);
    }

    // ------------------------------------------------------------------ the boundary, to the tick

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_LastConfirmOneTickAfterTheClose_Confirms()
    {
        BreakoutTracker tracker = WithLastConfirm(new TimeSpan(7, 45, 0) + TimeSpan.FromTicks(1));

        Assert.Equal(Side.Buy, Require(Feed(tracker, ClosingAt(7, 45, BuyClose), Bias.Long)).Side);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_LastConfirmEqualToTheClose_Finishes()
    {
        BreakoutTracker tracker = WithLastConfirm(new TimeSpan(7, 45, 0));

        Assert.Null(Feed(tracker, ClosingAt(7, 45, BuyClose), Bias.Long));
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_LastConfirmOneTickBeforeTheClose_Finishes()
    {
        BreakoutTracker tracker = WithLastConfirm(new TimeSpan(7, 45, 0) - TimeSpan.FromTicks(1));

        Assert.Null(Feed(tracker, ClosingAt(7, 45, BuyClose), Bias.Long));
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_NonDefaultLastConfirm1030_ConfirmsAt1015AndFinishesAt1030()
    {
        BreakoutTracker before = WithLastConfirm(new TimeSpan(10, 30, 0));
        BreakoutTracker at = WithLastConfirm(new TimeSpan(10, 30, 0));

        Assert.Equal(Side.Buy, Require(Feed(before, ClosingAt(10, 15, BuyClose), Bias.Long)).Side);
        Assert.Null(Feed(at, ClosingAt(10, 30, BuyClose), Bias.Long));
        AssertDone(at);
    }

    // ------------------------------------------------------------------ bars that are not checked

    [Theory]
    [Trait("Req", "SIG-029")]
    [InlineData(6, 45)] // before the window start
    [InlineData(17, 0)] // at the window end
    [InlineData(17, 15)] // after the window end
    public void OnM15Closed_BarNotCheckedAfterLastConfirm_ChangesNothing(int hour, int minute)
    {
        BreakoutTracker tracker = WithLastConfirm(Eight);

        Assert.Null(Feed(tracker, ClosingAt(hour, minute, BuyClose), Bias.Long));
        AssertArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_BarClosingWithTheReferenceBar_ChangesNothing()
    {
        // Opens 2026-03-03 21:45, before the reference bar closes: not checked (SIG-022), so 22:00 does not finish it.
        BreakoutTracker tracker = WithLastConfirm(Eight);

        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 21, 45), BuyClose), Bias.Long));
        AssertArmed(tracker);
    }

    // ------------------------------------------------------------------ off value: the rule before

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_LastConfirmEqualToTheWindowEnd_BarClosing1645Confirms()
    {
        BreakoutTracker explicitOff = WithLastConfirm(TimeSpan.FromHours(17.0));
        BreakoutTracker defaults = Armed();

        BreakoutSetup a = Require(Feed(explicitOff, ClosingAt(16, 45, BuyClose), Bias.Long));
        BreakoutSetup b = Require(Feed(defaults, ClosingAt(16, 45, BuyClose), Bias.Long));

        Assert.Equal(b, a);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_LastConfirm1645_BarClosing1645Finishes()
    {
        BreakoutTracker tracker = WithLastConfirm(new TimeSpan(16, 45, 0));

        Assert.Null(Feed(tracker, ClosingAt(16, 45, BuyClose), Bias.Long));
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void OnM15Closed_LastConfirmNotSetAndWindowEnd1200_FollowsTheWindowEnd()
    {
        var parameters = new BreakoutParameters { WindowEndUtc = TimeSpan.FromHours(12.0) };
        BreakoutTracker tracker = Armed(parameters);

        Assert.Equal(TimeSpan.FromHours(12.0), parameters.LastConfirmUtc);
        Assert.Equal(Side.Buy, Require(Feed(tracker, ClosingAt(11, 45, BuyClose), Bias.Long)).Side);
    }

    // ------------------------------------------------------------------ with the trend filter (SIG-028)

    [Fact]
    [Trait("Req", "SIG-029")]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_TrendBlocksAt0745_ThenTheBarAt0800FinishesTheReference()
    {
        var tracker = new BreakoutTracker(new BreakoutParameters { TrendSlowEmaPeriod = 200, LastConfirmUtc = Eight }, RewardRisk);
        tracker.OnD1Closed(ReferenceBar(), 1.0800, 1.0850);

        Assert.Null(Feed(tracker, ClosingAt(7, 45, BuyClose), Bias.Long));
        AssertArmed(tracker);
        Assert.Null(Feed(tracker, ClosingAt(8, 0, BuyClose), Bias.Long));
        AssertDone(tracker);
    }
}
