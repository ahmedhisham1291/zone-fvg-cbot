using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-003: a trade is skipped if free margin after the trade would be below 50% of equity; the
/// margin estimate is the one returned by the broker port. Equity 100,000, free margin 90,000:
/// the trade may use at most 40,000. The reference buy of 250,000 units at 0.16 USD per unit
/// needs exactly 40,000; at 0.16004 it needs 40,010, leaving 49,990.
/// </summary>
public class Rsk003MarginScenarios
{
    private static ExecutionHarness WithMargin(double marginPerUnit)
    {
        FakeBroker broker = ExecutionHarness.DefaultBroker(account: ExecutionData.Account(100000.0, 100000.0, 90000.0));
        broker.MarginPerUnit = marginPerUnit;
        return new ExecutionHarness(broker);
    }

    [Theory]
    [Trait("Req", "RSK-003")]
    [InlineData(Side.Buy, 250000.0)]
    [InlineData(Side.Sell, 238000.0)]
    public void MarginEstimate_IsTheOneReturnedByTheBroker(Side side, double volume)
    {
        ExecutionHarness h = WithMargin(0.04);

        h.EvaluateAndPlace(side == Side.Buy ? ExecutionData.Buy() : ExecutionData.Sell());

        (string symbol, Side asked, double units, double margin) = Assert.Single(h.Broker.MarginRequests);
        Assert.Equal(("EURUSD", side, volume), (symbol, asked, units));
        Assert.Equal(volume * 0.04, margin, 9);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-003")]
    public void FreeMarginAfterExactlyHalfOfEquity_IsPlaced()
    {
        ExecutionHarness h = WithMargin(0.16);

        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ExecutionData.Buy())!.Outcome);
        Assert.Equal(40000.0, Assert.Single(h.Broker.MarginRequests).Margin, 6);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-003")]
    public void FreeMarginAfterBelowHalfOfEquity_IsSkipped()
    {
        ExecutionHarness h = WithMargin(0.16004);

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.FreeMarginTooLow, skip.Reason);
        Assert.Equal(49990.0, skip.Measured, 6);
        Assert.Equal(50000.0, skip.Limit, 6);
        Assert.Empty(h.Events.Alerts);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-003")]
    public void MarginEstimateNotANumber_IsSkippedFailClosed()
    {
        ExecutionHarness h = WithMargin(double.NaN);

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.RiskInvalidInput, skip.Reason);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }
}
