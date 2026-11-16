using Xunit;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-004: a signal is skipped if spread plus round-trip commission, in pips, is above
/// CostToRiskMax (10%) of the stop distance. Stop 20 pips: the limit is 2.0 pips. Spread 1.5 pips;
/// commission 0.00005 USD per unit is 0.00005 / 0.0001 = 0.5 pip, so the cost is exactly 2.0;
/// 0.000051 per unit is 0.51 pip, a cost of 2.01.
/// </summary>
public class Exe004CostScenarios
{
    private static ExecutionHarness WithCosts(double commissionPerUnit)
    {
        return new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(spread: 0.00015, commissionPerUnit: commissionPerUnit)));
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void Buy_CostExactly10Percent_IsPlaced()
    {
        ExecutionHarness h = WithCosts(0.00005);

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void Buy_CostAbove10Percent_IsSkipped()
    {
        ExecutionHarness h = WithCosts(0.000051);

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.CostTooHigh, skip.Reason);
        Assert.Equal(2.01, skip.Measured, 9);
        Assert.Equal(2.0, skip.Limit, 9);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void Sell_CostExactly10PercentOfTheSpreadWidenedStop_IsPlaced()
    {
        // Signal stop 1.10185 + spread 0.00015 = 1.10200: 20 pips, limit 2.0.
        ExecutionHarness h = WithCosts(0.00005);

        PlannedOrder order = h.OrderOf(ExecutionData.Sell(stop: 1.10185));

        Assert.Equal(20.0, order.StopPips, 9);
        Assert.Equal(PlaceOutcome.Placed, h.Orders.TryPlace(order).Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void Sell_CostAbove10Percent_IsSkipped()
    {
        ExecutionHarness h = WithCosts(0.000051);

        SkipRecord skip = h.SkipOf(ExecutionData.Sell(stop: 1.10185));

        Assert.Equal(SkipReason.CostTooHigh, skip.Reason);
        Assert.Equal(2.01, skip.Measured, 9);
        Assert.Equal(2.0, skip.Limit, 9);
        ScenarioAssert.Global(h.Broker);
    }
}
