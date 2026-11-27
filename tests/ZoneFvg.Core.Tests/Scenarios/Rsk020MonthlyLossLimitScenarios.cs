using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Risk;
using Range = ZoneFvg.Core.Tests.SessionRangeBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-020 through the pre-trade filters and through <see cref="BotCore"/> with a
/// <see cref="FakeBroker"/>: the refusal and its skip record, the three sizing plans, the history
/// window and its broker calls, broker data errors, the cancellation of pending entries, and what
/// the limit leaves alone (open positions, the timed exit, everything when it is 0).
/// </summary>
/// <remarks>
/// <para>Filters: the reference buy of <see cref="ExecutionData"/> on EURUSD at Wednesday
/// 2026-01-14 10:00 UTC, fixed sizing 0.5%, core defaults otherwise (InitialCapital 200,000,
/// daily budget 4,000, caps off), MonthlyLossLimitPercent 2 (4,000 USD). RiskEpochDate
/// 10 January 2026, so the history the filters read without the limit starts at 2026-01-09
/// 23:00 UTC, and with it at the month start, 2025-12-31 23:00 UTC. History: a trade closed on
/// 20 December 2025 brought the balance to 205,000 (S), one closed on 5 January 2026 brought it
/// to the current balance B. At B = 201,000: risk 1,005, volume 502,000, risk money 1,004.</para>
/// <para>Bot: the USDJPY instance of <see cref="RecoveryData"/> on Wednesday 2026-03-04 with
/// <see cref="RiskData.Parameters"/> (InitialCapital 100,000, daily budget 1,500), so the monthly
/// limit of 2% is 2,000 USD. The month began at 2026-02-28 23:00 UTC. A manual trade closed on
/// 2 March lost 1,500; at 09:10 a trade of another instance loses 500 more.</para>
/// </remarks>
public class Rsk020MonthlyLossLimitScenarios
{
    private const string Manual = "manual";

    private static readonly DateTime EpochTen = new(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EpochTenStart = new(2026, 1, 9, 23, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime JanuaryStart = new(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MarchStart = new(2026, 2, 28, 23, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime A = RecoveryData.Activation;
    private static readonly DateTime Nine = A.AddHours(1.0);

    private static RiskParameters Fixed(double limit) => new() { RiskEpochDate = EpochTen, LadderEnabled = false, MonthlyLossLimitPercent = limit };

    private static ClosedTradeInfo Closed(long id, DateTime closeUtc, double net, double balanceAfter, string label = Manual)
    {
        return new ClosedTradeInfo(id, "GBPUSD", label, closeUtc.AddHours(-2.0), closeUtc, net, 0.0, 0.0, balanceAfter);
    }

    private static ClosedTradeInfo[] MonthTo(double balance)
    {
        return new[]
        {
            Closed(1, new DateTime(2025, 12, 20, 12, 0, 0, DateTimeKind.Utc), 5000.0, 205000.0),
            Closed(2, ExecutionData.At(5, 12, 0), balance - 205000.0, balance),
        };
    }

    // ------------------------------------------------------------------ the filters

    [Theory]
    [Trait("Req", "RSK-020")]
    [InlineData(201000.0, false)]
    [InlineData(201000.01, true)]
    [InlineData(200999.99, false)]
    public void Filters_AroundTheLimit_SkippedAsMonthlyLossLimitFromExactlyTheLimit(double balance, bool allowed)
    {
        FilterRun run = FilterRun.Evaluate(Fixed(2.0), balance);

        Assert.Equal(allowed, run.Result.IsAllowed);
        if (allowed)
        {
            Assert.Empty(run.Events.Skips);
            return;
        }

        SkipRecord skip = Assert.Single(run.Events.Skips);
        Assert.Equal(SkipReason.MonthlyLossLimit, skip.Reason);
        Assert.Equal(205000.0 - balance, skip.Measured, 6);
        Assert.Equal(4000.0, skip.Limit, 9);
        Assert.Contains("monthly closed loss", skip.Detail, StringComparison.Ordinal);
        Assert.Null(run.Result.Order);
        Assert.Empty(run.Broker.Requests);
    }

    public static IEnumerable<object[]> Plans()
    {
        yield return new object[] { "fixed", Fixed(2.0) };
        yield return new object[] { "ladder", Fixed(2.0) with { LadderEnabled = true } };
        yield return new object[] { "recovery", Fixed(2.0) with { SizingPlan = SizingPlan.Recovery } };
        yield return new object[] { "fixed with the brake", Fixed(2.0) with { DrawdownBrakePercent = 3.0 } };
    }

    [Theory]
    [Trait("Req", "RSK-020")]
    [MemberData(nameof(Plans))]
    public void Filters_EverySizingPlan_RefusedTheSameWay(string plan, RiskParameters risk)
    {
        FilterRun refused = FilterRun.Evaluate(risk, 201000.0);
        FilterRun allowed = FilterRun.Evaluate(risk, 201000.01);

        SkipRecord skip = Assert.Single(refused.Events.Skips);
        Assert.True(skip.Reason == SkipReason.MonthlyLossLimit, plan + ": " + skip.Reason + " " + skip.Detail);
        Assert.Equal(4000.0, skip.Measured, 6);
        Assert.Equal(4000.0, skip.Limit, 9);
        Assert.Empty(refused.Broker.Requests);
        Assert.True(allowed.Result.IsAllowed, plan);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Filters_LimitZero_SameOrderAndTheSameHistoryReadsAsWithoutTheField()
    {
        FilterRun without = FilterRun.Evaluate(new RiskParameters { RiskEpochDate = EpochTen, LadderEnabled = false }, 201000.0);
        FilterRun zero = FilterRun.Evaluate(Fixed(0.0), 201000.0);

        Assert.True(zero.Result.IsAllowed);
        Assert.Equal(new[] { EpochTenStart }, zero.Since);
        Assert.Equal(without.Since, zero.Since);
        Assert.Equal(without.Broker.QueryCount, zero.Broker.QueryCount);
        Assert.Equal(without.Result.Order!.VolumeUnits, zero.Result.Order!.VolumeUnits);
        Assert.Equal(without.Result.Order.Comment, zero.Result.Order.Comment);
        Assert.Equal(without.Result.Order.Label, zero.Result.Order.Label);
        Assert.Equal(502000.0, zero.Result.Order.VolumeUnits);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Filters_LimitOnNotReached_TheSameOrderTheHistoryReadOnceFromTheMonthStart()
    {
        FilterRun off = FilterRun.Evaluate(Fixed(0.0), 205000.0);
        FilterRun on = FilterRun.Evaluate(Fixed(2.0), 205000.0);

        Assert.True(on.Result.IsAllowed);
        Assert.Equal(new[] { JanuaryStart }, on.Since);
        Assert.Equal(off.Broker.QueryCount, on.Broker.QueryCount);
        Assert.Equal(off.Result.Order!.VolumeUnits, on.Result.Order!.VolumeUnits);
        Assert.Equal(off.Result.Order.Comment, on.Result.Order.Comment);
        Assert.Equal(off.Result.Order.RiskMoney, on.Result.Order.RiskMoney);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Filters_LimitOnWithTheLadder_TheHistoryReadOnceFromTheMonthStart()
    {
        FilterRun on = FilterRun.Evaluate(Fixed(2.0) with { LadderEnabled = true }, 205000.0);

        Assert.True(on.Result.IsAllowed);
        Assert.Equal(new[] { JanuaryStart }, on.Since);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Filters_LimitOnBrokerHistoryThrows_SkippedAsBrokerDataInvalid_NoOrder()
    {
        FilterRun run = FilterRun.Evaluate(Fixed(2.0), 205000.0, _ => throw new InvalidOperationException("The broker history could not be read."));

        SkipRecord skip = Assert.Single(run.Events.Skips);
        Assert.Equal(SkipReason.BrokerDataInvalid, skip.Reason);
        Assert.Equal("The broker history could not be read.", skip.Detail);
        Assert.Null(run.Result.Order);
        Assert.Empty(run.Broker.Requests);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Filters_LimitOnBrokerHistoryInvalid_SkippedAsBrokerDataInvalid_NoOrder()
    {
        ClosedTradeInfo[] history = { Closed(1, ExecutionData.At(5, 12, 0), double.NaN, 205000.0) };

        FilterRun run = FilterRun.Evaluate(Fixed(2.0), 205000.0, _ => history);

        Assert.Equal(SkipReason.BrokerDataInvalid, Assert.Single(run.Events.Skips).Reason);
        Assert.Empty(run.Broker.Requests);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Filters_LimitOnBrokerReturnsNoHistory_SkippedAsBrokerDataInvalid_ReadOnce()
    {
        FilterRun run = FilterRun.Evaluate(Fixed(2.0), 205000.0, _ => null!);

        SkipRecord skip = Assert.Single(run.Events.Skips);
        Assert.Equal(SkipReason.BrokerDataInvalid, skip.Reason);
        Assert.Equal("The broker returned no history.", skip.Detail);
        Assert.Single(run.Since);
        Assert.Empty(run.Broker.Requests);
    }

    // ------------------------------------------------------------------ the bot

    private static BotParameters BotWith(double? limit)
    {
        BotParameters parameters = RecoveryData.Parameters();
        return limit.HasValue ? parameters with { Risk = parameters.Risk with { MonthlyLossLimitPercent = limit.Value } } : parameters;
    }

    private static long AddReferenceOrder(FakeBroker broker)
    {
        return broker.AddOrder(RecoveryData.Symbol, Side.Buy, RecoveryData.Volume, RecoveryData.Entry, RecoveryData.Stop, RecoveryData.Target, A.AddHours(4.0), RecoveryData.Label, RecoveryData.Comment());
    }

    /// <summary>Started at 09:00 with 1,500 lost this month and the reference order pending.</summary>
    private static (BotHarness Harness, List<DateTime> Since) StartedWithOrder(double? limit)
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(98500.0, 98500.0, 98500.0)));
        var since = new List<DateTime>();
        h.Port.History = s =>
        {
            since.Add(s);
            return h.Broker.GetHistory(s);
        };
        h.Broker.AddClosedTrade(Closed(901, RecoveryData.At(2, 12, 0), -1500.0, 98500.0));
        AddReferenceOrder(h.Broker);
        h.Start(Nine, BotWith(limit));
        return (h, since);
    }

    /// <summary>09:10: a trade of another instance loses 500 more; the month has lost 2,000.</summary>
    private static void LoseFiveHundredMore(BotHarness h)
    {
        h.Broker.AdvanceTo(A.AddMinutes(70.0));
        h.Broker.Account = ExecutionData.Account(98000.0, 98000.0, 98000.0);
        h.Broker.AddClosedTrade(Closed(902, A.AddMinutes(70.0), -500.0, 98000.0, "ZFVG|v1|XAUUSD|RH-20260304"));
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Bot_LimitReached_PendingEntriesCancelledByTheDailyBudgetPath_JournalNamesTheMonthlyLimit()
    {
        (BotHarness h, _) = StartedWithOrder(2.0);
        Assert.NotNull(h.ReferenceOrder());
        Assert.Equal(0, h.Count(BrokerCall.Cancel));

        LoseFiveHundredMore(h);
        h.FeedTo(A.AddMinutes(75.0));

        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
        string line = Assert.Single(h.Journal.Lines("action"), l => l.Contains("duty=monthly-limit ", StringComparison.Ordinal));
        Assert.Contains(" outcome=Cancelled detail=monthly\\sloss\\slimit\\sreached", line, StringComparison.Ordinal);

        // The daily budget is not exhausted: 500 lost today plus the order's 489.13.
        Assert.False(h.Core.BudgetBlocked);
        Assert.Equal(0, h.Journal.Count("action", "duty=daily-budget "));

        h.FeedTo(A.AddHours(2.0));
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
        Assert.Equal(1, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Bot_LimitReachedWhileRecovering_PendingEntriesCancelledAfterTheRecovery()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(98000.0, 98000.0, 98000.0)));
        h.Broker.AddClosedTrade(Closed(901, RecoveryData.At(2, 12, 0), -2000.0, 98000.0));
        AddReferenceOrder(h.Broker);

        h.Start(Nine, BotWith(2.0));

        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
        Assert.Equal(1, h.Journal.Count("action", "duty=monthly-limit "));
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Bot_LimitNotReached_TheOrderStays()
    {
        (BotHarness h, _) = StartedWithOrder(2.0);
        h.Broker.AdvanceTo(A.AddMinutes(70.0));
        h.Broker.Account = ExecutionData.Account(98000.01, 98000.01, 98000.01);
        h.Broker.AddClosedTrade(Closed(902, A.AddMinutes(70.0), -499.99, 98000.01, "ZFVG|v1|XAUUSD|RH-20260304"));

        h.FeedTo(A.AddMinutes(90.0));

        Assert.NotNull(h.ReferenceOrder());
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Equal(0, h.Journal.Count("action", "duty=monthly-limit "));
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Bot_LimitReached_AnOpenPositionKeepsItsStopAndTargetAndIsNotClosed()
    {
        (BotHarness h, _) = StartedWithOrder(2.0);
        h.FillReferenceOrder(A.AddMinutes(65.0));
        h.Core.OnTradeEvent();
        PositionInfo before = Assert.Single(h.Broker.GetPositions());

        LoseFiveHundredMore(h);
        h.FeedTo(A.AddHours(3.0));
        h.Timer(A.AddHours(3.0).AddMinutes(1.0));

        PositionInfo after = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(RecoveryData.Stop, after.StopLoss);
        Assert.Equal(RecoveryData.Target, after.TakeProfit);
        Assert.Equal(before.VolumeUnits, after.VolumeUnits);
        Assert.Equal(0, h.Count(BrokerCall.Close));
        Assert.Equal(0, h.Count(BrokerCall.Modify));
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Bot_LimitReached_TheTimedExitOfASessionRangePositionStillRuns()
    {
        DateTime start = Range.Utc(2026, 3, 4, 6, 0);
        DateTime exitUtc = Range.Utc(2026, 3, 4, 21, 30);
        BotParameters parameters = Range.Parameters() with { Risk = Range.Parameters().Risk with { MonthlyLossLimitPercent = 2.0 } };
        BotHarness h = Range.Started(start, Range.Bars(Range.Winter), parameters: parameters);
        DateTime placement = Range.Evaluation(Range.Winter);
        h.FeedTo(placement);
        h.Broker.AdvanceTo(placement.AddMinutes(20.0));
        h.Broker.Tick(Range.Symbol, 100.4, 100.41);
        h.Core.OnTradeEvent();
        PositionInfo position = Assert.Single(h.Broker.GetPositions());

        // 2,000 lost on 2 March by a manual trade: the limit is reached.
        h.Broker.Account = ExecutionData.Account(98000.0, 98000.0, 98000.0);
        h.Broker.AddClosedTrade(Closed(990, RecoveryData.At(2, 12, 0), -2000.0, 98000.0));
        h.FeedTo(exitUtc.AddMinutes(-15.0));
        DateTime now = h.Broker.ServerTimeUtc;
        var month = new RiskSnapshot(now, h.Broker.GetAccount(), Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), h.Broker.GetHistory(MarchStart), MarchStart, Array.Empty<SymbolInfo>());
        MonthlyLossStatus status = new RiskEngine(parameters.Risk).MonthlyLoss(month);
        Assert.True(status.Reached);
        Assert.Equal(2000.0, status.Limit, 9);
        Assert.True(status.Loss >= 2000.0);
        PositionInfo open = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(position.StopLoss, open.StopLoss);
        Assert.Equal(position.TakeProfit, open.TakeProfit);
        Assert.Equal(0, h.Count(BrokerCall.Close));

        h.FeedTo(exitUtc);
        h.Core.OnTradeEvent();

        Assert.Empty(h.Broker.GetPositions());
        BrokerRequest close = Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close);
        Assert.Equal(position.Id, close.TargetId);
        Assert.Equal(1, h.Journal.Count("action", "duty=range-exit "));
        Assert.Equal(0, h.Count(BrokerCall.Modify));
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Bot_LimitZero_IdenticalJournalBrokerCallsAndHistoryReadsToARunWithoutTheField()
    {
        (BotHarness without, List<DateTime> withoutSince) = StartedWithOrder(null);
        (BotHarness zero, List<DateTime> zeroSince) = StartedWithOrder(0.0);

        foreach (BotHarness h in new[] { without, zero })
        {
            LoseFiveHundredMore(h);
            h.FeedTo(A.AddHours(2.0));
            h.Timer(A.AddHours(2.0).AddMinutes(1.0));
        }

        // Read counts first: the assertions below read the broker too.
        Assert.Equal(without.Broker.QueryCount, zero.Broker.QueryCount);
        Assert.Equal(without.Journal.Entries, zero.Journal.Entries);
        Assert.Equal(without.Broker.Requests, zero.Broker.Requests);
        Assert.NotNull(zero.ReferenceOrder());
        Assert.Equal(0, zero.Count(BrokerCall.Cancel));
        Assert.Equal(withoutSince, zeroSince);
        Assert.DoesNotContain(MarchStart, zeroSince);
        Assert.Equal(0, zero.Journal.Count("action", "duty=monthly-limit "));
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Bot_LimitOnHistoryUnreadable_NoCancellationOneJournalLinePerBarNoError()
    {
        (BotHarness h, _) = StartedWithOrder(2.0);
        LoseFiveHundredMore(h);
        h.Port.History = s => s == MarchStart ? throw new InvalidOperationException("history unavailable") : h.Broker.GetHistory(s);

        h.FeedTo(A.AddMinutes(75.0));

        Assert.NotNull(h.ReferenceOrder());
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Equal(1, h.Journal.Count("risk", "event=monthly-limit-unavailable reason=InvalidOperationException:\\shistory\\sunavailable"));
        Assert.Empty(h.Journal.Lines("error"));
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Bot_LimitOnBrokerReturnsNoHistory_NoCancellationOneJournalLine()
    {
        (BotHarness h, _) = StartedWithOrder(2.0);
        LoseFiveHundredMore(h);
        h.Port.History = s => s == MarchStart ? null! : h.Broker.GetHistory(s);

        h.FeedTo(A.AddMinutes(75.0));

        Assert.NotNull(h.ReferenceOrder());
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Equal(1, h.Journal.Count("risk", "event=monthly-limit-unavailable reason=InvalidOperationException:\\sThe\\sbroker\\sreturned\\sno\\shistory."));
        Assert.Empty(h.Journal.Lines("error"));
    }

    // ------------------------------------------------------------------ the resend of an unclear placement

    /// <summary>
    /// The validator's case at InitialCapital 200,000 (limit 4,000): 3,800 lost on 2 March, so the reference
    /// signal passes at 08:45; its placement is unclear and nothing reached the broker. At 08:50 a trade of
    /// another instance loses 300 (4,100 in the month). The 09:00 close resolves the placement.
    /// </summary>
    private static BotHarness UnclearAtTheConfirmation(double? limit, Func<BotHarness, Func<DateTime, IReadOnlyList<ClosedTradeInfo>>>? history = null)
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(A, ExecutionData.Account(196200.0, 196200.0, 196200.0)));
        h.Broker.AddClosedTrade(Closed(901, RecoveryData.At(2, 12, 0), -3800.0, 196200.0));
        BotParameters parameters = RecoveryData.Parameters();
        RiskParameters risk = parameters.Risk with { InitialCapital = 200000.0 };
        risk = limit.HasValue ? risk with { MonthlyLossLimitPercent = limit.Value } : risk;
        h.Start(A, parameters with { Risk = risk });
        h.FeedTo(A.AddMinutes(30.0));
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        h.FeedTo(RecoveryData.Confirmation);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());

        h.Broker.AdvanceTo(A.AddMinutes(50.0));
        h.Broker.Account = ExecutionData.Account(195900.0, 195900.0, 195900.0);
        h.Broker.AddClosedTrade(Closed(902, A.AddMinutes(50.0), -300.0, 195900.0, "ZFVG|v1|XAUUSD|RH-20260304"));
        if (history != null)
        {
            h.Port.History = history(h);
        }

        h.FeedTo(A.AddMinutes(60.0));
        return h;
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Resend_LimitReachedBeforeTheNextBar_NotResentNothingPlacedJournaled()
    {
        BotHarness h = UnclearAtTheConfirmation(2.0);

        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(1, h.Journal.Count("order", "outcome=Abandoned detail=monthly\\sloss\\slimit\\sreached,\\snot\\sresent"));
        Assert.Equal(1, h.Journal.Count("skip", "reason=UnclearOutcomeNotResent"));

        h.FeedTo(A.AddHours(2.0));
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Resend_LimitOnMonthHistoryUnreadable_NotResentNothingPlacedJournaled()
    {
        BotHarness h = UnclearAtTheConfirmation(2.0, harness => s => s == MarchStart ? throw new InvalidOperationException("history unavailable") : harness.Broker.GetHistory(s));

        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Journal.Count("risk", "event=monthly-limit-unavailable"));
        Assert.Equal(1, h.Journal.Count("order", "outcome=Abandoned detail=monthly\\sloss\\slimit\\sstatus\\sunknown,\\snot\\sresent"));
        Assert.Empty(h.Journal.Lines("error"));
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Resend_LimitZero_ResentAsBeforeIdenticalToARunWithoutTheField()
    {
        BotHarness zero = UnclearAtTheConfirmation(0.0);
        BotHarness without = UnclearAtTheConfirmation(null);

        Assert.Equal(without.Broker.QueryCount, zero.Broker.QueryCount);
        Assert.Equal(without.Journal.Entries, zero.Journal.Entries);
        Assert.Equal(without.Broker.Requests, zero.Broker.Requests);
        Assert.Equal(2, zero.Count(BrokerCall.Place));
        Assert.Single(zero.Broker.GetPendingOrders(), order => order.Label == RecoveryData.Label);
        Assert.Equal(0, zero.Journal.Count("order", "outcome=Abandoned"));
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Resend_LimitOnNotReached_ResentAsWithTheLimitOff()
    {
        // Nothing more is lost before the 09:00 close: 3,800 is below the 4,000 limit, so the resend goes out.
        var h = new BotHarness(broker: BotHarness.NewBroker(A, ExecutionData.Account(196200.0, 196200.0, 196200.0)));
        h.Broker.AddClosedTrade(Closed(901, RecoveryData.At(2, 12, 0), -3800.0, 196200.0));
        BotParameters parameters = RecoveryData.Parameters();
        h.Start(A, parameters with { Risk = parameters.Risk with { InitialCapital = 200000.0, MonthlyLossLimitPercent = 2.0 } });
        h.FeedTo(A.AddMinutes(30.0));
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        h.FeedTo(A.AddMinutes(60.0));

        Assert.Equal(2, h.Count(BrokerCall.Place));
        Assert.Single(h.Broker.GetPendingOrders(), order => order.Label == RecoveryData.Label);
    }

    // ------------------------------------------------------------------ smaller cases

    [Fact]
    [Trait("Req", "RSK-020")]
    public void Filters_MonthStartBalanceNotPositive_RefusedAsRiskInvalidInput_NoOrder()
    {
        // Balance 1,000 after +2,000 in January: S = -1,000.
        ClosedTradeInfo[] history = { Closed(1, ExecutionData.At(5, 12, 0), 2000.0, 1000.0) };

        FilterRun run = FilterRun.Evaluate(Fixed(2.0) with { InitialCapital = 10000.0 }, 1000.0, _ => history);

        SkipRecord skip = Assert.Single(run.Events.Skips);
        Assert.Equal(SkipReason.RiskInvalidInput, skip.Reason);
        Assert.Contains("month-start balance", skip.Detail, StringComparison.Ordinal);
        Assert.Empty(run.Broker.Requests);
    }

    [Theory]
    [Trait("Req", "RSK-020")]
    [InlineData(true)]
    [InlineData(false)]
    public void Filters_LadderOrRecoveryHistoryThrowsWithTheLimitOn_NoOrder(bool ladder)
    {
        RiskParameters risk = ladder ? Fixed(2.0) with { LadderEnabled = true } : Fixed(2.0) with { SizingPlan = SizingPlan.Recovery };

        // The sizing reads the history as before this work package; its exception leaves the filters (the bot's
        // event boundary journals and counts it) and nothing is sent.
        Assert.Throws<InvalidOperationException>(() => FilterRun.Evaluate(risk, 205000.0, _ => throw new InvalidOperationException("The broker history could not be read.")));
    }

    /// <summary>One EURUSD instance's filters on a FakeBroker with the history reads recorded (or replaced).</summary>
    private sealed class FilterRun
    {
        private FilterRun(RiskParameters risk, double balance, Func<DateTime, IReadOnlyList<ClosedTradeInfo>>? historyOverride)
        {
            Broker = ExecutionHarness.DefaultBroker(account: ExecutionData.Account(balance, balance, balance));
            foreach (ClosedTradeInfo trade in MonthTo(balance))
            {
                Broker.AddClosedTrade(trade);
            }

            var port = new SwitchableBroker(Broker)
            {
                History = since =>
                {
                    Since.Add(since);
                    return historyOverride != null ? historyOverride(since) : Broker.GetHistory(since);
                },
            };
            var news = new NewsService(new NewsParameters(), new[] { NewsTestData.ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(NewsTestData.Failed()), true, ExecutionData.QuietNews);
            Filters = new PreTradeFilters(ExecutionData.ExecutionDefaults, ExecutionData.SignalDefaults, risk, port, news, Events);
        }

        public FakeBroker Broker { get; }

        public RecordingEvents Events { get; } = new();

        public PreTradeFilters Filters { get; }

        public List<DateTime> Since { get; } = new();

        public PreTradeResult Result { get; private set; } = null!;

        public static FilterRun Evaluate(RiskParameters risk, double balance, Func<DateTime, IReadOnlyList<ClosedTradeInfo>>? historyOverride = null)
        {
            var run = new FilterRun(risk, balance, historyOverride);
            run.Result = run.Filters.Evaluate(ExecutionData.Buy(), new PreTradeContext(ExecutionData.Symbol, BotState.Running, Array.Empty<Zone>(), false));
            return run;
        }
    }
}
