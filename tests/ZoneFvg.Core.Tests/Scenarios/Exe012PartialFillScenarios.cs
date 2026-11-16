using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-012: a partial fill is managed as a normal position and the unfilled remainder is left to
/// expire. The reference buy of 250,000 fills 40%: a position of 100,000 with stop 1.09800 and
/// target 1.10400, and a pending remainder of 150,000 that expires at 12:00. At the target the
/// position books 40 pips x 0.0001 x 100,000 = 400 USD.
/// </summary>
public class Exe012PartialFillScenarios
{
    [Fact]
    [Trait("Req", "EXE-012")]
    public void FortyPercentPartialFill_IsProtectedAndManagedAndTheRemainderExpires()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.PartialFill, fraction: 0.4);
        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ExecutionData.Buy())!.Outcome);

        h.Broker.Tick("EURUSD", 1.0999, 1.1);

        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(100000.0, position.VolumeUnits);
        Assert.Equal(1.098, position.StopLoss);
        Assert.Equal(1.104, position.TakeProfit);
        Assert.Equal(150000.0, Assert.Single(h.Broker.GetPendingOrders()).VolumeUnits);
        Assert.Empty(h.Trades.EnsureProtection());
        Assert.Equal(SkipReason.SymbolBusy, h.SkipOf(ExecutionData.Buy(zoneId: "D-20260114-0800")).Reason);
        ScenarioAssert.Global(h.Broker);

        h.Broker.AdvanceTo(ExecutionData.At(14, 12, 0));
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Single(h.Broker.GetPositions());
        Assert.Equal(0, h.Broker.Count(BrokerCall.Cancel));

        h.Broker.Tick("EURUSD", 1.104, 1.1041);
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(400.0, Assert.Single(h.Broker.History).NetProfit, 6);
        Assert.Equal(1, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-012")]
    [Trait("Req", "EXE-013")]
    public void PartialFillOnFriday_RemainderCancelledThenPositionClosedAt2000()
    {
        // EntryExpiryBars 40: placed on Friday 11:45, the order would expire at 21:45. 40% fills; at
        // 20:00 the remainder is still pending, so it is cancelled first and then the position closed.
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(now: ExecutionData.At(16, 11, 45)), execution: new ExecutionParameters { EntryExpiryBars = 40 });
        h.Broker.Script(BrokerCall.Place, BrokerFault.PartialFill, fraction: 0.4);
        h.EvaluateAndPlace(ExecutionData.Buy(closeUtc: ExecutionData.At(16, 11, 45)));
        h.Broker.Tick("EURUSD", 1.0999, 1.1);
        h.Broker.AdvanceTo(ExecutionData.At(16, 20, 0));
        Assert.Single(h.Broker.GetPendingOrders());

        IReadOnlyList<TradeAction> actions = h.Trades.FridayClose(ExecutionData.At(16, 20, 0));

        Assert.Equal(new[] { TradeActionOutcome.Cancelled, TradeActionOutcome.Closed }, actions.Select(a => a.Outcome));
        Assert.Empty(h.Broker.GetPositions());
        Assert.Empty(h.Broker.GetPendingOrders());
        ScenarioAssert.Global(h.Broker);
    }
}
