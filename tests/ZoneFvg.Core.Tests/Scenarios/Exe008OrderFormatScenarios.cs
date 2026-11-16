using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-008: entries are limit orders with stop loss, take profit and expiry attached at
/// placement; label ZFVG|v1|EURUSD|&lt;zoneId&gt;; the comment encodes entry, stop, target, risk
/// money and the parameter hash (f681dff2 for the default parameters) and parses back to the plan.
/// </summary>
public class Exe008OrderFormatScenarios
{
    [Fact]
    [Trait("Req", "EXE-008")]
    public void Buy_PlacedOrderCarriesProtectionExpiryLabelAndAParseableComment()
    {
        var h = new ExecutionHarness();

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        LimitOrderRequest request = Assert.Single(h.Broker.Requests).Order!;
        Assert.Equal(new LimitOrderRequest(
            "EURUSD", Side.Buy, 250000.0, 1.1, 1.098, 1.104, ExecutionData.At(14, 12, 0),
            "ZFVG|v1|EURUSD|D-20260114-0400", "e=1.10000;s=1.09800;t=1.10400;r=500.00;h=f681dff2"), request);

        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(1.098, order.StopLoss);
        Assert.Equal(1.104, order.TakeProfit);
        Assert.Equal(ExecutionData.At(14, 12, 0), order.ExpiryUtc);
        Assert.Equal(new CommentPlan(1.1, 1.098, 1.104, 500.0, "f681dff2"), OrderComment.TryParse(order.Comment));
        Assert.True(OrderLabel.TryParse(order.Label, out string symbol, out string zoneId));
        Assert.Equal(("EURUSD", "D-20260114-0400"), (symbol, zoneId));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void Sell_CommentCarriesTheSpreadWidenedStopAndTheRoundedRisk()
    {
        var h = new ExecutionHarness();

        h.EvaluateAndPlace(ExecutionData.Sell());

        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal("ZFVG|v1|EURUSD|S-20260114-0400", order.Label);
        Assert.Equal("e=1.10000;s=1.10210;t=1.09600;r=499.80;h=f681dff2", order.Comment);
        Assert.Equal(238000.0, order.VolumeUnits);
        Assert.Equal(Side.Sell, order.Side);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void OtherParameters_WriteAnotherHash()
    {
        var execution = new ExecutionParameters { MinRoomR = 2.0 };
        var h = new ExecutionHarness(execution: execution);

        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        string expected = ParameterHash.Compute(ExecutionData.SignalDefaults, execution);
        Assert.NotEqual("f681dff2", expected);
        Assert.Equal(expected, h.Filters.ParameterHashValue);
        Assert.Equal(expected, OrderComment.TryParse(order.Comment)!.ParameterHash);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void SignalLevelsOffTheGrid_AreRoundedToTheSymbolsDigits()
    {
        // Entry 1.100004 rounds to 1.10000, stop 1.097996 to 1.09800, target 1.1040049 to 1.10400.
        var h = new ExecutionHarness();

        PlannedOrder order = h.OrderOf(ExecutionData.Buy(entry: 1.100004, stop: 1.097996, target: 1.1040049));

        Assert.Equal(1.1, order.Entry, 12);
        Assert.Equal(1.098, order.StopLoss, 12);
        Assert.Equal(1.104, order.TakeProfit, 12);
        Assert.Equal(5, order.PriceDigits);
        Assert.Equal("e=1.10000;s=1.09800;t=1.10400;r=500.00;h=f681dff2", order.Comment);
        ScenarioAssert.Global(h.Broker);
    }
}
