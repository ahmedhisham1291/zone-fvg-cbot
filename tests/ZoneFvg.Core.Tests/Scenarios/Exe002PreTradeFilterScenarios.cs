using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-002: an order is placed only inside SessionStartUtc to SessionEndUtc (07:00 included,
/// 17:00 excluded), on Fridays before FridayNoEntryUtc (12:00), with spread at most
/// MaxSpreadPips (1.5), with the market open, with no bot position or pending order on the
/// symbol, and in state Running. Each filter alone blocks the reference buy, and passes at its
/// boundary. 2026-01-15 is a Thursday, 2026-01-16 a Friday.
/// </summary>
public class Exe002PreTradeFilterScenarios
{
    private static ExecutionHarness At(DateTime now)
    {
        return new ExecutionHarness(ExecutionHarness.DefaultBroker(now: now));
    }

    // ------------------------------------------------------------------ session

    [Fact]
    [Trait("Req", "EXE-002")]
    public void BeforeSessionStart_IsSkipped()
    {
        ExecutionHarness h = At(ExecutionData.At(14, 6, 45));

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(closeUtc: ExecutionData.At(14, 6, 45)));

        Assert.Equal(SkipReason.OutsideSession, skip.Reason);
        Assert.Equal(6.75, skip.Measured, 9);
        Assert.Equal(7.0, skip.Limit, 9);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void AtSessionStart_IsPlaced()
    {
        ExecutionHarness h = At(ExecutionData.At(14, 7, 0));

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy(closeUtc: ExecutionData.At(14, 7, 0)));

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void AtSessionEnd_IsSkipped()
    {
        ExecutionHarness h = At(ExecutionData.At(14, 17, 0));

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(closeUtc: ExecutionData.At(14, 17, 0)));

        Assert.Equal(SkipReason.OutsideSession, skip.Reason);
        Assert.Equal(17.0, skip.Measured, 9);
        Assert.Equal(17.0, skip.Limit, 9);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void OneTickBeforeSessionEnd_IsPlaced()
    {
        // The 16:45 signal evaluated at 16:59:59.9999999. Expiry: the news window at 17:30 comes
        // before 16:59:59.9999999 + 2 hours, so the order expires at 17:30.
        ExecutionHarness h = At(ExecutionData.At(14, 17, 0).AddTicks(-1));

        PlannedOrder order = h.OrderOf(ExecutionData.Buy(closeUtc: ExecutionData.At(14, 16, 45)));

        Assert.Equal(ExecutionData.At(14, 17, 30), order.ExpiryUtc);
        Assert.Equal(PlaceOutcome.Placed, h.Orders.TryPlace(order).Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ Friday

    [Fact]
    [Trait("Req", "EXE-002")]
    public void FridayAtNoEntryTime_IsSkipped()
    {
        ExecutionHarness h = At(ExecutionData.At(16, 12, 0));

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(closeUtc: ExecutionData.At(16, 12, 0)));

        Assert.Equal(SkipReason.FridayNoEntry, skip.Reason);
        Assert.Equal(12.0, skip.Measured, 9);
        Assert.Equal(12.0, skip.Limit, 9);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void FridayOneTickBeforeNoEntryTime_IsPlaced()
    {
        ExecutionHarness h = At(ExecutionData.At(16, 12, 0).AddTicks(-1));

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy(closeUtc: ExecutionData.At(16, 11, 45)));

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void ThursdayAtNoon_IsPlaced()
    {
        ExecutionHarness h = At(ExecutionData.At(15, 12, 0));

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy(closeUtc: ExecutionData.At(15, 12, 0)));

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ spread and market

    [Theory]
    [Trait("Req", "EXE-002")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void SpreadAboveMaximum_IsSkipped(Side side)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(spread: 0.00016)));

        SkipRecord skip = h.SkipOf(side == Side.Buy ? ExecutionData.Buy() : ExecutionData.Sell());

        Assert.Equal(SkipReason.SpreadTooWide, skip.Reason);
        Assert.Equal(1.6, skip.Measured, 9);
        Assert.Equal(1.5, skip.Limit);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-002")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void SpreadAtMaximum_IsPlaced(Side side)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(spread: 0.00015)));

        PlaceResult? result = h.EvaluateAndPlace(side == Side.Buy ? ExecutionData.Buy() : ExecutionData.Sell());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void MarketClosed_IsSkipped()
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(marketOpen: false)));

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.MarketClosed, skip.Reason);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ symbol busy

    [Fact]
    [Trait("Req", "EXE-002")]
    public void BotPositionOnTheSymbol_IsSkipped()
    {
        var h = new ExecutionHarness();
        h.Broker.AddPosition("EURUSD", Side.Buy, 100000.0, 1.105, 1.103, 1.109, ExecutionData.Label("D-20260113-0800"), "c");

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.SymbolBusy, skip.Reason);
        Assert.Equal(1.0, skip.Measured);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void BotPendingOrderOnTheSymbol_IsSkipped()
    {
        var h = new ExecutionHarness();
        h.Broker.AddOrder("EURUSD", Side.Sell, 100000.0, 1.105, 1.107, 1.101, ExecutionData.At(14, 11, 0), ExecutionData.Label("S-20260113-0800"), "c");

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.SymbolBusy, skip.Reason);
        Assert.Equal(1.0, skip.Measured);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void ManualPositionOnTheSymbolAndBotPositionOnAnotherSymbol_DoNotBlock()
    {
        var h = new ExecutionHarness();
        h.Broker.SetSymbol(RiskData.GbpUsd);
        h.Broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.105, 1.103, 1.109, "manual", "c");
        h.Broker.AddPosition("GBPUSD", Side.Buy, 1000.0, 1.25, 1.248, 1.254, "ZFVG|v1|GBPUSD|D-20260113-0800", "c");

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void UnresolvedPlacementOnTheSymbol_IsSkipped()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied);
        Assert.Equal(PlaceOutcome.Unclear, h.EvaluateAndPlace(ExecutionData.Buy())!.Outcome);

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(zoneId: "D-20260114-0800"));

        Assert.Equal(SkipReason.SymbolBusy, skip.Reason);
        Assert.Equal(0.0, skip.Measured);
        Assert.Contains("unresolved", skip.Detail, StringComparison.Ordinal);
        Assert.Equal(1, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-002")]
    public void TwoSignalsOnOneBar_SmallerZoneIdFirst_SecondIsSymbolBusy()
    {
        var h = new ExecutionHarness();
        Signal later = ExecutionData.Buy(zoneId: "D-20260114-0800");
        Signal earlier = ExecutionData.Buy(zoneId: "D-20260114-0400");

        IReadOnlyList<Signal> ordered = SignalOrder.ForEvaluation(new[] { later, earlier });

        Assert.Equal(new[] { earlier, later }, ordered);
        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ordered[0])!.Outcome);
        Assert.Equal(SkipReason.SymbolBusy, h.SkipOf(ordered[1]).Reason);
        Assert.Equal(ExecutionData.Label("D-20260114-0400"), Assert.Single(h.Broker.GetPendingOrders()).Label);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ state

    [Theory]
    [Trait("Req", "EXE-002")]
    [InlineData(BotState.Recovering)]
    [InlineData(BotState.Blocked)]
    [InlineData(BotState.Halted)]
    public void StateNotRunning_IsSkippedBeforeAnyBrokerQuery(BotState state)
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(), state);

        Assert.Equal(SkipReason.BotNotRunning, skip.Reason);
        Assert.Equal((double)state, skip.Measured);
        Assert.Equal((double)BotState.Running, skip.Limit);
        Assert.Equal(0, h.Broker.QueryCount);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }
}
