using Xunit;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-012: the values of the consistency readout (spec v0.20 row BK, section 15.8), a pure
/// function of the closed trades, the start date and the limit. Days are CE(S)T dates of the close
/// time; profit is the trade's net profit (commission and swap included); met is yes only when the
/// total is above 0 and the best day is at most total x limit / 100.
/// </summary>
/// <remarks>
/// Start date 1 March 2026: 00:00 CET is 2026-02-28 23:00 UTC. Trades close at 10:00 UTC (11:00 CET)
/// on the day named unless a test says otherwise. Limit 30.
/// </remarks>
public class Ops012ConsistencyReadoutTests
{
    private static readonly DateTime Since = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0, int second = 0)
    {
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
    }

    private static ClosedTradeInfo Trade(long id, DateTime closeUtc, double net, string label = "manual")
    {
        return new ClosedTradeInfo(id, "XAUUSD", label, closeUtc.AddHours(-1.0), closeUtc, net, -5.0, -1.0, 100000.0 + net);
    }

    private static ClosedTradeInfo OnDay(long id, int day, double net)
    {
        return Trade(id, Utc(2026, 3, day, 10), net);
    }

    private static ConsistencyResult Compute(params ClosedTradeInfo[] trades)
    {
        return ConsistencyReadout.Compute(trades, Since, 30.0);
    }

    private static string Line(ConsistencyResult result)
    {
        return string.Join(" ", ConsistencyReadout.Fields(result).Select(field => field.Key + "=" + field.Value));
    }

    // ------------------------------------------------------------------ ratio and met

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_Total10000BestDay6000_Ratio60NotMet()
    {
        ConsistencyResult result = Compute(OnDay(1, 2, 6000.0), OnDay(2, 3, 4000.0));

        Assert.Equal(10000.0, result.Total, 1e-9);
        Assert.Equal(new DateTime(2026, 3, 2), result.BestDay);
        Assert.Equal(6000.0, result.BestDayProfit!.Value, 1e-9);
        Assert.Equal(60.0, result.RatioPercent!.Value, 1e-9);
        Assert.False(result.Met);
        Assert.Equal("since=2026-03-01 total=10000 bestDay=2026-03-02 bestDayProfit=6000 ratio=60 limit=30 met=no", Line(result));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_BestDayExactlyAtTheLimit_Met()
    {
        ConsistencyResult result = Compute(OnDay(1, 2, 3000.0), OnDay(2, 3, 3000.0), OnDay(3, 4, 3000.0), OnDay(4, 5, 1000.0));

        Assert.Equal(30.0, result.RatioPercent!.Value, 1e-9);
        Assert.True(result.Met);
        Assert.Equal("since=2026-03-01 total=10000 bestDay=2026-03-02 bestDayProfit=3000 ratio=30 limit=30 met=yes", Line(result));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_BestDayOneCentAboveTheLimit_NotMet()
    {
        // Total 10,000.00, best day 3,000.01: 30.0001%.
        ConsistencyResult result = Compute(OnDay(1, 2, 3000.0), OnDay(2, 3, 3000.01), OnDay(3, 4, 3000.0), OnDay(4, 5, 999.99));

        Assert.Equal(new DateTime(2026, 3, 3), result.BestDay);
        Assert.False(result.Met);
        Assert.Equal("since=2026-03-01 total=10000 bestDay=2026-03-03 bestDayProfit=3000.01 ratio=30 limit=30 met=no", Line(result));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_BestDayJustBelowTheLimitByLessThanTheTolerance_Met()
    {
        // PriceMath tolerance: a best day above total x limit / 100 by less than one billionth counts as at the limit.
        ConsistencyResult result = ConsistencyReadout.Compute(new[] { OnDay(1, 2, 0.1 + 0.2), OnDay(2, 3, 0.3), OnDay(3, 4, 0.3), OnDay(4, 5, 0.1) }, Since, 30.0);

        Assert.Equal(new DateTime(2026, 3, 2), result.BestDay);

        Assert.True(result.Met);
    }

    [Theory]
    [Trait("Req", "OPS-012")]
    [InlineData(0.0)]
    [InlineData(-500.0)]
    public void Compute_TotalNotAbove0_RatioNaNotMet(double secondDay)
    {
        ConsistencyResult result = Compute(OnDay(1, 2, 500.0), OnDay(2, 3, -500.0 + secondDay));

        Assert.Null(result.RatioPercent);
        Assert.False(result.Met);
        Assert.Contains(" ratio=NA limit=30 met=no", Line(result), StringComparison.Ordinal);
        Assert.Contains(" bestDay=2026-03-02 bestDayProfit=500 ", Line(result), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_NoTrade_TotalZeroEverythingElseNa()
    {
        ConsistencyResult result = Compute();

        Assert.Equal(0.0, result.Total);
        Assert.Null(result.BestDay);
        Assert.Null(result.BestDayProfit);
        Assert.Equal("since=2026-03-01 total=0 bestDay=NA bestDayProfit=NA ratio=NA limit=30 met=no", Line(result));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_LosingDays_CountAgainstTheTotal()
    {
        // +6,000, -2,000, +4,000: total 8,000, best day 6,000, 75%.
        ConsistencyResult result = Compute(OnDay(1, 2, 6000.0), OnDay(2, 3, -2000.0), OnDay(3, 4, 4000.0));

        Assert.Equal(8000.0, result.Total, 1e-9);
        Assert.Equal(75.0, result.RatioPercent!.Value, 1e-9);
        Assert.False(result.Met);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_ProfitIsTheNetProfitAsReported()
    {
        // The net profit already holds commission and swap (ClosedTradeInfo.NetProfit); nothing is added or taken off.
        ConsistencyResult result = Compute(new ClosedTradeInfo(1, "XAUUSD", "manual", Utc(2026, 3, 2, 9), Utc(2026, 3, 2, 10), 1234.56, -40.0, -7.5, 101234.56));

        Assert.Equal(1234.56, result.Total, 1e-9);
        Assert.Equal(1234.56, result.BestDayProfit!.Value, 1e-9);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_EveryTradeOfTheAccount_BotOrNot()
    {
        ConsistencyResult result = Compute(OnDay(1, 2, 1000.0), Trade(2, Utc(2026, 3, 3, 10), 2000.0, "ZFVG|v1|XAUUSD|SR-20260303"));

        Assert.Equal(3000.0, result.Total, 1e-9);
    }

    // ------------------------------------------------------------------ the start and the days

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_TradeOneSecondBeforeMidnightCetOfTheStartDate_Excluded_AtMidnight_Included()
    {
        ClosedTradeInfo before = Trade(1, Utc(2026, 2, 28, 22, 59, 59), 9000.0);
        ClosedTradeInfo atStart = Trade(2, Utc(2026, 2, 28, 23, 0, 0), 1000.0);

        ConsistencyResult result = Compute(before, atStart);

        Assert.Equal(1000.0, result.Total, 1e-9);
        Assert.Equal(new DateTime(2026, 3, 1), result.BestDay);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_TwoTradesOnOneCestDay_SumIntoOneDay()
    {
        // 2 March 10:00 UTC and 2 March 22:30 UTC (23:30 CET) are both 2 March; 3 March has 4,500.
        ConsistencyResult result = Compute(OnDay(1, 2, 3000.0), Trade(2, Utc(2026, 3, 2, 22, 30), 2500.0), OnDay(3, 3, 4500.0));

        Assert.Equal(new DateTime(2026, 3, 2), result.BestDay);
        Assert.Equal(5500.0, result.BestDayProfit!.Value, 1e-9);
        Assert.Equal(10000.0, result.Total, 1e-9);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_TradeAt2330UtcInSummer_BelongsToTheNextCestDay()
    {
        // 10 June 2026 23:30 UTC is 11 June 01:30 CEST; 11 June 10:00 UTC is the same CE(S)T day.
        DateTime since = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        ClosedTradeInfo late = Trade(1, Utc(2026, 6, 10, 23, 30), 2000.0);
        ClosedTradeInfo sameDay = Trade(2, Utc(2026, 6, 11, 10), 2000.0);
        ClosedTradeInfo dayBefore = Trade(3, Utc(2026, 6, 10, 10), 3000.0);

        ConsistencyResult result = ConsistencyReadout.Compute(new[] { late, sameDay, dayBefore }, since, 30.0);

        Assert.Equal(new DateTime(2026, 6, 11), result.BestDay);
        Assert.Equal(4000.0, result.BestDayProfit!.Value, 1e-9);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_EqualBestDays_TheEarlierDayIsNamed()
    {
        ConsistencyResult result = Compute(OnDay(1, 5, 2000.0), OnDay(2, 3, 2000.0), OnDay(3, 4, 1000.0));

        Assert.Equal(new DateTime(2026, 3, 3), result.BestDay);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_SameTradesInAnyOrder_SameResult()
    {
        ClosedTradeInfo[] trades = { OnDay(1, 2, 6000.0), OnDay(2, 3, -2000.0), OnDay(3, 4, 4000.0), Trade(4, Utc(2026, 3, 2, 22, 30), 0.3) };

        string forward = Line(ConsistencyReadout.Compute(trades, Since, 30.0));
        string backward = Line(ConsistencyReadout.Compute(trades.Reverse().ToArray(), Since, 30.0));

        Assert.Equal(forward, backward);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Fields_OtherLimit_WrittenInRoundTripFormat()
    {
        ConsistencyResult result = ConsistencyReadout.Compute(new[] { OnDay(1, 2, 1000.0) }, Since, 12.5);

        Assert.EndsWith(" ratio=100 limit=12.5 met=no", Line(result), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Fields_MoneyAndRatio_RoundedTo2DecimalsHalfAwayFromZero()
    {
        // 1,500 of 4,200 is 35.714...%; 1,000.125 is exact in binary and rounds up to 1,000.13.
        Assert.Equal(
            "since=2026-03-01 total=4200 bestDay=2026-03-02 bestDayProfit=1500 ratio=35.71 limit=30 met=no",
            Line(Compute(OnDay(1, 2, 1500.0), OnDay(2, 3, 1400.0), OnDay(3, 4, 1300.0))));
        Assert.Contains(" bestDayProfit=1000.13 ", Line(Compute(OnDay(1, 2, 1000.125), OnDay(2, 3, 100.0))), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Fields_TotalThatRoundsToMinusZero_WrittenAs0()
    {
        ConsistencyResult result = Compute(OnDay(1, 2, 0.001), OnDay(2, 3, -0.004));

        Assert.Equal("since=2026-03-01 total=0 bestDay=2026-03-02 bestDayProfit=0 ratio=NA limit=30 met=no", Line(result));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void UnavailableFields_TheStatusAndNothingElse()
    {
        string line = string.Join(" ", ConsistencyReadout.UnavailableFields().Select(field => field.Key + "=" + field.Value));

        Assert.Equal("status=unavailable", line);
    }

    // ------------------------------------------------------------------ invalid data

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConsistencyReadout.Compute(null!, Since, 30.0));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_NullEntry_Throws()
    {
        Assert.Throws<ArgumentException>(() => ConsistencyReadout.Compute(new ClosedTradeInfo[] { OnDay(1, 2, 1.0), null! }, Since, 30.0));
    }

    [Theory]
    [Trait("Req", "OPS-012")]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Compute_ProfitNotFinite_Throws(double net)
    {
        Assert.Throws<ArgumentException>(() => Compute(OnDay(1, 2, net)));
    }

    [Fact]
    [Trait("Req", "OPS-012")]
    public void Compute_CloseTimeNotUtc_Throws()
    {
        ClosedTradeInfo local = OnDay(1, 2, 1.0) with { CloseTimeUtc = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Local) };

        Assert.Throws<ArgumentException>(() => Compute(local));
    }
}
