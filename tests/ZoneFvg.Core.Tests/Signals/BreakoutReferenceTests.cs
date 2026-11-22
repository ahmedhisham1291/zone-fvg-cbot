using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Tests.BreakoutData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Breakout reference and checked bars (SIG-022). Worked example of <see cref="BreakoutData"/>:
/// the reference bar opens 2026-03-02 22:00 and closes 2026-03-03 22:00; the reference high is
/// 1.0850, so a close of 1.0858 with a long bias confirms whenever the bar is checked.
/// </summary>
public class BreakoutReferenceTests
{
    [Fact]
    [Trait("Req", "SIG-022")]
    public void Reference_BeforeAnyD1Bar_IsNullAndNoBarConfirms()
    {
        var tracker = new BreakoutTracker(new BreakoutParameters(), RewardRisk);

        Assert.Null(tracker.Reference);
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 4, 8, 0), 1.0858), Bias.Long));
        Assert.Null(tracker.Reference);
    }

    [Fact]
    [Trait("Req", "SIG-022")]
    public void OnD1Closed_FirstBar_BecomesTheArmedReference()
    {
        BreakoutTracker tracker = Armed();

        Assert.Equal(new BreakoutReference(Utc(2026, 3, 2, 22, 0), 1.0850, 1.0780, true), tracker.Reference);
    }

    [Theory]
    [Trait("Req", "SIG-022")]
    [InlineData(6, 45)] // closes 07:00, the window start
    [InlineData(8, 0)] // closes 08:15
    [InlineData(16, 30)] // closes 16:45, the last close before the window end
    public void OnM15Closed_BarClosingInsideTheWindow_IsCheckedAndConfirms(int hour, int minute)
    {
        BreakoutTracker tracker = Armed();

        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 3, 4, hour, minute), 1.0858), Bias.Long));

        Assert.Equal(Side.Buy, setup.Side);
        AssertDone(tracker);
    }

    [Theory]
    [Trait("Req", "SIG-022")]
    [InlineData(6, 30)] // closes 06:45, one bar before the window start
    [InlineData(16, 45)] // closes 17:00, the window end
    [InlineData(17, 0)] // closes 17:15
    [InlineData(2, 0)] // closes 02:15
    public void OnM15Closed_BarClosingOutsideTheWindow_IsNotChecked(int hour, int minute)
    {
        BreakoutTracker tracker = Armed();

        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 4, hour, minute), 1.0858), Bias.Long));
        AssertArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-022")]
    public void OnM15Closed_BarClosingTogetherWithTheReferenceBar_IsNotChecked()
    {
        // Rule (a) with the window 00:00 to 23:00: the bar opening 2026-03-03 21:45 closes at
        // 22:00, inside the window, but it opened before the reference bar closed.
        BreakoutTracker tracker = Armed(new BreakoutParameters { WindowStartUtc = TimeSpan.Zero, WindowEndUtc = TimeSpan.FromHours(23.0) });

        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 21, 45), 1.0858), Bias.Long));
        AssertArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-022")]
    public void OnM15Closed_BarOpeningAtTheReferenceClose_IsCheckedAndConfirms()
    {
        BreakoutTracker tracker = Armed(new BreakoutParameters { WindowStartUtc = TimeSpan.Zero, WindowEndUtc = TimeSpan.FromHours(23.0) });

        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 3, 3, 22, 0), 1.0858), Bias.Long));

        Assert.Equal(Utc(2026, 3, 3, 22, 15), setup.ConfirmationCloseUtc);
        AssertDone(tracker);
    }

    [Theory]
    [Trait("Req", "SIG-022")]
    [InlineData(3, 23, 45, 0, 0)] // closes 2026-03-04 00:00, the start of the window 00:00 to 23:00
    [InlineData(4, 2, 45, 3, 0)] // closes 2026-03-04 03:00, before the default start of 07:00
    public void OnM15Closed_WindowFromMidnight_BarClosingBeforeSevenIsChecked(int day, int hour, int minute, int closeHour, int closeMinute)
    {
        BreakoutTracker tracker = Armed(new BreakoutParameters { WindowStartUtc = TimeSpan.Zero, WindowEndUtc = TimeSpan.FromHours(23.0) });

        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 3, day, hour, minute), 1.0858), Bias.Long));

        Assert.Equal(Utc(2026, 3, 4, closeHour, closeMinute), setup.ConfirmationCloseUtc);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-022")]
    public void OnD1Closed_AfterAConfirmation_NewReferenceIsArmedWithTheNewValues()
    {
        BreakoutTracker tracker = Armed();
        Require(Feed(tracker, Flat(Utc(2026, 3, 4, 8, 0), 1.0858), Bias.Long));

        tracker.OnD1Closed(BarBuilder.One(TimeFrame.D1, Utc(2026, 3, 3, 22, 0), 1.0840, 1.0900, 1.0820, 1.0880));

        Assert.Equal(new BreakoutReference(Utc(2026, 3, 3, 22, 0), 1.0900, 1.0820, true), tracker.Reference);
        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 3, 5, 8, 0), 1.0905), Bias.Long));
        Assert.Equal("BH-20260303-2200", setup.Id);
        AssertPrice(1.0900, setup.Level, "level");
    }

    [Fact]
    [Trait("Req", "SIG-022")]
    public void OnD1Closed_UnconfirmedReference_IsReplacedWithoutAResult()
    {
        BreakoutTracker tracker = Armed();

        tracker.OnD1Closed(BarBuilder.One(TimeFrame.D1, Utc(2026, 3, 3, 22, 0), 1.0840, 1.0900, 1.0820, 1.0880));

        Assert.Equal(new BreakoutReference(Utc(2026, 3, 3, 22, 0), 1.0900, 1.0820, true), tracker.Reference);

        // 1.0858 is above the old high 1.0850 but not above the new high 1.0900.
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 5, 8, 0), 1.0858), Bias.Long));
        AssertArmed(tracker);
    }

    [Theory]
    [Trait("Req", "SIG-022")]
    [InlineData(22, 0)] // the same open time
    [InlineData(21, 0)] // an earlier open time
    public void OnD1Closed_BarNotOpeningAfterTheReference_ThrowsAndChangesNothing(int hour, int minute)
    {
        BreakoutTracker tracker = Armed();
        BreakoutReference? before = tracker.Reference;

        Assert.Throws<InvalidOperationException>(() => tracker.OnD1Closed(BarBuilder.One(TimeFrame.D1, Utc(2026, 3, 2, hour, minute), 1.0800, 1.0900, 1.0700, 1.0850)));

        Assert.Equal(before, tracker.Reference);
    }

    [Fact]
    [Trait("Req", "SIG-022")]
    public void OnD1Closed_BarNotOpeningAfterADoneReference_ThrowsAndKeepsItDone()
    {
        BreakoutTracker tracker = Armed();
        Require(Feed(tracker, Flat(Utc(2026, 3, 4, 8, 0), 1.0858), Bias.Long));
        BreakoutReference? before = tracker.Reference;

        Assert.Throws<InvalidOperationException>(() => tracker.OnD1Closed(ReferenceBar()));

        Assert.Equal(before, tracker.Reference);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-022")]
    public void OnD1Closed_NullBar_Throws()
    {
        var tracker = new BreakoutTracker(new BreakoutParameters(), RewardRisk);

        Assert.Throws<ArgumentNullException>(() => tracker.OnD1Closed(null!));
        Assert.Null(tracker.Reference);
    }

    [Fact]
    [Trait("Req", "SIG-022")]
    public void OnM15Closed_NullBar_Throws()
    {
        BreakoutTracker tracker = Armed();

        Assert.Throws<ArgumentNullException>(() => tracker.OnM15Closed(null!, Bias.Long, AtrM15, AtrH4));
        AssertArmed(tracker);
    }
}
