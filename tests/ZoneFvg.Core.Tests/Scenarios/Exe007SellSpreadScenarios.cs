using Xunit;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-007: for sell orders the current spread is added to the stop distance before sizing and
/// placement; buys are unchanged. Spread 1 pip: the sell stop 1.10200 becomes 1.10210, 21 pips,
/// volume 500 / (21 x 0.0001) = 238,095.2 rounded down to 238,000, risk 238,000 x 21 x 0.0001 =
/// 499.80. Spread 1.5 pips: 1.10215, 21.5 pips, volume 232,558.1 down to 232,000, risk 498.80.
/// The buy keeps stop 1.09800, 20 pips, 250,000 units, risk 500.
/// </summary>
public class Exe007SellSpreadScenarios
{
    [Theory]
    [Trait("Req", "EXE-007")]
    [InlineData(0.0001, 1.1021, 21.0, 238000.0, 499.8)]
    [InlineData(0.00015, 1.10215, 21.5, 232000.0, 498.8)]
    public void Sell_StopAndSizeIncludeTheSpread(double spread, double stop, double stopPips, double volume, double risk)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(spread: spread)));

        PlannedOrder order = h.OrderOf(ExecutionData.Sell());
        h.Orders.TryPlace(order);

        Assert.Equal(stop, order.StopLoss, 9);
        Assert.Equal(stopPips, order.StopPips, 9);
        Assert.Equal(volume, order.VolumeUnits);
        Assert.Equal(risk, order.RiskMoney, 6);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Equal(stop, request.Order!.StopLossPrice, 9);
        Assert.Equal(volume, request.Order.VolumeUnits);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-007")]
    [InlineData(0.0001)]
    [InlineData(0.00015)]
    public void Buy_StopAndSizeDoNotIncludeTheSpread(double spread)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(spread: spread)));

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        Assert.Equal(1.098, order.StopLoss, 9);
        Assert.Equal(20.0, order.StopPips, 9);
        Assert.Equal(250000.0, order.VolumeUnits);
        Assert.Equal(500.0, order.RiskMoney, 6);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-007")]
    public void Sell_TargetIsTheSignalsTarget()
    {
        var h = new ExecutionHarness();

        PlannedOrder order = h.OrderOf(ExecutionData.Sell());

        Assert.Equal(1.096, order.TakeProfit, 9);
        Assert.Equal(1.1, order.Entry, 9);
        ScenarioAssert.Global(h.Broker);
    }
}
