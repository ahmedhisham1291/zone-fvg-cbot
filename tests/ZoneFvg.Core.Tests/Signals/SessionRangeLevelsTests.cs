using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Tests.SessionRangeData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The setup of the SessionRange mode (SIG-032): side, entry, stop, target, expiry, id, and at
/// most one setup per trading day. The range of every day here is 2000 to 2010.
/// </summary>
public class SessionRangeLevelsTests
{
    [Fact]
    [Trait("Req", "SIG-032")]
    public void Setup_LongBias_IsABuyStopAtTheRangeHigh()
    {
        SessionRangeSetup setup = Single(Tracker(), DayToEvaluation(Winter), Bias.Long);

        Assert.Equal("RH-20250115", setup.Id);
        Assert.Equal(Side.Buy, setup.Side);
        Assert.Equal(2010.0, setup.Entry);
        Assert.Equal(2005.0, setup.Stop);
        Assert.Equal(2020.0, setup.Target);
        Assert.Equal(Utc(2025, 1, 15, 17, 0), setup.ExpiryUtc);
        Assert.Equal(DateTimeKind.Utc, setup.ExpiryUtc.Kind);
        Assert.Equal(RangeHigh, setup.RangeHigh);
        Assert.Equal(RangeLow, setup.RangeLow);
        Assert.Equal(Winter, setup.TradingDate);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Setup_ShortBias_IsASellStopAtTheRangeLow()
    {
        SessionRangeSetup setup = Single(Tracker(), DayToEvaluation(Winter), Bias.Short);

        Assert.Equal("RL-20250115", setup.Id);
        Assert.Equal(Side.Sell, setup.Side);
        Assert.Equal(2000.0, setup.Entry);
        Assert.Equal(2005.0, setup.Stop);
        Assert.Equal(1990.0, setup.Target);
        Assert.Equal(Utc(2025, 1, 15, 17, 0), setup.ExpiryUtc);
        Assert.Equal(RangeHigh, setup.RangeHigh);
        Assert.Equal(RangeLow, setup.RangeLow);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData(Bias.Long, 2010.0, 2004.0, 2025.0)]
    [InlineData(Bias.Short, 2000.0, 2006.0, 1985.0)]
    public void Setup_StopAndTarget_FollowRangeStopAtrAndRewardRisk(Bias bias, double entry, double stop, double target)
    {
        // RangeStopAtr 1.5 x ATR(H4) 4 = stop distance 6; RewardRisk 2.5 x 6 = target distance 15.
        var tracker = new SessionRangeTracker(new SessionRangeParameters { StopAtrMult = 1.5 }, 2.5, LateFridayNoEntryUtc);

        SessionRangeSetup setup = Single(tracker, DayToEvaluation(Winter), bias, AtrD1, 4.0);

        Assert.True(PriceMath.AlmostEqual(entry, setup.Entry, PriceMath.DefaultTolerance));
        Assert.True(PriceMath.AlmostEqual(stop, setup.Stop, PriceMath.DefaultTolerance));
        Assert.True(PriceMath.AlmostEqual(target, setup.Target, PriceMath.DefaultTolerance));
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Setup_SummerDay_ExpiresAt1600Utc()
    {
        SessionRangeSetup setup = Single(Tracker(), DayToEvaluation(Summer), Bias.Long);

        Assert.Equal(Utc(2025, 7, 16, 16, 0), setup.ExpiryUtc);
        Assert.Equal("RH-20250716", setup.Id);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Setup_WinterDay_ExpiresAt1700Utc()
    {
        Assert.Equal(Utc(2025, 1, 15, 17, 0), Single(Tracker(), DayToEvaluation(Winter), Bias.Long).ExpiryUtc);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData(18, 0, 17, 0)]
    [InlineData(17, 0, 17, 0)]
    [InlineData(16, 45, 16, 45)]
    [InlineData(12, 0, 12, 0)]
    public void Setup_EvaluatedOnAWinterFriday_ExpiresNotLaterThanFridayNoEntryUtc(int fridayHour, int fridayMinute, int expiryHour, int expiryMinute)
    {
        // 17 January 2025 is a Friday; 12:00 New York is 17:00 UTC.
        var friday = new DateTime(2025, 1, 17);

        SessionRangeSetup setup = Single(Tracker(new TimeSpan(fridayHour, fridayMinute, 0)), DayToEvaluation(friday), Bias.Long);

        Assert.Equal(Utc(2025, 1, 17, expiryHour, expiryMinute), setup.ExpiryUtc);
        Assert.Equal(DateTimeKind.Utc, setup.ExpiryUtc.Kind);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData(17, 0, 16, 0)]
    [InlineData(16, 0, 16, 0)]
    [InlineData(12, 0, 12, 0)]
    public void Setup_EvaluatedOnASummerFriday_ExpiresNotLaterThanFridayNoEntryUtc(int fridayHour, int fridayMinute, int expiryHour, int expiryMinute)
    {
        // 18 July 2025 is a Friday; 12:00 New York is 16:00 UTC.
        var friday = new DateTime(2025, 7, 18);

        SessionRangeSetup setup = Single(Tracker(new TimeSpan(fridayHour, fridayMinute, 0)), DayToEvaluation(friday), Bias.Short);

        Assert.Equal(Utc(2025, 7, 18, expiryHour, expiryMinute), setup.ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Setup_EvaluatedOnAThursday_IgnoresFridayNoEntryUtc()
    {
        SessionRangeSetup setup = Single(Tracker(TimeSpan.FromHours(10.0)), DayToEvaluation(new DateTime(2025, 1, 16)), Bias.Long);

        Assert.Equal(Utc(2025, 1, 16, 17, 0), setup.ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Setup_MondayWhoseRangeStartsOnSunday_IsNamedByMonday()
    {
        var monday = new DateTime(2025, 1, 20);
        List<Bar> bars = DayToEvaluation(monday);
        Assert.Equal(DayOfWeek.Sunday, bars[0].OpenTimeUtc.DayOfWeek);

        Assert.Equal("RL-20250120", Single(Tracker(), bars, Bias.Short).Id);
        Assert.Equal("RH-20250120", Single(Tracker(), DayToEvaluation(monday), Bias.Long).Id);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Setup_AfterItForTheRestOfTheTradingDay_NoFurtherSetup()
    {
        SessionRangeTracker tracker = Tracker();
        SessionRangeSetup setup = Single(tracker, DayToEvaluation(Winter), Bias.Long);
        tracker.OrderNoLongerPending(setup.Id);

        // Price breaks far beyond both edges for the rest of the day; nothing more is reported.
        var rest = new List<Bar>();
        for (int k = 0; k < 56; k++)
        {
            rest.Add(Span(At(Winter, 3, 0) + (k * M15), 2100.0, 1900.0));
        }

        List<SessionRangeEvent?> answers = Feed(tracker, rest, Bias.Short);

        Assert.All(answers, answer => Assert.Null(answer));
        Assert.Same(setup, tracker.Setup);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Setup_StopNotAboveZero_GivesNoSetup()
    {
        // ATR(H4) 2010 puts the buy stop at 0.
        Assert.All(Feed(Tracker(), DayToEvaluation(Winter), Bias.Long, AtrD1, 2010.0), answer => Assert.Null(answer));
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Setup_TargetNotAboveZero_GivesNoSetup()
    {
        // ATR(H4) 1000 puts the sell stop at 3000 and the target at 0.
        Assert.All(Feed(Tracker(), DayToEvaluation(Winter), Bias.Short, AtrD1, 1000.0), answer => Assert.Null(answer));
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Setup_EntryNotAboveZero_GivesNoSetup()
    {
        // A range from 0 to 5: the sell entry at the range low is 0.
        List<Bar> bars = Enumerable.Range(0, 32).Select(k => Span(At(Winter, 19, 0) + (k * M15), 5.0, 0.0)).ToList();

        Assert.All(Feed(Tracker(), bars, Bias.Short, 2.0, 1.0), answer => Assert.Null(answer));
        Assert.NotNull(OnlySetup(Feed(Tracker(), bars, Bias.Long, 2.0, 1.0)));
    }

    private static SessionRangeSetup Single(SessionRangeTracker tracker, IEnumerable<Bar> bars, Bias bias, double atrD1 = AtrD1, double atrH4 = AtrH4)
    {
        SessionRangeSetup? setup = OnlySetup(Feed(tracker, bars, bias, atrD1, atrH4));
        Assert.NotNull(setup);
        return setup!;
    }
}
