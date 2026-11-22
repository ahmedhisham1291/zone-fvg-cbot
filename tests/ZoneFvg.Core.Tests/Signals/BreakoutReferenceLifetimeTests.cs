using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Tests.BreakoutData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// A reference serves one day (SIG-027). Worked example of <see cref="BreakoutData"/>: the
/// reference bar opens Monday 2026-03-02 22:00 and closes Tuesday 2026-03-03 22:00; high 1.0850,
/// low 1.0780. The tracker notes the open time of the first M15 bar it receives that opens at or
/// after the reference bar's close (the anchor); an M15 bar that opens 24 hours or more after the
/// anchor is not checked and changes nothing, until a D1 bar closes and gives a new reference.
/// Weekdays: Monday 2, Tuesday 3, Wednesday 4, Thursday 5, Friday 6, Sunday 8, Monday 9 and
/// Tuesday 10 March 2026.
/// </summary>
public class BreakoutReferenceLifetimeTests
{
    private const double AboveHigh = 1.0858;
    private const double BelowLow = 1.0772;
    private const double Inside = 1.0840;

    private static readonly BreakoutParameters WideWindow = new() { WindowStartUtc = TimeSpan.Zero, WindowEndUtc = TimeSpan.FromHours(23.0) };

    private static double Beyond(Bias bias)
    {
        return bias == Bias.Long ? AboveHigh : BelowLow;
    }

    // A D1 bar with the worked example's high and low that opens at the given time.
    private static Bar Day(DateTime openUtc)
    {
        return BarBuilder.One(TimeFrame.D1, openUtc, 1.0800, High, Low, 1.0840);
    }

    // Asserts that the bar returns null and changes nothing: the reference is the same Armed record.
    private static void AssertNotChecked(BreakoutTracker tracker, Bar bar, Bias bias)
    {
        BreakoutReference? before = tracker.Reference;

        Assert.Null(Feed(tracker, bar, bias));

        AssertArmed(tracker);
        Assert.Equal(before, tracker.Reference);
    }

    // ------------------------------------------------------------------ inside and outside the day

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnM15Closed_InsideTheDayDefaultWindow_TheWorkedExampleBuyConfirmsExactlyAsToday()
    {
        // The first bar 2026-03-03 22:00 closes 22:15, outside the window. The buy bar opens 10 hours later.
        BreakoutTracker tracker = Armed();
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 22, 0), AboveHigh), Bias.Long));
        AssertArmed(tracker);
        Bar buy = M15(Utc(2026, 3, 4, 8, 0), 1.0845, 1.0862, 1.0843, 1.0858);

        BreakoutSetup setup = Require(Feed(tracker, buy, Bias.Long));

        BreakoutSetup today = Require(Feed(Armed(), buy, Bias.Long));
        Assert.Equal(BreakoutFeed.Key(today), BreakoutFeed.Key(setup));
        Assert.Equal("BH-20260302-2200", setup.Id);
        Assert.Equal(Side.Buy, setup.Side);
        AssertPrice(1.0863, setup.Entry, "entry");
        AssertPrice(1.0823, setup.Stop, "stop");
        AssertPrice(1.0943, setup.Target, "target");
        AssertPrice(High, setup.Level, "level");
        Assert.Equal(Utc(2026, 3, 4, 8, 15), setup.ConfirmationCloseUtc);
        AssertDone(tracker);
    }

    [Theory]
    [Trait("Req", "SIG-027")]
    [InlineData(Bias.Long)]
    [InlineData(Bias.Short)]
    public void OnM15Closed_OutsideTheDay_NotCheckedAndNothingChanges(Bias bias)
    {
        // 2026-03-05 08:00 is 34 hours after the first bar 2026-03-03 22:00; no D1 bar in between.
        BreakoutTracker tracker = Armed();
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 22, 0), Inside), Bias.Long));

        AssertNotChecked(tracker, Flat(Utc(2026, 3, 5, 8, 0), Beyond(bias)), bias);

        // The anchor kept its value: the next bar is not checked either.
        AssertNotChecked(tracker, Flat(Utc(2026, 3, 5, 8, 15), Beyond(bias)), bias);
    }

    [Theory]
    [Trait("Req", "SIG-027")]
    [InlineData(Bias.Long)]
    [InlineData(Bias.Short)]
    public void OnM15Closed_OutsideTheDay_BarWithWicksBeyondBothLevels_NotCheckedEither(Bias bias)
    {
        // Whatever the bar closes at: a bar that also has a wick beyond both levels.
        BreakoutTracker tracker = Armed();
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 22, 0), Inside), Bias.Long));

        AssertNotChecked(tracker, M15(Utc(2026, 3, 5, 9, 0), Inside, 1.0900, 1.0700, Beyond(bias)), bias);
    }

    [Theory]
    [Trait("Req", "SIG-027")]
    [InlineData(Bias.Long)]
    [InlineData(Bias.Short)]
    public void OnM15Closed_TwentyThreeHoursFortyFiveAfterTheFirstBar_IsCheckedAndConfirms(Bias bias)
    {
        // Window 00:00 to 23:00. The first bar 2026-03-03 22:00 closes 22:15 inside the window, inside the range.
        BreakoutTracker tracker = Armed(WideWindow);
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 22, 0), Inside), bias));
        AssertArmed(tracker);

        // 2026-03-04 21:45 is 23 h 45 min after it and closes at 22:00, inside the window.
        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 3, 4, 21, 45), Beyond(bias)), bias));

        Assert.Equal(bias == Bias.Long ? Side.Buy : Side.Sell, setup.Side);
        Assert.Equal(Utc(2026, 3, 4, 22, 0), setup.ConfirmationCloseUtc);
        AssertDone(tracker);
    }

    [Theory]
    [Trait("Req", "SIG-027")]
    [InlineData(Bias.Long)]
    [InlineData(Bias.Short)]
    public void OnM15Closed_ExactlyTwentyFourHoursAfterTheFirstBar_IsNotChecked(Bias bias)
    {
        // Window 00:00 to 23:00: 2026-03-04 22:00 closes at 22:15, inside the window, exactly 24 hours after the first bar.
        BreakoutTracker tracker = Armed(WideWindow);
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 22, 0), Inside), bias));

        AssertNotChecked(tracker, Flat(Utc(2026, 3, 4, 22, 0), Beyond(bias)), bias);
    }

    // ------------------------------------------------------------------ the weekend and a series one bar behind

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnM15Closed_ThursdayReference_MondayBarAfterTheSundayOpenConfirms()
    {
        // The reference bar opens Thursday 2026-03-05 22:00 and closes Friday 22:00. The first bar after it
        // opens Sunday 2026-03-08 22:00; Monday 08:00 is 10 hours later.
        var tracker = new BreakoutTracker(WideWindow, RewardRisk);
        tracker.OnD1Closed(Day(Utc(2026, 3, 5, 22, 0)));
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 8, 22, 0), Inside), Bias.Long));

        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 3, 9, 8, 0), AboveHigh), Bias.Long));

        Assert.Equal("BH-20260305-2200", setup.Id);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnM15Closed_ThursdayReferenceWithoutANewD1Bar_MondayEveningAndTuesdayAreNotChecked()
    {
        // Monday 2026-03-09 22:00 is exactly 24 hours after the first bar (Sunday 22:00), Tuesday 08:00 is 34 hours after it.
        var tracker = new BreakoutTracker(WideWindow, RewardRisk);
        tracker.OnD1Closed(Day(Utc(2026, 3, 5, 22, 0)));
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 8, 22, 0), Inside), Bias.Long));

        AssertNotChecked(tracker, Flat(Utc(2026, 3, 9, 22, 0), AboveHigh), Bias.Long);
        AssertNotChecked(tracker, Flat(Utc(2026, 3, 10, 8, 0), AboveHigh), Bias.Long);
    }

    [Theory]
    [Trait("Req", "SIG-027")]
    [InlineData(Bias.Long)]
    [InlineData(Bias.Short)]
    public void OnM15Closed_SeriesOneBarBehind_MondayIsNotCheckedAgainstWednesdaysRange(Bias bias)
    {
        // The reference bar opens Wednesday 2026-03-04 22:00 and closes Thursday 22:00; Friday's bar (Thursday
        // 22:00 to Friday 22:00) never arrives. Friday's M15 bars arrive from Thursday 22:00 to Friday 19:45.
        var tracker = new BreakoutTracker(new BreakoutParameters(), RewardRisk);
        tracker.OnD1Closed(Day(Utc(2026, 3, 4, 22, 0)));
        for (DateTime open = Utc(2026, 3, 5, 22, 0); open < Utc(2026, 3, 6, 20, 0); open = open.AddMinutes(15.0))
        {
            Assert.Null(Feed(tracker, Flat(open, Inside), bias));
        }

        AssertNotChecked(tracker, Flat(Utc(2026, 3, 9, 8, 0), Beyond(bias)), bias);
    }

    // ------------------------------------------------------------------ the anchor

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnM15Closed_BarBeforeTheReferenceClose_DoesNotSetTheAnchor_TheNextBarDoes()
    {
        // 2026-03-03 21:45 opens before the close at 22:00. The next bar received opens 2026-03-04 10:00:
        // 2026-03-05 09:45 is 23 h 45 min after it (36 hours after 21:45), 2026-03-05 10:00 is 24 hours after it.
        BreakoutTracker checkedOne = Armed();
        BreakoutTracker notChecked = Armed();
        foreach (BreakoutTracker tracker in new[] { checkedOne, notChecked })
        {
            Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 21, 45), Inside), Bias.Long));
            Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 4, 10, 0), Inside), Bias.Long));
        }

        BreakoutSetup setup = Require(Feed(checkedOne, Flat(Utc(2026, 3, 5, 9, 45), AboveHigh), Bias.Long));
        Assert.Equal(Utc(2026, 3, 5, 10, 0), setup.ConfirmationCloseUtc);
        AssertNotChecked(notChecked, Flat(Utc(2026, 3, 5, 10, 0), AboveHigh), Bias.Long);
    }

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnM15Closed_FirstBarAtTheReferenceCloseItself_IsTheAnchor()
    {
        // The bar that opens at the close (2026-03-03 22:00) sets the anchor: 2026-03-04 21:45 is checked, 22:00 is not.
        BreakoutTracker checkedOne = Armed(WideWindow);
        BreakoutTracker notChecked = Armed(WideWindow);
        Assert.Null(Feed(checkedOne, Flat(Utc(2026, 3, 3, 22, 0), Inside), Bias.Long));
        Assert.Null(Feed(notChecked, Flat(Utc(2026, 3, 3, 22, 0), Inside), Bias.Long));

        Require(Feed(checkedOne, Flat(Utc(2026, 3, 4, 21, 45), AboveHigh), Bias.Long));
        AssertNotChecked(notChecked, Flat(Utc(2026, 3, 4, 22, 0), AboveHigh), Bias.Long);
    }

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnM15Closed_BiasNeutral_SetsTheAnchor()
    {
        // 2026-03-04 10:00 with bias Neutral closes above the high and confirms nothing; 2026-03-05 10:00 is 24 hours later.
        BreakoutTracker tracker = Armed();
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 4, 10, 0), AboveHigh), Bias.Neutral));
        AssertArmed(tracker);

        AssertNotChecked(tracker, Flat(Utc(2026, 3, 5, 10, 0), AboveHigh), Bias.Long);
    }

    [Theory]
    [Trait("Req", "SIG-027")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OnM15Closed_MissingAtrValues_SetTheAnchor(bool withM15, bool withH4)
    {
        // 2026-03-04 10:00 closes inside the range with a missing ATR value; 2026-03-05 10:00 is 24 hours later.
        BreakoutTracker tracker = Armed();
        Assert.Null(tracker.OnM15Closed(Flat(Utc(2026, 3, 4, 10, 0), Inside), Bias.Long, withM15 ? AtrM15 : null, withH4 ? AtrH4 : null));
        AssertArmed(tracker);

        AssertNotChecked(tracker, Flat(Utc(2026, 3, 5, 10, 0), AboveHigh), Bias.Long);
    }

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnM15Closed_BarOutsideTheWindow_SetsTheAnchor()
    {
        // 2026-03-04 02:00 closes 02:15, outside the window, above the high; 2026-03-05 08:00 is 30 hours later.
        BreakoutTracker tracker = Armed();
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 4, 2, 0), AboveHigh), Bias.Long));
        AssertArmed(tracker);

        AssertNotChecked(tracker, Flat(Utc(2026, 3, 5, 8, 0), AboveHigh), Bias.Long);
    }

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnM15Closed_LaterBarsOfTheDay_DoNotMoveTheAnchor()
    {
        // Every bar from 2026-03-03 22:00 to 2026-03-04 21:30 is fed inside the range (window 00:00 to 23:00);
        // the anchor stays at the first: 21:45 is checked, and in another tracker 22:00 is not.
        BreakoutTracker checkedOne = Armed(WideWindow);
        BreakoutTracker notChecked = Armed(WideWindow);
        for (DateTime open = Utc(2026, 3, 3, 22, 0); open < Utc(2026, 3, 4, 21, 45); open = open.AddMinutes(15.0))
        {
            Assert.Null(Feed(checkedOne, Flat(open, Inside), Bias.Long));
            Assert.Null(Feed(notChecked, Flat(open, Inside), Bias.Long));
        }

        Assert.Null(Feed(notChecked, Flat(Utc(2026, 3, 4, 21, 45), Inside), Bias.Long));

        Require(Feed(checkedOne, Flat(Utc(2026, 3, 4, 21, 45), AboveHigh), Bias.Long));
        AssertNotChecked(notChecked, Flat(Utc(2026, 3, 4, 22, 0), AboveHigh), Bias.Long);
    }

    // ------------------------------------------------------------------ a new reference, a rejected D1 bar, a finished reference

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnD1Closed_NewReference_EmptiesTheAnchor()
    {
        // The old anchor is 2026-03-03 22:00. The new reference opens 2026-03-03 22:00 and closes 2026-03-04 22:00;
        // 2026-03-05 08:00 is 34 hours after the old anchor and the first bar of the new reference's day.
        BreakoutTracker tracker = Armed();
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 22, 0), Inside), Bias.Long));
        tracker.OnD1Closed(BarBuilder.One(TimeFrame.D1, Utc(2026, 3, 3, 22, 0), 1.0840, 1.0900, 1.0820, 1.0880));

        BreakoutSetup setup = Require(Feed(tracker, Flat(Utc(2026, 3, 5, 8, 0), 1.0905), Bias.Long));

        Assert.Equal("BH-20260303-2200", setup.Id);
        AssertPrice(1.0900, setup.Level, "level");
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnD1Closed_NewReference_ItsOwnDayCountsFromItsOwnFirstBar()
    {
        // New reference 2026-03-03 22:00 (closes 2026-03-04 22:00); its first bar opens 2026-03-04 22:00, so
        // 2026-03-05 22:00 is exactly 24 hours later (window 00:00 to 23:00).
        BreakoutTracker tracker = Armed(WideWindow);
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 22, 0), Inside), Bias.Long));
        tracker.OnD1Closed(BarBuilder.One(TimeFrame.D1, Utc(2026, 3, 3, 22, 0), 1.0840, 1.0900, 1.0820, 1.0880));
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 4, 22, 0), 1.0850), Bias.Long));

        AssertNotChecked(tracker, Flat(Utc(2026, 3, 5, 22, 0), 1.0905), Bias.Long);
    }

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnD1Closed_RejectedBar_KeepsTheAnchor()
    {
        // A D1 bar that does not open after the reference throws and changes nothing, the anchor included.
        BreakoutTracker tracker = Armed();
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 3, 22, 0), Inside), Bias.Long));

        Assert.Throws<InvalidOperationException>(() => tracker.OnD1Closed(ReferenceBar()));

        AssertNotChecked(tracker, Flat(Utc(2026, 3, 5, 8, 0), AboveHigh), Bias.Long);
    }

    [Theory]
    [Trait("Req", "SIG-027")]
    [InlineData(4, 9, 0)] // one hour after the confirmation, inside the 24 hours
    [InlineData(5, 9, 0)] // 25 hours after the first bar
    public void OnM15Closed_FinishedReference_LaterBarsReturnNullAsToday(int day, int hour, int minute)
    {
        BreakoutTracker tracker = Armed();
        Require(Feed(tracker, Flat(Utc(2026, 3, 4, 8, 0), AboveHigh), Bias.Long));
        BreakoutReference? done = tracker.Reference;

        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, day, hour, minute), 1.0870), Bias.Long));
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, day, hour, minute + 15), 1.0770), Bias.Short));

        Assert.Equal(done, tracker.Reference);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-027")]
    public void OnM15Closed_NoReference_NoAnchorBeforeTheFirstD1Bar()
    {
        // Bars before the first D1 bar set nothing: after the reference arrives, its own first bar is the anchor.
        var tracker = new BreakoutTracker(new BreakoutParameters(), RewardRisk);
        Assert.Null(Feed(tracker, Flat(Utc(2026, 3, 1, 8, 0), AboveHigh), Bias.Long));
        tracker.OnD1Closed(ReferenceBar());

        Require(Feed(tracker, Flat(Utc(2026, 3, 4, 8, 0), AboveHigh), Bias.Long));
    }
}
