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

    [Fact]
    [Trait("Req", "RSK-016")]
    public void AfterOneLoss_Risks150_75000Units()
    {
        ExecutionHarness h = WithHistory(D.Recovery, D.NthTrade(1, "EURUSD", -100.0));

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(75000.0, order.VolumeUnits);
        Assert.Equal("e=1.10000;s=1.09800;t=1.10400;r=150.00;h=f681dff2", order.Comment);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void AfterTwoLosses_Risks225_RoundedDownTo112000UnitsAndRiskMoney224()
    {
        ExecutionHarness h = WithHistory(D.Recovery, D.NthTrade(1, "EURUSD", -100.0), D.NthTrade(2, "EURUSD", -150.0));

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(112000.0, order.VolumeUnits);
        Assert.Equal(224.0, order.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void FirstTradeOfTheEpoch_Risks100()
    {
        ExecutionHarness h = D.Harness(D.Recovery);

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(50000.0, order.VolumeUnits);
        Assert.Equal(100.0, order.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void LongRun_CappedAt128PercentOfTheBalanceRead_NotAboveTheVolumeCap()
    {
        ExecutionHarness h = WithHistory(D.Recovery, D.NthTrade(1, "EURUSD", -10000.0));

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        // R = 2,560 (raw 5,100): 1,280,000 units, within the default 3,000,000.
        Assert.Equal(1280000.0, order.VolumeUnits);
        Assert.Equal(2560.0, order.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void TheDivisor_IsTheInstancesRewardRisk()
    {
        var filters = new SymbolFilters(D.Recovery, D.Account(), signal: ExecutionData.SignalDefaults with { RewardRisk = 3.0 });
        filters.Broker.AddClosedTrade(D.NthTrade(1, "EURUSD", -100.0));

        PreTradeResult result = filters.Evaluate("EURUSD");

        // (100 + 3 x 100) / 3 = 133.33: 66,666.7 -> 66,000 units.
        Assert.True(result.IsAllowed, result.Skip?.Detail);
        Assert.Equal(66000.0, result.Order!.VolumeUnits);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    [Trait("Req", "RSK-002")]
    public void RecoveryWithTheSanityCapOnAt055_TheCappedAmountIsSkipped()
    {
        ExecutionHarness h = WithHistory(D.Recovery with { SanityCapEnabled = true }, D.NthTrade(1, "EURUSD", -10000.0));

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
        // The GBPUSD losses of the ladder data: the EURUSD run is empty, so 100 at the balance of 100,000.
        ExecutionHarness h = LadderHarness(ExecutionData.RiskDefaults with { SizingPlan = SizingPlan.Recovery }, losses: true);

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(50000.0, order.VolumeUnits);
    }

    // ------------------------------------------------------------------ RSK-017 per pair, restart, fail closed

    [Fact]
    [Trait("Req", "RSK-017")]
    public void EachPairOnItsOwn_EurUsdRisks225AndGbpUsd100()
    {
        var filters = new SymbolFilters(D.Recovery, D.Account());
        filters.Broker.AddClosedTrade(D.Trade(1, "EURUSD", "E1", D.Placed(1), D.ClosedAt(1), -100.0));
        filters.Broker.AddClosedTrade(D.Trade(2, "GBPUSD", "G1", D.Placed(2), D.ClosedAt(2), -300.0));
        filters.Broker.AddClosedTrade(D.Trade(3, "EURUSD", "E2", D.Placed(3), D.ClosedAt(3), -150.0));
        filters.Broker.AddClosedTrade(D.Trade(4, "GBPUSD", "G2", D.Placed(4), D.ClosedAt(4), 200.0));

        PreTradeResult eur = filters.Evaluate("EURUSD");
        PreTradeResult gbp = filters.Evaluate("GBPUSD");
        PreTradeResult aud = filters.Evaluate("AUDUSD");

        Assert.Equal(112000.0, eur.Order!.VolumeUnits);
        Assert.Equal(50000.0, gbp.Order!.VolumeUnits);
        Assert.Equal("ZFVG|v1|GBPUSD|D-20260114-0400", gbp.Order!.Label);
        Assert.Equal(50000.0, aud.Order!.VolumeUnits);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Restart_AfterTwoLosses_TheThirdOrderHasTheSameVolume()
    {
        ClosedTradeInfo[] history = { D.NthTrade(1, "EURUSD", -104.2), D.NthTrade(2, "EURUSD", -157.35), D.NthTrade(3, "GBPUSD", -99.99) };
        ExecutionHarness before = WithHistory(D.Recovery, history);
        PlannedOrder first = before.OrderOf(ExecutionData.Buy());

        // A new set of filters on the same broker is a restart: nothing is carried over.
        var restarted = new ExecutionHarness(before.Broker, risk: D.Recovery);
        PlannedOrder again = restarted.OrderOf(ExecutionData.Buy());
        ExecutionHarness reordered = WithHistory(D.Recovery, Enumerable.Reverse(history).ToArray());
        PlannedOrder other = reordered.OrderOf(ExecutionData.Buy());

        // L = 261.55, R = 230.775: 115,387.5 -> 115,000 units.
        Assert.Equal(115000.0, first.VolumeUnits);
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

    private static SymbolFilters AfterTodaysLoss(bool backtest = false, bool earlierRun = true)
    {
        // Balance 197,440 after a loss of 2,560 closed today, M = 200,000. With the earlier EURUSD loss of 5,000, L = 7,560.
        var filters = new SymbolFilters(D.Recovery, D.Account(197440.0), backtest);
        if (earlierRun)
        {
            filters.Broker.AddClosedTrade(D.Trade(1, "EURUSD", "OLD", ExecutionData.At(12, 10, 0), ExecutionData.At(12, 12, 0), -5000.0));
        }

        filters.Broker.AddClosedTrade(D.Trade(2, "EURUSD", "TODAY", ExecutionData.At(14, 7, 0), ExecutionData.At(14, 8, 0), -2560.0));
        return filters;
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    [Trait("Req", "RSK-016")]
    public void CappedRecoveryAmountAfterTodaysLoss_RefusedByTheDailyBudget()
    {
        SymbolFilters filters = AfterTodaysLoss();

        PreTradeResult result = filters.Evaluate("EURUSD");

        // R = 1.28% of 197,440 = 2,527.232 -> 1,263,000 units, risk 2,526; used 2,560 + 2,526 = 5,086 above 4,000.
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
        SymbolFilters filters = AfterTodaysLoss(backtest: true);

        PreTradeResult result = filters.Evaluate("EURUSD");

        Assert.True(result.IsAllowed, result.Skip?.Detail);
        Assert.Equal(1263000.0, result.Order!.VolumeUnits);
        Assert.Equal(2526.0, result.Order!.RiskMoney, 6);
        Assert.Contains("worst-case daily loss 5086", filters.Waived ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    [Trait("Req", "RSK-017")]
    public void AccountWide_EurUsdLossThenGbpUsd100Allowed_WithAnAudUsdPositionRefused()
    {
        SymbolFilters allowed = AfterTodaysLoss(earlierRun: false);
        SymbolFilters refused = AfterTodaysLoss(earlierRun: false);
        refused.Broker.AddPosition("AUDUSD", Side.Buy, 700000.0, 1.1, 1.098, 1.104, D.Label("AUDUSD", "D-20260114-0000"), "e=;s=;t=");

        PreTradeResult gbp = allowed.Evaluate("GBPUSD");
        PreTradeResult blocked = refused.Evaluate("GBPUSD");

        // GBPUSD has no run of its own: R = 100, 50,000 units; used 2,560 + 100 = 2,660; with 1,400 more at the AUDUSD stop 4,060.
        Assert.True(gbp.IsAllowed, gbp.Skip?.Detail);
        Assert.Equal(50000.0, gbp.Order!.VolumeUnits);
        Assert.Equal(SkipReason.DailyBudgetExceeded, blocked.Skip!.Reason);
        Assert.Equal(4060.0, blocked.Skip!.Measured, 6);
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
