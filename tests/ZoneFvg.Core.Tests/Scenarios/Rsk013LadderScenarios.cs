using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-013 through the execution layer: the ladder percent replaces the fixed RiskPercent of
/// RSK-001, computed at every signal from the broker's positions, pending orders and history
/// since RiskEpochDate; every other check stays as it was.
/// </summary>
/// <remarks>
/// Reference buy of <see cref="ExecutionData"/>: balance 100,000, stop 20 pips, pip value 0.0001
/// per unit, step 1,000. Volume = 100,000 x percent / 100 / (20 x 0.0001), rounded down:
/// entry 1 (0.08886%) 44,430 -> 44,000, risk 88.00; entry 2 (0.13329%) 66,645 -> 66,000, risk
/// 132.00; steady state (0.2999025%) 149,951.25 -> 149,000, risk 298.00; cap (0.5%) 250,000,
/// risk 500.00. Earlier trades are on other symbols, between 5 and 9 January, after the epoch of
/// 1 January and before the scenario day.
/// </remarks>
public class Rsk013LadderScenarios
{
    private static RiskParameters LadderOn => ExecutionData.RiskDefaults with { LadderEnabled = true };

    private static ExecutionHarness Harness()
    {
        var h = new ExecutionHarness(risk: LadderOn);
        h.Broker.SetSymbol(RiskData.GbpUsd);
        h.Broker.SetSymbol(RiskData.AudUsd);
        return h;
    }

    private static void AddClosed(FakeBroker broker, int day, double net, string zone)
    {
        DateTime open = ExecutionData.At(day, 10, 0);
        broker.AddClosedTrade(new ClosedTradeInfo(day, "GBPUSD", "ZFVG|v1|GBPUSD|" + zone, open, open.AddHours(2.0), net, 0.0, 0.0, 100000.0 + net));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void FirstEntryOfTheEpoch_RisksPart1OfTheFirstDeal()
    {
        var h = Harness();

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(44000.0, order.VolumeUnits);
        Assert.Equal("e=1.10000;s=1.09800;t=1.10400;r=88.00;h=f681dff2", order.Comment);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void PendingBotOrderOnAnotherSymbol_TakesNumberOne_TheSignalIsEntryTwo()
    {
        var h = Harness();
        h.Broker.AddOrder("GBPUSD", Side.Buy, 50000.0, 1.3, 1.298, 1.304, ExecutionData.At(14, 12, 0), "ZFVG|v1|GBPUSD|D-20260114-0000", "e=;s=;t=");

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(66000.0, order.VolumeUnits);
        Assert.Equal(132.0, order.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void CancelledOrder_DoesNotTakeANumber()
    {
        var h = Harness();
        long id = h.Broker.AddOrder("GBPUSD", Side.Buy, 50000.0, 1.3, 1.298, 1.304, ExecutionData.At(14, 12, 0), "ZFVG|v1|GBPUSD|D-20260114-0000", "e=;s=;t=");
        Assert.Equal(OrderOutcome.Success, h.Broker.CancelOrder(id).Outcome);

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(44000.0, order.VolumeUnits);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void AfterLosses_EveryEntryRisksTheSteadyState()
    {
        var h = Harness();
        AddClosed(h.Broker, 5, -100.0, "A");
        AddClosed(h.Broker, 6, -100.0, "B");
        AddClosed(h.Broker, 7, -100.0, "C");
        AddClosed(h.Broker, 8, -100.0, "D");

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(149000.0, order.VolumeUnits);
        Assert.Equal(298.0, order.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void AfterWins_TheCapOf0_5PercentHolds_TheOtherLimitsStillApply()
    {
        var h = Harness();
        for (int day = 5; day <= 9; day++)
        {
            AddClosed(h.Broker, day, 100.0, "W" + day.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());
        h.Broker.MarginPerUnit = 0.3;
        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        Assert.Equal(250000.0, order.VolumeUnits);
        Assert.Equal(500.0, order.RiskMoney, 6);
        Assert.Equal(SkipReason.FreeMarginTooLow, skip.Reason);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void BreakEvenTrade_EndsTheRunOfWins()
    {
        var h = Harness();
        AddClosed(h.Broker, 5, 100.0, "A");
        AddClosed(h.Broker, 6, 100.0, "B");
        AddClosed(h.Broker, 7, 0.0, "C");

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        // New deal s = 0 (C broke even); the deals of entries C, B and A were started after 2, 1 and 0 wins:
        // 0.30 x (0.2962 + 0.1481 g^2 + 0.22215 g + 0.333225) = 0.407616% -> 203,807.9 units -> 203,000.
        double g = 1.5924;
        double percent = 0.30 * (0.2962 + (0.1481 * g * g) + (0.22215 * g) + 0.333225);
        Assert.Equal(203000.0, order.VolumeUnits);
        Assert.Equal(203000.0, Math.Floor(100000.0 * percent / 100.0 / 0.002 / 1000.0) * 1000.0);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Restart_SameBrokerData_GivesTheSameOrder()
    {
        var h = Harness();
        AddClosed(h.Broker, 5, 100.0, "A");
        AddClosed(h.Broker, 6, -50.0, "B");
        AddClosed(h.Broker, 7, 100.0, "C");
        PlannedOrder first = h.OrderOf(ExecutionData.Buy());

        var restarted = new ExecutionHarness(h.Broker, risk: LadderOn);
        PlannedOrder second = restarted.OrderOf(ExecutionData.Buy());

        Assert.Equal(first.VolumeUnits, second.VolumeUnits);
        Assert.Equal(first.RiskMoney, second.RiskMoney);
        Assert.Equal(first.Comment, second.Comment);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void LadderDisabled_GivesTheFixedRiskPercentOrder()
    {
        var h = new ExecutionHarness(risk: LadderOn with { LadderEnabled = false });
        h.Broker.SetSymbol(RiskData.GbpUsd);
        AddClosed(h.Broker, 5, -100.0, "A");

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(250000.0, order.VolumeUnits);
        Assert.Equal("e=1.10000;s=1.09800;t=1.10400;r=500.00;h=f681dff2", order.Comment);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void BrokenBotHistory_SkipsTheSignalAsInvalidBrokerData()
    {
        var h = Harness();
        AddClosed(h.Broker, 5, double.NaN, "A");

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.BrokerDataInvalid, skip.Reason);
        Assert.Empty(h.Broker.Requests);
    }
}
