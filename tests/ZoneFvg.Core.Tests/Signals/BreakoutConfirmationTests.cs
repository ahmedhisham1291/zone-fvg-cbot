using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Tests.BreakoutData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Breakout confirmation (SIG-023). Worked example of <see cref="BreakoutData"/>: reference high
/// 1.0850 and low 1.0780. Bars open 2026-03-04 08:00 unless stated, inside the window.
/// </summary>
public class BreakoutConfirmationTests
{
    private static readonly DateTime At0800 = Utc(2026, 3, 4, 8, 0);

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_LongCloseAboveTheHigh_ConfirmsABuyAndFinishesTheReference()
    {
        BreakoutTracker tracker = Armed();

        BreakoutSetup setup = Require(Feed(tracker, Flat(At0800, 1.0858), Bias.Long));

        Assert.Equal(Side.Buy, setup.Side);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_LongCloseExactlyAtTheHigh_DoesNotConfirm()
    {
        BreakoutTracker tracker = Armed();

        Assert.Null(Feed(tracker, Flat(At0800, 1.0850), Bias.Long));
        AssertArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_LongCloseOneTenthPipAboveTheHigh_Confirms()
    {
        BreakoutTracker tracker = Armed();

        BreakoutSetup setup = Require(Feed(tracker, Flat(At0800, 1.08501), Bias.Long));

        Assert.Equal(Side.Buy, setup.Side);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_LongWickAboveTheHighWithCloseInside_DoesNotConfirm()
    {
        BreakoutTracker tracker = Armed();

        Assert.Null(Feed(tracker, M15(At0800, 1.0845, 1.0862, 1.0843, 1.0849), Bias.Long));
        AssertArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_ShortCloseBelowTheLow_ConfirmsASellAndFinishesTheReference()
    {
        BreakoutTracker tracker = Armed();

        BreakoutSetup setup = Require(Feed(tracker, Flat(At0800, 1.0772), Bias.Short));

        Assert.Equal(Side.Sell, setup.Side);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_ShortCloseExactlyAtTheLow_DoesNotConfirm()
    {
        BreakoutTracker tracker = Armed();

        Assert.Null(Feed(tracker, Flat(At0800, 1.0780), Bias.Short));
        AssertArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_ShortCloseOneTenthPipBelowTheLow_Confirms()
    {
        BreakoutTracker tracker = Armed();

        BreakoutSetup setup = Require(Feed(tracker, Flat(At0800, 1.07799), Bias.Short));

        Assert.Equal(Side.Sell, setup.Side);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_ShortWickBelowTheLowWithCloseInside_DoesNotConfirm()
    {
        BreakoutTracker tracker = Armed();

        Assert.Null(Feed(tracker, M15(At0800, 1.0785, 1.0788, 1.0768, 1.0781), Bias.Short));
        AssertArmed(tracker);
    }

    [Theory]
    [Trait("Req", "SIG-023")]
    [InlineData(1.0858)] // above the high
    [InlineData(1.0772)] // below the low
    public void OnM15Closed_NeutralBias_NothingConfirms(double close)
    {
        BreakoutTracker tracker = Armed();

        Assert.Null(Feed(tracker, Flat(At0800, close), Bias.Neutral));
        AssertArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_LongCloseBelowTheLow_ChangesNothingAndALaterBreakConfirms()
    {
        BreakoutTracker tracker = Armed();

        Assert.Null(Feed(tracker, Flat(At0800, 1.0772), Bias.Long));
        AssertArmed(tracker);

        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 3, 4, 8, 15), 1.0858), Bias.Long));
        Assert.Equal(Side.Buy, setup.Side);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_ShortCloseAboveTheHigh_ChangesNothingAndALaterBreakConfirms()
    {
        BreakoutTracker tracker = Armed();

        Assert.Null(Feed(tracker, Flat(At0800, 1.0858), Bias.Short));
        AssertArmed(tracker);

        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 3, 4, 8, 15), 1.0772), Bias.Short));
        Assert.Equal(Side.Sell, setup.Side);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_AfterAConfirmation_ASecondBreakReturnsNull()
    {
        BreakoutTracker tracker = Armed();
        Require(Feed(tracker, Flat(At0800, 1.0858), Bias.Long));
        BreakoutReference? done = tracker.Reference;

        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 4, 8, 15), 1.0870), Bias.Long));
        Assert.Equal(done, tracker.Reference);
    }

    [Fact]
    [Trait("Req", "SIG-023")]
    public void OnM15Closed_AfterAConfirmation_StaysDoneUntilTheNextD1Bar()
    {
        BreakoutTracker tracker = Armed();
        Require(Feed(tracker, Flat(At0800, 1.0858), Bias.Long));
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 4, 9, 0), 1.0770), Bias.Short));
        AssertDone(tracker);

        tracker.OnD1Closed(BarBuilder.One(TimeFrame.D1, Utc(2026, 3, 3, 22, 0), 1.0840, 1.0900, 1.0820, 1.0880));

        AssertArmed(tracker);
    }
}
