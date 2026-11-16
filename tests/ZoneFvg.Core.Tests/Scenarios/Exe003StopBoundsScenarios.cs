using Xunit;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-003: a signal is skipped if its stop distance is below MinStopPips (12) or above
/// MaxStopAtr (2.0) x the ATR(H4) carried by the signal (0.00200, so 40 pips). Entry 1.10000;
/// the sell stop includes the 1-pip spread (EXE-007), so a sell signal stop of 1.10110 is an
/// order stop of 1.10120, 12 pips.
/// </summary>
public class Exe003StopBoundsScenarios
{
    [Fact]
    [Trait("Req", "EXE-003")]
    public void Buy_Stop12Pips_IsPlaced()
    {
        // Volume 500 / (12 x 0.0001) = 416,666.7, rounded down to 416,000.
        var h = new ExecutionHarness();

        PlannedOrder order = h.OrderOf(ExecutionData.Buy(stop: 1.0988, target: 1.1024));

        Assert.Equal(12.0, order.StopPips, 9);
        Assert.Equal(416000.0, order.VolumeUnits);
        Assert.Equal(PlaceOutcome.Placed, h.Orders.TryPlace(order).Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-003")]
    public void Buy_Stop11Point9Pips_IsSkipped()
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(stop: 1.09881, target: 1.1024));

        Assert.Equal(SkipReason.StopTooSmall, skip.Reason);
        Assert.Equal(11.9, skip.Measured, 9);
        Assert.Equal(12.0, skip.Limit);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-003")]
    public void Sell_Stop12PipsWithSpread_IsPlaced()
    {
        var h = new ExecutionHarness();

        PlannedOrder order = h.OrderOf(ExecutionData.Sell(stop: 1.1011, target: 1.0976));

        Assert.Equal(1.1012, order.StopLoss, 9);
        Assert.Equal(12.0, order.StopPips, 9);
        Assert.Equal(PlaceOutcome.Placed, h.Orders.TryPlace(order).Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-003")]
    public void Sell_Stop11Point9PipsWithSpread_IsSkipped()
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.Sell(stop: 1.10109, target: 1.0976));

        Assert.Equal(SkipReason.StopTooSmall, skip.Reason);
        Assert.Equal(11.9, skip.Measured, 9);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-003")]
    public void Buy_StopExactly2Atr_IsPlaced()
    {
        // 2.0 x 0.00200 = 0.00400 = 40 pips; volume 500 / (40 x 0.0001) = 125,000.
        var h = new ExecutionHarness();

        PlannedOrder order = h.OrderOf(ExecutionData.Buy(stop: 1.096, target: 1.108));

        Assert.Equal(40.0, order.StopPips, 9);
        Assert.Equal(125000.0, order.VolumeUnits);
        Assert.Equal(PlaceOutcome.Placed, h.Orders.TryPlace(order).Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-003")]
    public void Buy_StopAbove2Atr_IsSkipped()
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(stop: 1.09599, target: 1.108));

        Assert.Equal(SkipReason.StopTooLarge, skip.Reason);
        Assert.Equal(40.1, skip.Measured, 9);
        Assert.Equal(40.0, skip.Limit, 9);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-003")]
    public void Sell_StopExactly2AtrWithSpread_IsPlacedAndOnePipMoreIsSkipped()
    {
        // Signal stop 1.10390 + spread 0.00010 = 1.10400: 40 pips. 1.10391 gives 40.1 pips.
        var h = new ExecutionHarness();

        PlannedOrder order = h.OrderOf(ExecutionData.Sell(stop: 1.1039, target: 1.092));
        SkipRecord skip = h.SkipOf(ExecutionData.Sell(stop: 1.10391, target: 1.092));

        Assert.Equal(40.0, order.StopPips, 9);
        Assert.Equal(SkipReason.StopTooLarge, skip.Reason);
        Assert.Equal(40.1, skip.Measured, 9);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-003")]
    [InlineData(0.0015, 30.0)] // 2.0 x 0.00150 = 30 pips: the 40-pip stop is too large
    [InlineData(0.0, 0.0)] // no ATR: every stop is too large (fail closed)
    public void AtrH4CarriedByTheSignal_SetsTheUpperBound(double atrH4, double limitPips)
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(stop: 1.096, target: 1.108, atrH4: atrH4));

        Assert.Equal(SkipReason.StopTooLarge, skip.Reason);
        Assert.Equal(40.0, skip.Measured, 9);
        Assert.Equal(limitPips, skip.Limit, 9);
        ScenarioAssert.Global(h.Broker);
    }
}
