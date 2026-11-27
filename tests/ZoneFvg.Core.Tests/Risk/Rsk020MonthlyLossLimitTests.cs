using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-020: the monthly loss limit in the risk module. The parameter and its validation, the
/// start of the CE(S)T calendar month, the check of a new trade and the month status.
/// </summary>
/// <remarks>
/// <para>Account of spec v0.19: InitialCapital 200,000, MonthlyLossLimitPercent 2, so the limit is
/// 4,000 USD; every other value is the core default (daily budget 4,000, caps off). Epoch
/// 1 January 2026.</para>
/// <para>Server time Wednesday 2026-03-18 10:00 UTC (11:00 CET). The month began at 00:00 CET on
/// 1 March, which is 2026-02-28 23:00 UTC; the day at 2026-03-17 23:00 UTC. The snapshots carry
/// history from the epoch start (2025-12-31 23:00 UTC) unless a test says otherwise.</para>
/// <para>S, the balance at the month start, is 205,000: a trade closed on 20 February brought the
/// balance from 200,000 to 205,000; a trade closed on 10 March brought it to the current balance B.
/// The month's closed loss is S - B. The new trade is a EURUSD buy of 10,000 units with a 20-pip
/// stop: 20 USD at its stop, far inside every other limit.</para>
/// </remarks>
public class Rsk020MonthlyLossLimitTests
{
    private const string Manual = "manual";

    private static readonly DateTime Now = Utc(2026, 3, 18, 10, 0);

    /// <summary>00:00 CET on 1 March 2026.</summary>
    private static readonly DateTime MonthStart = Utc(2026, 2, 28, 23, 0);

    private static RiskParameters Limit2 => new() { RiskEpochDate = RiskData.Epoch, LadderEnabled = false, MonthlyLossLimitPercent = 2.0 };

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second = 0)
    {
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
    }

    private static ClosedTradeInfo Closed(long id, DateTime closeUtc, double net, double balanceAfter, string label = Manual, double commission = 0.0, double swap = 0.0)
    {
        return new ClosedTradeInfo(id, "GBPUSD", label, closeUtc.AddHours(-2.0), closeUtc, net, commission, swap, balanceAfter);
    }

    /// <summary>The two trades of the remarks for the balance <paramref name="balance"/>: S = 205,000.</summary>
    private static ClosedTradeInfo[] MonthTo(double balance)
    {
        return new[]
        {
            Closed(1, Utc(2026, 2, 20, 12, 0), 5000.0, 205000.0),
            Closed(2, Utc(2026, 3, 10, 12, 0), balance - 205000.0, balance),
        };
    }

    private static RiskSnapshot Snapshot(double balance, IEnumerable<ClosedTradeInfo> history, DateTime? now = null, DateTime? since = null)
    {
        return new RiskSnapshot(now ?? Now, RiskData.Account(balance), Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history, since ?? RiskData.EpochStart, RiskData.AllSymbols);
    }

    private static PlannedTrade NewTrade => RiskData.Trade("EURUSD", Side.Buy, 1.1, 1.098, 10000.0);

    private static RiskCheckResult Check(RiskParameters parameters, RiskSnapshot snapshot)
    {
        return new RiskEngine(parameters).CheckNewTrade(snapshot, NewTrade);
    }

    // ------------------------------------------------------------------ the parameter

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Defaults_CoreDefaultIsZeroOff()
    {
        Assert.Equal(0.0, new RiskParameters().MonthlyLossLimitPercent);
    }

    [Theory]
    [Trait("Req", "RSK-020")]
    [InlineData(-0.01, "-0.01")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(8.0, "8")]
    public void Validate_OutOfRange_RefusedNamingIt(double value, string text)
    {
        RiskParameters parameters = Limit2 with { MonthlyLossLimitPercent = value };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("MonthlyLossLimitPercent", exception.ParamName);
        Assert.StartsWith("MonthlyLossLimitPercent must be at least 0 and below MaxDrawdownPercent but was " + text + ".", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RiskEngine(parameters));
    }

    [Theory]
    [Trait("Req", "RSK-020")]
    [InlineData(0.0)]
    [InlineData(2.0)]
    [InlineData(7.99)]
    public void Validate_InRange_Accepted(double value)
    {
        Exception? exception = Record.Exception(() => (Limit2 with { MonthlyLossLimitPercent = value }).Validate());

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Validate_AgainstALowerMaxDrawdown_RefusedAtThatLimit()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => (Limit2 with { MaxDrawdownPercent = 2.0 }).Validate());

        Assert.Equal("MonthlyLossLimitPercent", exception.ParamName);
    }

    // ------------------------------------------------------------------ the month start

    [Theory]
    [Trait("Req", "RSK-020")]
    [InlineData("2026-01-14T10:00:00Z", "2025-12-31T23:00:00Z")]
    [InlineData("2026-01-31T22:59:59Z", "2025-12-31T23:00:00Z")]
    [InlineData("2026-01-31T23:00:00Z", "2026-01-31T23:00:00Z")]
    [InlineData("2026-03-31T21:59:59Z", "2026-02-28T23:00:00Z")]
    [InlineData("2026-03-31T22:00:00Z", "2026-03-31T22:00:00Z")]
    [InlineData("2026-03-29T01:00:00Z", "2026-02-28T23:00:00Z")]
    [InlineData("2026-07-15T12:00:00Z", "2026-06-30T22:00:00Z")]
    [InlineData("2026-10-25T01:00:00Z", "2026-09-30T22:00:00Z")]
    [InlineData("2026-10-31T22:59:59Z", "2026-09-30T22:00:00Z")]
    [InlineData("2026-10-31T23:00:00Z", "2026-10-31T23:00:00Z")]
    [InlineData("2026-12-31T22:59:59Z", "2026-11-30T23:00:00Z")]
    [InlineData("2026-12-31T23:00:00Z", "2026-12-31T23:00:00Z")]
    public void StartOfCestMonthUtc_AroundMonthEndsAndBothSwitches_IsMidnightCestOfTheFirst(string now, string expected)
    {
        DateTime start = CestClock.StartOfCestMonthUtc(DateTimeText.Utc(now));

        Assert.Equal(DateTimeText.Utc(expected), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void StartOfCestMonthUtc_TimeNotUtc_Refused()
    {
        Assert.Throws<ArgumentException>(() => CestClock.StartOfCestMonthUtc(new DateTime(2026, 3, 18, 10, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void StartOfCestMonthUtc_IsTheDayStartOfTheFirstDay()
    {
        for (DateTime t = Utc(2026, 1, 1, 0, 0); t < Utc(2027, 1, 1, 0, 0); t = t.AddHours(7.0))
        {
            DateTime start = CestClock.StartOfCestMonthUtc(t);
            DateTime local = start + CestClock.UtcOffset(start);
            Assert.Equal(1, local.Day);
            Assert.Equal(TimeSpan.Zero, local.TimeOfDay);
            Assert.Equal(start, CestClock.StartOfCestDayUtc(start));
            Assert.True(start <= t);
            Assert.True(t < CestClock.StartOfCestMonthUtc(start.AddDays(32.0)));
        }
    }

    // ------------------------------------------------------------------ the boundary

    [Theory]
    [Trait("Req", "RSK-020")]
    [InlineData(201000.0, false)]
    [InlineData(201000.01, true)]
    [InlineData(200999.99, false)]
    [InlineData(205000.0, true)]
    public void CheckNewTrade_AroundFourThousandBelowTheMonthStart_RefusedFromExactlyTheLimit(double balance, bool allowed)
    {
        RiskCheckResult result = Check(Limit2, Snapshot(balance, MonthTo(balance)));

        Assert.Equal(allowed, result.IsAllowed);
        if (!allowed)
        {
            Assert.Equal(RiskReason.MonthlyLossLimitReached, result.Reason);
            Assert.Equal(205000.0 - balance, result.Measured, 6);
            Assert.Equal(4000.0, result.Limit, 9);
            Assert.Contains("monthly closed loss", result.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void MonthlyLoss_Status_GivesTheMonthStartBalanceTheLossAndTheLimit()
    {
        var engine = new RiskEngine(Limit2);

        MonthlyLossStatus reached = engine.MonthlyLoss(Snapshot(201000.0, MonthTo(201000.0)));
        MonthlyLossStatus open = engine.MonthlyLoss(Snapshot(201000.01, MonthTo(201000.01)));

        Assert.True(reached.Reached);
        Assert.Equal(MonthStart, reached.MonthStartUtc);
        Assert.Equal(205000.0, reached.MonthStartBalance, 6);
        Assert.Equal(201000.0, reached.Balance);
        Assert.Equal(4000.0, reached.Loss, 6);
        Assert.Equal(4000.0, reached.Limit, 9);
        Assert.False(open.Reached);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void MonthlyLoss_LimitOff_NeverReached()
    {
        MonthlyLossStatus status = new RiskEngine(Limit2 with { MonthlyLossLimitPercent = 0.0 }).MonthlyLoss(Snapshot(150000.0, MonthTo(150000.0)));

        Assert.False(status.Reached);
        Assert.Equal(0.0, status.Limit);
    }

    // ------------------------------------------------------------------ S is rebuilt from history

    [Fact]
    [Trait("Req", "RSK-020")]
    public void CheckNewTrade_TradesClosedBeforeTheMonthStart_DoNotCount()
    {
        // February lost 10,000 (from 214,000 to 204,000); March lost 3,000: 3,000 < 4,000.
        ClosedTradeInfo[] history =
        {
            Closed(1, Utc(2026, 2, 3, 12, 0), 14000.0, 214000.0),
            Closed(2, Utc(2026, 2, 20, 12, 0), -10000.0, 204000.0),
            Closed(3, Utc(2026, 3, 10, 12, 0), -3000.0, 201000.0),
        };

        Assert.True(Check(Limit2, Snapshot(201000.0, history)).IsAllowed);
    }

    [Theory]
    [Trait("Req", "RSK-020")]
    [InlineData(22, 59, true)]
    [InlineData(23, 0, false)]
    public void CheckNewTrade_TradeAtTheMonthBoundary_CountsFromMidnightCest(int hour, int minute, bool allowed)
    {
        // 4,000 lost by a trade closed at 23:59 CET on 28 February (not counted) or at 00:00 CET on 1 March (counted).
        ClosedTradeInfo[] history = { Closed(1, Utc(2026, 2, 28, hour, minute), -4000.0, 201000.0) };

        RiskCheckResult result = Check(Limit2, Snapshot(201000.0, history));

        Assert.Equal(allowed, result.IsAllowed);
        Assert.Equal(allowed ? RiskReason.None : RiskReason.MonthlyLossLimitReached, result.Reason);
    }

    [Theory]
    [Trait("Req", "RSK-020")]
    [InlineData(-4000.0, -60.0, -40.0, false)]
    [InlineData(-3900.0, 0.0, 0.0, true)]
    public void CheckNewTrade_CommissionAndSwap_CountInTheNetResult(double net, double commission, double swap, bool allowed)
    {
        // A trade of -3,900 before costs: with 60 commission and 40 swap its net result is -4,000.
        double balance = 205000.0 + net;
        ClosedTradeInfo[] history = { Closed(1, Utc(2026, 3, 10, 12, 0), net, balance, commission: commission, swap: swap) };

        Assert.Equal(allowed, Check(Limit2, Snapshot(balance, history)).IsAllowed);
    }

    [Theory]
    [Trait("Req", "RSK-020")]
    [InlineData("ZFVG|v1|EURUSD|BH-20260309-2200")]
    [InlineData("ZFVG|v1|XAUUSD|RH-20260309")]
    [InlineData("manual")]
    [InlineData("")]
    public void CheckNewTrade_TradesOfAnotherInstanceOrWithoutABotLabel_Count(string label)
    {
        ClosedTradeInfo[] history = { Closed(1, Utc(2026, 3, 9, 12, 0), -4000.0, 201000.0, label) };

        Assert.Equal(RiskReason.MonthlyLossLimitReached, Check(Limit2, Snapshot(201000.0, history)).Reason);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void CheckNewTrade_ProfitsLaterInTheMonth_ReduceTheLoss()
    {
        ClosedTradeInfo[] history =
        {
            Closed(1, Utc(2026, 3, 2, 12, 0), -4500.0, 200500.0),
            Closed(2, Utc(2026, 3, 9, 12, 0), 1000.0, 201500.0),
        };

        Assert.True(Check(Limit2, Snapshot(201500.0, history)).IsAllowed);
    }

    // ------------------------------------------------------------------ the month change

    public static IEnumerable<object[]> MonthEnds()
    {
        // March ends in summer time (switch on 29 March): 1 April 00:00 CEST = 31 March 22:00 UTC.
        yield return new object[] { Utc(2026, 3, 30, 10, 0), Utc(2026, 3, 31, 21, 59, 59), Utc(2026, 3, 31, 22, 0), Utc(2026, 3, 10, 12, 0) };

        // October ends in winter time (switch on 25 October): 1 November 00:00 CET = 31 October 23:00 UTC.
        yield return new object[] { Utc(2026, 10, 30, 10, 0), Utc(2026, 10, 31, 22, 59, 59), Utc(2026, 10, 31, 23, 0), Utc(2026, 10, 12, 12, 0) };

        // February to March, both winter time: 1 March 00:00 CET = 28 February 23:00 UTC.
        yield return new object[] { Utc(2026, 2, 27, 10, 0), Utc(2026, 2, 28, 22, 59, 59), Utc(2026, 2, 28, 23, 0), Utc(2026, 2, 10, 12, 0) };
    }

    [Theory]
    [Trait("Req", "RSK-020")]
    [MemberData(nameof(MonthEnds))]
    public void CheckNewTrade_MonthChange_RefusedUntilMidnightCestOfTheFirstThenAllowed(DateTime thirtieth, DateTime lastSecond, DateTime firstOfNext, DateTime lossUtc)
    {
        ClosedTradeInfo[] history = { Closed(1, lossUtc, -4000.0, 201000.0) };

        Assert.Equal(RiskReason.MonthlyLossLimitReached, Check(Limit2, Snapshot(201000.0, history, thirtieth)).Reason);
        Assert.Equal(RiskReason.MonthlyLossLimitReached, Check(Limit2, Snapshot(201000.0, history, lastSecond)).Reason);
        Assert.True(Check(Limit2, Snapshot(201000.0, history, firstOfNext)).IsAllowed);
    }

    // ------------------------------------------------------------------ order, data and the off value

    [Fact]
    [Trait("Req", "RSK-020")]
    public void CheckNewTrade_LimitOff_TheSameLossIsAllowed()
    {
        RiskSnapshot snapshot = Snapshot(195000.0, MonthTo(195000.0));

        Assert.Equal(RiskReason.MonthlyLossLimitReached, Check(Limit2, snapshot).Reason);
        Assert.True(Check(Limit2 with { MonthlyLossLimitPercent = 0.0 }, snapshot).IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void CheckNewTrade_LimitOff_HistoryFromTheEpochOnlyIsEnoughAsBefore()
    {
        // Epoch 10 March: with the limit off the history need not reach the month start.
        RiskParameters off = Limit2 with { RiskEpochDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc), MonthlyLossLimitPercent = 0.0 };
        RiskSnapshot snapshot = Snapshot(201000.0, Array.Empty<ClosedTradeInfo>(), since: Utc(2026, 3, 9, 23, 0));

        Assert.True(Check(off, snapshot).IsAllowed);
        Assert.Equal(RiskReason.HistoryIncomplete, Check(off with { MonthlyLossLimitPercent = 2.0 }, snapshot).Reason);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void CheckNewTrade_HistoryNotReachingTheMonthStart_RefusedAsIncomplete()
    {
        RiskParameters lateEpoch = Limit2 with { RiskEpochDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc) };
        RiskSnapshot snapshot = Snapshot(201000.0, Array.Empty<ClosedTradeInfo>(), since: MonthStart.AddSeconds(1.0));

        RiskCheckResult result = Check(lateEpoch, snapshot);

        Assert.Equal(RiskReason.HistoryIncomplete, result.Reason);
        Assert.Contains("2026-02-28T23:00:00", result.Detail, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new RiskEngine(lateEpoch).MonthlyLoss(snapshot));
        Assert.True(Check(lateEpoch, Snapshot(201000.0, Array.Empty<ClosedTradeInfo>(), since: MonthStart)).IsAllowed);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void CheckNewTrade_MonthStartBalanceNotPositive_RefusedFailClosed()
    {
        // Balance 1,000 after +2,000 in the month: S = -1,000.
        ClosedTradeInfo[] history = { Closed(1, Utc(2026, 3, 10, 12, 0), 2000.0, 1000.0) };
        RiskParameters small = Limit2 with { InitialCapital = 10000.0 };

        RiskCheckResult result = Check(small, Snapshot(1000.0, history));

        Assert.False(result.IsAllowed);
        Assert.Equal(RiskReason.InvalidInput, result.Reason);
        Assert.Contains("month-start balance", result.Detail, StringComparison.Ordinal);
        Assert.True(new RiskEngine(small).MonthlyLoss(Snapshot(1000.0, history)).Reached);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void CheckNewTrade_DailyBudgetAlsoExceeded_TheExistingOrderIsKept()
    {
        // 4,500 lost today: the daily budget (checked first) refuses; without it the monthly limit refuses.
        ClosedTradeInfo[] history = { Closed(1, Utc(2026, 3, 18, 8, 0), -4500.0, 200500.0) };
        RiskSnapshot snapshot = Snapshot(200500.0, history);
        var engine = new RiskEngine(Limit2);

        Assert.Equal(RiskReason.DailyBudgetExceeded, engine.CheckNewTrade(snapshot, NewTrade).Reason);
        RiskCheckResult waived = engine.CheckNewTradeWithoutDailyBudget(snapshot, NewTrade);
        Assert.Equal(RiskReason.MonthlyLossLimitReached, waived.Reason);
        Assert.Equal(4500.0, waived.Measured, 6);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void CheckNewTrade_EntryLimitAlsoReached_TheEntryLimitComesFirst()
    {
        ClosedTradeInfo[] history = MonthTo(201000.0);
        RiskSnapshot snapshot = new(Now, RiskData.Account(201000.0), new[] { RiskData.Position(7, "GBPUSD", Side.Buy, 1000.0, 1.3, 1.298, Now.AddHours(-1.0), RiskData.BotLabel("GBPUSD")) }, Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart, RiskData.AllSymbols);

        Assert.Equal(RiskReason.AccountEntryLimitReached, Check(Limit2 with { MaxEntriesPerDay = 1 }, snapshot).Reason);
        Assert.Equal(RiskReason.MonthlyLossLimitReached, Check(Limit2, snapshot).Reason);
    }
}

/// <summary>Parses the invariant UTC texts of the RSK-020 tests.</summary>
internal static class DateTimeText
{
    public static DateTime Utc(string text)
    {
        return DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
    }
}
