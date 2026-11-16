using Xunit;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-010: after a successful placement the order is read back and its stop, target and expiry
/// are compared with the request; a mismatch cancels the order and raises an alert.
/// </summary>
public class Exe010ReadBackScenarios
{
    [Theory]
    [Trait("Req", "EXE-010")]
    [InlineData(BrokerFault.DropStopLoss, "stop loss")]
    [InlineData(BrokerFault.DropTakeProfit, "take profit")]
    [InlineData(BrokerFault.DropExpiry, "expiry")]
    public void ProtectionDroppedByTheBroker_OrderCancelledAndAlerted(BrokerFault fault, string what)
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, fault);

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.MismatchCancelled, result!.Outcome);
        Assert.StartsWith(what, result.Detail, StringComparison.Ordinal);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Equal(1, h.Broker.Count(BrokerCall.Cancel));
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.OrderMismatch));
        Assert.Equal(0, h.Events.AlertsOf(AlertKind.CancelFailed));
        Assert.Equal(2, h.Counter.Evaluate(ExecutionData.Now).LastHourCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-010")]
    public void MismatchAndTheCancelFails_BothAlerted()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.DropStopLoss);
        h.Broker.Script(BrokerCall.Cancel, BrokerFault.Rejected, "TradingDisabled");

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.MismatchCancelled, result!.Outcome);
        Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.OrderMismatch));
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.CancelFailed));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-010")]
    public void UnclearButAccepted_IsReadBackAndVerifiedWhenResolved()
    {
        // The broker took the order but answered Unclear. At the next bar the order is found by its
        // label and checked like after a success.
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearApplied);
        h.EvaluateAndPlace(ExecutionData.Buy());
        long id = Assert.Single(h.Broker.GetPendingOrders()).Id;
        h.Broker.AdvanceTo(ExecutionData.At(14, 10, 15));

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { ExecutionData.M15(ExecutionData.Now, 1.1005, 1.1008, 1.1002, 1.1005) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal("verified", resolved.Detail);
        Assert.Equal(id, resolved.OrderId);
        Assert.Equal(1, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-010")]
    public void SuccessButNoOrderAtTheBroker_AlertedAndNotResent()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.SuccessNotApplied);

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.NotFound, result!.Outcome);
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.OrderNotFound));
        Assert.Equal(0, h.Orders.UnresolvedCount);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-010")]
    public void FilledAtOnce_IsPlacedWithoutACancel()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.FilledAtOnce);

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        Assert.Single(h.Broker.GetPositions());
        Assert.Equal(0, h.Broker.Count(BrokerCall.Cancel));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-010")]
    public void MatchingReadBack_RaisesNoAlert()
    {
        var h = new ExecutionHarness();

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        Assert.Equal("verified", result.Detail);
        Assert.Equal(1001L, result.OrderId);
        Assert.Empty(h.Events.Alerts);
        ScenarioAssert.Global(h.Broker);
    }
}
