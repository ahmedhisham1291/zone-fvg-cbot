using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-009: before sending, the broker is queried for the label and nothing is sent if it exists;
/// a clear rejection is never retried; an unclear outcome is never resent blindly. At the next bar
/// the broker is queried again and the order is resent only if nothing exists, the signal is still
/// fresh (at most 15 minutes old) and no bar since the confirmation traded through the entry.
/// Reference buy: entry 1.10000, confirmed at 10:00; the next bar opens at 10:00 and closes at 10:15.
/// </summary>
public class Exe009IdempotentPlacementScenarios
{
    private static readonly DateTime NextClose = ExecutionData.At(14, 10, 15);

    private static Bar NextBar(double high, double low)
    {
        // Open and close in the middle of the range, so any high and low form a valid bar.
        double middle = (high + low) / 2.0;
        return ExecutionData.M15(ExecutionData.Now, middle, high, low, middle);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void ExistingOrderWithTheLabel_MeansZeroRequests()
    {
        var h = new ExecutionHarness();
        PlannedOrder order = h.OrderOf(ExecutionData.Buy());
        h.Broker.AddOrder("EURUSD", Side.Buy, 250000.0, 1.1, 1.098, 1.104, ExecutionData.At(14, 12, 0), order.Label, order.Comment);

        PlaceResult result = h.Orders.TryPlace(order);

        Assert.Equal(PlaceOutcome.AlreadyExists, result.Outcome);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void ExistingPositionWithTheLabel_MeansZeroRequests()
    {
        var h = new ExecutionHarness();
        PlannedOrder order = h.OrderOf(ExecutionData.Buy());
        h.Broker.AddPosition("EURUSD", Side.Buy, 250000.0, 1.1, 1.098, 1.104, order.Label, order.Comment);

        Assert.Equal(PlaceOutcome.AlreadyExists, h.Orders.TryPlace(order).Outcome);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void ClearRejection_OneRequestRecordedAlertedAndNeverRetried()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.Rejected, "NotEnoughMoney");

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());
        h.Broker.AdvanceTo(NextClose);
        IReadOnlyList<PlaceResult> resolved = h.Orders.ResolvePending(new[] { NextBar(1.1008, 1.1002) });

        Assert.Equal(PlaceOutcome.Rejected, result!.Outcome);
        Assert.Equal("NotEnoughMoney", result.Detail);
        Assert.Empty(resolved);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        SkipRecord skip = Assert.Single(h.Events.Skips);
        Assert.Equal(SkipReason.OrderRejected, skip.Reason);
        Assert.Contains("NotEnoughMoney", skip.Detail, StringComparison.Ordinal);
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.OrderRejected));
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(0, h.Orders.UnresolvedCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void UnclearButAccepted_ExactlyOneOrderAfterResolvePending()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearApplied, "Timeout");
        PlannedOrder order = h.OrderOf(ExecutionData.Buy());

        PlaceResult first = h.Orders.TryPlace(order);
        PlaceResult again = h.Orders.TryPlace(order);
        h.Broker.AdvanceTo(NextClose);
        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.1008, 1.1002) }));

        Assert.Equal(PlaceOutcome.Unclear, first.Outcome);
        Assert.Equal(PlaceOutcome.AwaitingResolution, again.Outcome);
        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Equal(0, h.Orders.UnresolvedCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-009")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void UnclearNotAccepted_StillFreshAndNotTradedThrough_IsResentOnce(Side side)
    {
        // The next bar stays 0.1 pip short of the entry: low 1.10001 (buy), high 1.09999 (sell).
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        Signal signal = side == Side.Buy ? ExecutionData.Buy() : ExecutionData.Sell();
        Bar bar = side == Side.Buy ? NextBar(1.1008, 1.10001) : NextBar(1.09999, 1.0992);

        Assert.Equal(PlaceOutcome.Unclear, h.EvaluateAndPlace(signal)!.Outcome);
        h.Broker.AdvanceTo(NextClose);
        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { bar }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(2, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-009")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void UnclearNotAccepted_PriceTradedThroughTheEntry_IsNotResent(Side side)
    {
        // The next bar touches the entry exactly: low 1.10000 (buy), high 1.10000 (sell).
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        Signal signal = side == Side.Buy ? ExecutionData.Buy() : ExecutionData.Sell();
        Bar bar = side == Side.Buy ? NextBar(1.1008, 1.1) : NextBar(1.1, 1.0992);

        h.EvaluateAndPlace(signal);
        h.Broker.AdvanceTo(NextClose);
        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { bar }));

        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Contains("traded through", resolved.Detail, StringComparison.Ordinal);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Equal(SkipReason.UnclearOutcomeNotResent, Assert.Single(h.Events.Skips).Reason);
        Assert.Equal(0, h.Orders.UnresolvedCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void UnclearNotAccepted_NoLongerFresh_IsNotResent()
    {
        // First resolution at 10:30: the signal is 30 minutes old.
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied);
        h.EvaluateAndPlace(ExecutionData.Buy());
        h.Broker.AdvanceTo(ExecutionData.At(14, 10, 30));

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.1008, 1.1002), ExecutionData.M15(NextClose, 1.1005, 1.1008, 1.1002, 1.1005) }));

        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Contains("no longer fresh", resolved.Detail, StringComparison.Ordinal);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void UnclearNotAccepted_NoBarSinceTheConfirmation_IsNotResent()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied);
        h.EvaluateAndPlace(ExecutionData.Buy());
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(Array.Empty<Bar>()));

        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Contains("price path unknown", resolved.Detail, StringComparison.Ordinal);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void UnclearNotAccepted_BeforeTheNextBarCloses_IsKeptWithoutARequest()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied);
        h.EvaluateAndPlace(ExecutionData.Buy());
        h.Broker.AdvanceTo(NextClose.AddTicks(-1));

        Assert.Empty(h.Orders.ResolvePending(new[] { NextBar(1.1008, 1.1002) }));

        Assert.Equal(1, h.Orders.UnresolvedCount);
        Assert.True(h.Orders.HasUnresolved("EURUSD"));
        Assert.False(h.Orders.HasUnresolved("GBPUSD"));
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    [Trait("Req", "EXE-015")]
    public void UnclearNotAccepted_LessThan15MinutesOfOrderLifeLeft_IsNotResent()
    {
        // A USD event at 10:50 opens a window at 10:20: the order expires at 10:20, 20 minutes after
        // placement. At the next close, 10:15, only 5 minutes are left.
        var h = new ExecutionHarness(news: "2026-01-14 10:50,USD,High,Speech\n");
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied);
        PlannedOrder order = h.OrderOf(ExecutionData.Buy());
        Assert.Equal(ExecutionData.At(14, 10, 20), order.ExpiryUtc);
        h.Orders.TryPlace(order);
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.1008, 1.1002) }));

        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Contains("15 minutes", resolved.Detail, StringComparison.Ordinal);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void UnclearThenFilledBeforeTheNextBar_IsResolvedAsPlacedWithoutARequest()
    {
        var h = new ExecutionHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearApplied);
        h.EvaluateAndPlace(ExecutionData.Buy());
        h.Broker.Tick("EURUSD", 1.0999, 1.1);
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.1008, 1.0999) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Single(h.Broker.GetPositions());
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void RequestLimitReached_EntryIsNotSentAndAlerted()
    {
        // 20 requests already in the last hour: a 21st would exceed MaxRequestsPerHour (RSK-010).
        var h = new ExecutionHarness();
        for (int i = 0; i < 20; i++)
        {
            h.Counter.Record(ExecutionData.Now.AddMinutes(-i));
        }

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy());

        Assert.Equal(PlaceOutcome.RequestLimitReached, result!.Outcome);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(SkipReason.RequestLimitReached, Assert.Single(h.Events.Skips).Reason);
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.RequestLimitReached));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void NineteenRequestsInTheLastHour_EntryIsSentAndCounted()
    {
        var h = new ExecutionHarness();
        for (int i = 0; i < 19; i++)
        {
            h.Counter.Record(ExecutionData.Now.AddMinutes(-i));
        }

        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ExecutionData.Buy())!.Outcome);
        Assert.Equal(20, h.Counter.Evaluate(ExecutionData.Now).LastHourCount);
        ScenarioAssert.Global(h.Broker);
    }
}
