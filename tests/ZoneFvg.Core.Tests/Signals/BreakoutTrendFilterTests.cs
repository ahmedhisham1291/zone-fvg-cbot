using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Tests.BreakoutData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Breakout trend filter in the tracker (SIG-028). Worked example of <see cref="BreakoutData"/>:
/// reference high 1.0850 and low 1.0780; the buy bar closes at 1.0858 and the sell bar at 1.0770,
/// both opening 2026-03-04 08:00 (inside the window). The fast and slow EMA values of the last
/// closed D1 bar are passed with the reference bar. Above and below are strict and use PriceMath
/// with its default tolerance (1e-9).
/// </summary>
public class BreakoutTrendFilterTests
{
    private const double BuyClose = 1.0858;
    private const double SellClose = 1.0770;
    private const double Slow = 1.0850;

    private static readonly DateTime At0800 = Utc(2026, 3, 4, 8, 0);

    private static BreakoutTracker WithTrend(double? fast, double? slow, int slowPeriod = 200)
    {
        var tracker = new BreakoutTracker(new BreakoutParameters { TrendSlowEmaPeriod = slowPeriod }, RewardRisk);
        tracker.OnD1Closed(ReferenceBar(), fast, slow);
        return tracker;
    }

    private static void AssertUnchangedArmed(BreakoutTracker tracker)
    {
        AssertArmed(tracker);
        Assert.Equal(new BreakoutReference(DayOpen, High, Low, true), tracker.Reference);
    }

    // ------------------------------------------------------------------ buy

    [Theory]
    [Trait("Req", "SIG-028")]
    [InlineData(2)]
    [InlineData(200)]
    public void OnM15Closed_BuyFastAboveSlow_ConfirmsAndFinishesTheReference(int slowPeriod)
    {
        BreakoutTracker tracker = WithTrend(1.0900, Slow, slowPeriod);

        BreakoutSetup setup = Require(Feed(tracker, Flat(At0800, BuyClose), Bias.Long));

        Assert.Equal(Side.Buy, setup.Side);
        Assert.Equal("BH-20260302-2200", setup.Id);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_BuyFastEqualToSlow_ConfirmsNothingAndStaysArmed()
    {
        BreakoutTracker tracker = WithTrend(Slow, Slow);

        Assert.Null(Feed(tracker, Flat(At0800, BuyClose), Bias.Long));
        AssertUnchangedArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_BuyFastBelowSlow_ConfirmsNothingAndStaysArmed()
    {
        BreakoutTracker tracker = WithTrend(1.0800, Slow);

        Assert.Null(Feed(tracker, Flat(At0800, BuyClose), Bias.Long));
        AssertUnchangedArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_BuyFastAboveSlowWithinTheTolerance_ConfirmsNothingAndStaysArmed()
    {
        // 1.0850 + 5e-10 is more than zero but not more than 1e-9 above the slow EMA: a raw comparison would confirm.
        BreakoutTracker tracker = WithTrend(Slow + 5e-10, Slow);

        Assert.Null(Feed(tracker, Flat(At0800, BuyClose), Bias.Long));
        AssertUnchangedArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_BuyFastBelowSlowWithinTheTolerance_ConfirmsNothingAndStaysArmed()
    {
        BreakoutTracker tracker = WithTrend(Slow - 5e-10, Slow);

        Assert.Null(Feed(tracker, Flat(At0800, BuyClose), Bias.Long));
        AssertUnchangedArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_BuyFastAboveSlowBeyondTheTolerance_Confirms()
    {
        BreakoutTracker tracker = WithTrend(Slow + 2e-9, Slow);

        BreakoutSetup setup = Require(Feed(tracker, Flat(At0800, BuyClose), Bias.Long));

        Assert.Equal(Side.Buy, setup.Side);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_BuyBlocked_LaterBarOfTheSameDayIsBlockedAsWell()
    {
        BreakoutTracker tracker = WithTrend(1.0800, Slow);

        Assert.Null(Feed(tracker, Flat(At0800, BuyClose), Bias.Long));
        Assert.Null(Feed(tracker, Flat(At0800.AddMinutes(15.0), BuyClose + 0.0010), Bias.Long));
        AssertUnchangedArmed(tracker);
    }

    // ------------------------------------------------------------------ sell (mirror)

    [Theory]
    [Trait("Req", "SIG-028")]
    [InlineData(2)]
    [InlineData(200)]
    public void OnM15Closed_SellFastBelowSlow_ConfirmsAndFinishesTheReference(int slowPeriod)
    {
        BreakoutTracker tracker = WithTrend(1.0800, Slow, slowPeriod);

        BreakoutSetup setup = Require(Feed(tracker, Flat(At0800, SellClose), Bias.Short));

        Assert.Equal(Side.Sell, setup.Side);
        Assert.Equal("BL-20260302-2200", setup.Id);
        AssertDone(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_SellFastEqualToSlow_ConfirmsNothingAndStaysArmed()
    {
        BreakoutTracker tracker = WithTrend(Slow, Slow);

        Assert.Null(Feed(tracker, Flat(At0800, SellClose), Bias.Short));
        AssertUnchangedArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_SellFastAboveSlow_ConfirmsNothingAndStaysArmed()
    {
        // The trend that allows a buy: a sell rule copied from the buy rule would confirm here.
        BreakoutTracker tracker = WithTrend(1.0900, Slow);

        Assert.Null(Feed(tracker, Flat(At0800, SellClose), Bias.Short));
        AssertUnchangedArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_SellFastBelowSlowWithinTheTolerance_ConfirmsNothingAndStaysArmed()
    {
        BreakoutTracker tracker = WithTrend(Slow - 5e-10, Slow);

        Assert.Null(Feed(tracker, Flat(At0800, SellClose), Bias.Short));
        AssertUnchangedArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_SellFastAboveSlowWithinTheTolerance_ConfirmsNothingAndStaysArmed()
    {
        BreakoutTracker tracker = WithTrend(Slow + 5e-10, Slow);

        Assert.Null(Feed(tracker, Flat(At0800, SellClose), Bias.Short));
        AssertUnchangedArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_SellFastBelowSlowBeyondTheTolerance_Confirms()
    {
        BreakoutTracker tracker = WithTrend(Slow - 2e-9, Slow);

        BreakoutSetup setup = Require(Feed(tracker, Flat(At0800, SellClose), Bias.Short));

        Assert.Equal(Side.Sell, setup.Side);
        AssertDone(tracker);
    }

    // ------------------------------------------------------------------ no EMA value: fail closed

    [Theory]
    [Trait("Req", "SIG-028")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void OnM15Closed_EmaValueMissing_ConfirmsNothingOnEitherSideAndStaysArmed(bool hasFast, bool hasSlow)
    {
        double? fast = hasFast ? 1.0900 : (double?)null;
        double? slow = hasSlow ? Slow : (double?)null;
        BreakoutTracker buy = WithTrend(fast, slow);
        BreakoutTracker sell = WithTrend(hasFast ? 1.0800 : (double?)null, slow);

        Assert.Null(Feed(buy, Flat(At0800, BuyClose), Bias.Long));
        Assert.Null(Feed(sell, Flat(At0800, SellClose), Bias.Short));
        AssertUnchangedArmed(buy);
        AssertUnchangedArmed(sell);
    }

    [Theory]
    [Trait("Req", "SIG-028")]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void OnM15Closed_EmaValueNotFinite_ConfirmsNothingAndStaysArmed(double bad)
    {
        BreakoutTracker fastBad = WithTrend(bad, Slow);
        BreakoutTracker slowBad = WithTrend(1.0900, bad);

        Assert.Null(Feed(fastBad, Flat(At0800, BuyClose), Bias.Long));
        Assert.Null(Feed(slowBad, Flat(At0800, BuyClose), Bias.Long));
        AssertUnchangedArmed(fastBad);
        AssertUnchangedArmed(slowBad);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_FilterOnAndReferenceFromTheOverloadWithoutEmaValues_ConfirmsNothing()
    {
        var tracker = new BreakoutTracker(new BreakoutParameters { TrendSlowEmaPeriod = 200 }, RewardRisk);
        tracker.OnD1Closed(ReferenceBar());

        Assert.Null(Feed(tracker, Flat(At0800, BuyClose), Bias.Long));
        AssertUnchangedArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnD1Closed_NewReference_ReplacesTheEmaValuesOfTheOldOne()
    {
        BreakoutTracker tracker = WithTrend(1.0800, Slow);
        Bar next = BarBuilder.One(TimeFrame.D1, DayOpen.AddDays(1.0), 1.0840, 1.0870, 1.0820, 1.0860);
        tracker.OnD1Closed(next, 1.0900, Slow);

        BreakoutSetup setup = Require(Feed(tracker, Flat(At0800.AddDays(1.0), 1.0880), Bias.Long));

        Assert.Equal("BH-20260303-2200", setup.Id);
    }

    // ------------------------------------------------------------------ filter off: the rule before (SIG-023 alone)

    [Theory]
    [Trait("Req", "SIG-028")]
    [InlineData(1.0800, 1.0850)]
    [InlineData(1.0850, 1.0850)]
    [InlineData(1.0900, 1.0850)]
    public void OnM15Closed_FilterOff_ConfirmsBothSidesWhateverTheEmaValues(double fast, double slow)
    {
        BreakoutTracker buy = WithTrend(fast, slow, 0);
        BreakoutTracker sell = WithTrend(fast, slow, 0);
        BreakoutTracker noValues = WithTrend(null, null, 0);

        Assert.Equal(Side.Buy, Require(Feed(buy, Flat(At0800, BuyClose), Bias.Long)).Side);
        Assert.Equal(Side.Sell, Require(Feed(sell, Flat(At0800, SellClose), Bias.Short)).Side);
        Assert.Equal(Side.Buy, Require(Feed(noValues, Flat(At0800, BuyClose), Bias.Long)).Side);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_FilterOff_SetupEqualsTheOneOfTheOverloadWithoutEmaValues()
    {
        BreakoutTracker withValues = WithTrend(1.0800, Slow, 0);
        BreakoutTracker without = Armed();

        BreakoutSetup a = Require(Feed(withValues, Flat(At0800, BuyClose), Bias.Long));
        BreakoutSetup b = Require(Feed(without, Flat(At0800, BuyClose), Bias.Long));

        Assert.Equal(b, a);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnM15Closed_FilterOnButCloseInsideTheRange_ChangesNothing()
    {
        BreakoutTracker tracker = WithTrend(1.0900, Slow);

        Assert.Null(Feed(tracker, Flat(At0800, 1.0820), Bias.Long));
        AssertUnchangedArmed(tracker);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void OnD1Closed_NullBar_Throws()
    {
        var tracker = new BreakoutTracker(new BreakoutParameters { TrendSlowEmaPeriod = 200 }, RewardRisk);

        Assert.Throws<ArgumentNullException>(() => tracker.OnD1Closed(null!, 1.0, 1.0));
        Assert.Null(tracker.Reference);
    }
}
