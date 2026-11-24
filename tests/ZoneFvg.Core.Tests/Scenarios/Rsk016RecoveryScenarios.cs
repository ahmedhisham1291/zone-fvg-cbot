using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;
using D = ZoneFvg.Core.Tests.RecoverySizingData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-016, RSK-017 and RSK-006 through the execution layer: with SizingPlan Recovery the risk
/// money of a new trade is R from the closed trades of the instance's symbol, read from the
/// broker at every signal, and every other check applies after it; with Ladder nothing changes.
/// </summary>
/// <remarks>
/// Reference buy of <see cref="ExecutionData"/>: entry 1.10000, stop 1.09800 (20 pips), so the
/// volume is R / 0.002 rounded down to 1,000 units. Defaults of spec v0.12 (200,000, 2%, caps off)
/// unless the old account of <see cref="RiskData.Parameters"/> is named. Earlier trades close at
/// 12:00 UTC on 2 to 11 January.
/// </remarks>
public class Rsk016RecoveryScenarios
{
    private static ExecutionHarness WithHistory(RiskParameters risk, params ClosedTradeInfo[] history)
    {
        ExecutionHarness h = D.Harness(risk);
        foreach (ClosedTradeInfo trade in history)
        {
            h.Broker.AddClosedTrade(trade);
        }

        return h;
    }

    // ------------------------------------------------------------------ RSK-016 sizing

    // RecoveryRunLossPercent 100 makes B1 = 4,060.27 and B2 = 6,090.40, above every cap of these tests.
    private static RiskParameters Large => D.Recovery with { RecoveryRunLossPercent = 100.0 };

    [Fact]
    [Trait("Req", "RSK-016")]
    public void AfterAWin_P1Risks64_6557_32000UnitsAndRiskMoney64()
    {
        ExecutionHarness h = WithHistory(D.Recovery, D.NthTrade(1, "EURUSD", 81.2054));

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(32000.0, order.VolumeUnits);
        Assert.Equal("e=1.10000;s=1.09800;t=1.10400;r=64.00;h=f681dff2", order.Comment);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void FirstEntryOfTheEpoch_Risks40_6027()
    {
        ExecutionHarness h = D.Harness(D.Recovery);

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        // 40.6027 / 0.002 = 20,301.3 -> 20,000 units.
        Assert.Equal(20000.0, order.VolumeUnits);
        Assert.Equal(40.0, order.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void RawAboveTheCap_CappedAt128PercentOfTheBalanceRead_NotAboveTheVolumeCap()
    {
        ExecutionHarness h = D.Harness(Large);

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        // R = 2,560 (raw 4,060.27): 1,280,000 units, within the default 3,000,000.
        Assert.Equal(1280000.0, order.VolumeUnits);
        Assert.Equal(2560.0, order.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void TheMultipliers_FollowTheInstancesRewardRisk()
    {
        var filters = new SymbolFilters(D.Recovery, D.Account(), signal: ExecutionData.SignalDefaults with { RewardRisk = 3.0 });

        PreTradeResult result = filters.Evaluate("EURUSD");

        // RR 3: B1 = 2,000 / 26.966... = 74.1670: 37,083.5 -> 37,000 units.
        Assert.True(result.IsAllowed, result.Skip?.Detail);
        Assert.Equal(37000.0, result.Order!.VolumeUnits);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    [Trait("Req", "RSK-002")]
    public void RecoveryWithTheSanityCapOnAt055_TheCappedAmountIsSkipped()
    {
        ExecutionHarness h = D.Harness(Large with { SanityCapEnabled = true });

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.RiskAboveSanityCap, skip.Reason);
        Assert.Equal(2560.0, skip.Measured, 6);
        Assert.Equal(1100.0, skip.Limit, 6);
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.SanityCap));
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    [Trait("Req", "RSK-018")]
    public void PlanLadderExplicitly_TheLadderScenariosGiveTheSameOrders()
    {
        // The data of Rsk013LadderScenarios: the old account, ladder on, GBPUSD trades before the scenario day.
        RiskParameters ladder = ExecutionData.RiskDefaults with { LadderEnabled = true };
        ExecutionHarness byDefault = LadderHarness(ladder, losses: false);
        ExecutionHarness explicitly = LadderHarness(ladder with { SizingPlan = SizingPlan.Ladder }, losses: false);
        ExecutionHarness afterLosses = LadderHarness(ladder with { SizingPlan = SizingPlan.Ladder }, losses: true);

        PlaceResult? a = byDefault.EvaluateAndPlace(ExecutionData.Buy());
        PlaceResult? b = explicitly.EvaluateAndPlace(ExecutionData.Buy());
        PlannedOrder steady = afterLosses.OrderOf(ExecutionData.Buy());

        Assert.Equal(a!.Outcome, b!.Outcome);
        PendingOrderInfo first = Assert.Single(explicitly.Broker.GetPendingOrders());
        Assert.Equal(44000.0, first.VolumeUnits);
        Assert.Equal("e=1.10000;s=1.09800;t=1.10400;r=88.00;h=f681dff2", first.Comment);
        Assert.Equal(byDefault.Broker.Requests, explicitly.Broker.Requests);
        Assert.Equal(byDefault.Events.Skips, explicitly.Events.Skips);
        Assert.Equal(byDefault.Events.Alerts, explicitly.Events.Alerts);
        Assert.Equal(149000.0, steady.VolumeUnits);
        Assert.Equal(298.0, steady.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void SameBrokerDataUnderRecovery_TheOtherSymbolsLossesDoNotCount()
    {
        // The GBPUSD losses of the ladder data: EURUSD is at p1 with all columns 0. The old account of
        // 100,000 halves B1: 20.3013, 10,150.7 -> 10,000 units.
        ExecutionHarness h = LadderHarness(ExecutionData.RiskDefaults with { SizingPlan = SizingPlan.Recovery }, losses: true);

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(10000.0, order.VolumeUnits);
    }

    // ------------------------------------------------------------------ RSK-017 per pair, restart, fail closed

    [Fact]
    [Trait("Req", "RSK-017")]
    public void EachPairOnItsOwn_EurUsdAfterAWinGbpUsdAfterALossAudUsdFresh()
    {
        var filters = new SymbolFilters(D.Recovery, D.Account());
        filters.Broker.AddClosedTrade(D.Trade(1, "EURUSD", "E1", D.Placed(1), D.ClosedAt(1), 81.2054));
        filters.Broker.AddClosedTrade(D.Trade(2, "GBPUSD", "G1", D.Placed(2), D.ClosedAt(2), -40.6027));

        PreTradeResult eur = filters.Evaluate("EURUSD");
        PreTradeResult gbp = filters.Evaluate("GBPUSD");
        PreTradeResult aud = filters.Evaluate("AUDUSD");

        // EURUSD p1 with C = 1: 64.6557 -> 32,000; GBPUSD p2: 60.9040 -> 30,000; AUDUSD p1: 40.6027 -> 20,000.
        Assert.Equal(32000.0, eur.Order!.VolumeUnits);
        Assert.Equal(30000.0, gbp.Order!.VolumeUnits);
        Assert.Equal("ZFVG|v1|GBPUSD|D-20260114-0400", gbp.Order!.Label);
        Assert.Equal(20000.0, aud.Order!.VolumeUnits);
    }

    [Theory]
    [Trait("Req", "RSK-017")]
    [Trait("Req", "RSK-016")]
    [InlineData(0.0)]
    [InlineData(-100000.0)]
    [InlineData(250000.0)]
    public void Isolation_AnotherSymbolsResultInTheBrokerHistory_ChangesNoEurUsdColumnOrVolume(double audUsdNet)
    {
        // Cap 0.05% (freeze limit 100 on 200,000), balance 300,000 (cap 150 now). EURUSD has three wins at p1;
        // the third (raw 102.9578) froze the steps, so C = 2 and the next p1 risks 102.9578: 51,478.9 -> 51,000 units.
        // An AUDUSD result after it, read by the same broker call, changes nothing.
        var filters = new SymbolFilters(D.Recovery with { RecoveryMaxRiskPercent = 0.05 }, D.Account(300000.0));
        filters.Broker.AddClosedTrade(D.Trade(1, "EURUSD", "T01", D.Placed(1), D.ClosedAt(1), 81.2054));
        filters.Broker.AddClosedTrade(D.Trade(2, "EURUSD", "T02", D.Placed(2), D.ClosedAt(2), 129.3115));
        filters.Broker.AddClosedTrade(D.Trade(3, "EURUSD", "T03", D.Placed(3), D.ClosedAt(3), 205.9156));
        if (audUsdNet != 0.0)
        {
            filters.Broker.AddClosedTrade(D.Trade(99, "AUDUSD", "BIG", D.Placed(3).AddHours(3.0), D.Placed(3).AddHours(4.0), audUsdNet));
        }

        PreTradeResult eur = filters.Evaluate("EURUSD");

        Assert.True(eur.IsAllowed, eur.Skip?.Detail);
        Assert.Equal(51000.0, eur.Order!.VolumeUnits);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Restart_AfterAWinAndTwoLosses_TheNextOrderHasTheSameVolume()
    {
        ClosedTradeInfo[] history = { D.NthTrade(1, "EURUSD", 81.2054), D.NthTrade(2, "EURUSD", -64.6557), D.NthTrade(3, "EURUSD", -60.904), D.NthTrade(4, "GBPUSD", -99.99) };
        ExecutionHarness before = WithHistory(D.Recovery, history);
        PlannedOrder first = before.OrderOf(ExecutionData.Buy());

        // A new set of filters on the same broker is a restart: nothing is carried over.
        var restarted = new ExecutionHarness(before.Broker, risk: D.Recovery);
        PlannedOrder again = restarted.OrderOf(ExecutionData.Buy());
        ExecutionHarness reordered = WithHistory(D.Recovery, Enumerable.Reverse(history).ToArray());
        PlannedOrder other = reordered.OrderOf(ExecutionData.Buy());

        // p3 with E = 0: 91.3561 -> 45,678 -> 45,000 units.
        Assert.Equal(45000.0, first.VolumeUnits);
        Assert.Equal(first.VolumeUnits, again.VolumeUnits);
        Assert.Equal(first.RiskMoney, again.RiskMoney);
        Assert.Equal(first.Comment, again.Comment);
        Assert.Equal(first.VolumeUnits, other.VolumeUnits);
        Assert.Equal(first.RiskMoney, other.RiskMoney);
    }

    public static IEnumerable<object[]> InvalidHistory()
    {
        yield return new object[] { "NaN", D.Trade(1, "EURUSD", "A", D.Placed(1), D.ClosedAt(1), double.NaN) };
        yield return new object[] { "local close", D.Trade(1, "EURUSD", "A", D.Placed(1), DateTime.SpecifyKind(D.ClosedAt(1), DateTimeKind.Local), -100.0) };
        yield return new object[] { "close after T", D.Trade(1, "EURUSD", "A", ExecutionData.Now.AddHours(-1.0), ExecutionData.Now.AddHours(1.0), -100.0) };
    }

    [Theory]
    [Trait("Req", "RSK-017")]
    [MemberData(nameof(InvalidHistory))]
    public void InvalidClosedTradeOfTheSymbol_NoOrderBrokerDataInvalid_TheLadderAsToday(string name, ClosedTradeInfo trade)
    {
        ExecutionHarness recovery = WithHistory(D.Recovery, trade);
        ExecutionHarness ladder = WithHistory(ExecutionData.RiskDefaults with { LadderEnabled = true }, trade);

        SkipRecord recoverySkip = recovery.SkipOf(ExecutionData.Buy());
        SkipRecord ladderSkip = ladder.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.BrokerDataInvalid, recoverySkip.Reason);
        Assert.StartsWith("Recovery sizing (RSK-017): ", recoverySkip.Detail, StringComparison.Ordinal);
        Assert.Equal(0, recovery.Broker.WriteRequestCount);
        Assert.Equal(SkipReason.BrokerDataInvalid, ladderSkip.Reason);
        Assert.StartsWith("Ladder sizing (RSK-013): ", ladderSkip.Detail, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(name));
    }

    // ------------------------------------------------------------------ RSK-006 daily budget with Recovery

    private static SymbolFilters AfterTodaysLoss(RiskParameters risk, bool backtest = false)
    {
        // Balance 197,440 after a loss of 2,560 on EURUSD closed today, M = 200,000.
        var filters = new SymbolFilters(risk, D.Account(197440.0), backtest);
        filters.Broker.AddClosedTrade(D.Trade(2, "EURUSD", "TODAY", ExecutionData.At(14, 7, 0), ExecutionData.At(14, 8, 0), -2560.0));
        return filters;
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    [Trait("Req", "RSK-016")]
    public void CappedRecoveryAmountAfterTodaysLoss_RefusedByTheDailyBudget()
    {
        SymbolFilters filters = AfterTodaysLoss(Large);

        PreTradeResult result = filters.Evaluate("EURUSD");

        // EURUSD p2 (raw 6,090.40) is capped at 1.28% of 197,440 = 2,527.232 -> 1,263,000 units, risk 2,526;
        // used 2,560 + 2,526 = 5,086 above 4,000.
        Assert.False(result.IsAllowed);
        Assert.Equal(SkipReason.DailyBudgetExceeded, result.Skip!.Reason);
        Assert.Equal(5086.0, result.Skip!.Measured, 6);
        Assert.Equal(4000.0, result.Skip!.Limit, 6);
        Assert.Null(filters.Waived);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CappedRecoveryAmountAfterTodaysLoss_InABacktest_WouldBlockAndIsPlaced()
    {
        SymbolFilters filters = AfterTodaysLoss(Large, backtest: true);

        PreTradeResult result = filters.Evaluate("EURUSD");

        Assert.True(result.IsAllowed, result.Skip?.Detail);
        Assert.Equal(1263000.0, result.Order!.VolumeUnits);
        Assert.Equal(2526.0, result.Order!.RiskMoney, 6);
        Assert.Contains("worst-case daily loss 5086", filters.Waived ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    [Trait("Req", "RSK-017")]
    public void AccountWide_EurUsdLossThenGbpUsdP1Allowed_WithAnAudUsdPositionRefused()
    {
        // GBPUSD with a volume step of 1 unit, so its 40.6027 at p1 is 20,301 units, risk 40.602.
        SymbolFilters allowed = AfterTodaysLoss(D.Recovery);
        SymbolFilters refused = AfterTodaysLoss(D.Recovery);
        foreach (SymbolFilters filters in new[] { allowed, refused })
        {
            filters.Broker.SetSymbol(D.Pair("GBPUSD", "GBP") with { VolumeStepUnits = 1.0, VolumeMinUnits = 1.0 });
        }

        refused.Broker.AddPosition("AUDUSD", Side.Buy, 700000.0, 1.1, 1.098, 1.104, D.Label("AUDUSD", "D-20260114-0000"), "e=;s=;t=");

        PreTradeResult gbp = allowed.Evaluate("GBPUSD");
        PreTradeResult blocked = refused.Evaluate("GBPUSD");

        // Used 2,560 + 40.602 = 2,600.602; with 1,400 more at the AUDUSD stop 4,000.602 > 4,000.
        Assert.True(gbp.IsAllowed, gbp.Skip?.Detail);
        Assert.Equal(20301.0, gbp.Order!.VolumeUnits);
        Assert.Equal(SkipReason.DailyBudgetExceeded, blocked.Skip!.Reason);
        Assert.Equal(4000.602, blocked.Skip!.Measured, 6);
    }

    private static ExecutionHarness LadderHarness(RiskParameters risk, bool losses)
    {
        var h = new ExecutionHarness(risk: risk);
        h.Broker.SetSymbol(RiskData.GbpUsd);
        h.Broker.SetSymbol(RiskData.AudUsd);
        if (losses)
        {
            for (int day = 5; day <= 8; day++)
            {
                DateTime open = ExecutionData.At(day, 10, 0);
                h.Broker.AddClosedTrade(new ClosedTradeInfo(day, "GBPUSD", "ZFVG|v1|GBPUSD|" + day.ToString(System.Globalization.CultureInfo.InvariantCulture), open, open.AddHours(2.0), -100.0, 0.0, 0.0, 100000.0 - 100.0));
            }
        }

        return h;
    }
}
