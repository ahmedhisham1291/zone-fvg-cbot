using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-018: a breakout signal is placed as a stop order through PlaceStopOrder, with stop loss,
/// take profit and expiry attached, label and comment as in EXE-008 with the breakout id; the
/// placement, read-back, protection, partial fill, Friday close and expiry rules of limit orders
/// apply. A ZoneFvg signal is still a limit order. The harness filters were built with default
/// breakout parameters, so the comment hash is 119a3864. Values: <see cref="ExecutionData"/>.
/// </summary>
public class Exe018StopOrderScenarios
{
    private const string BuyComment = "e=1.08640;s=1.08230;t=1.09430;r=496.10;h=119a3864";
    private static readonly DateTime Expiry = ExecutionData.At(14, 12, 0);

    private static ExecutionHarness BuyHarness()
    {
        return ExecutionData.BreakoutHarness(ExecutionData.BuyQuote());
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void BreakoutBuy_OneStopOrderRequestWithProtectionExpiryLabelAndComment()
    {
        ExecutionHarness h = BuyHarness();

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.BreakoutBuy());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        Assert.Equal("verified", result.Detail);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Null(request.Order);
        Assert.Equal(
            new StopOrderRequest("EURUSD", Side.Buy, 121000.0, 1.0864, 1.0823, 1.0943, Expiry, "ZFVG|v1|EURUSD|BH-20260112-2200", BuyComment),
            request.StopOrder);
        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(1.0864, order.EntryPrice);
        Assert.Equal(Expiry, order.ExpiryUtc);
        Assert.Equal(new CommentPlan(1.0864, 1.0823, 1.0943, 496.1, "119a3864"), OrderComment.TryParse(order.Comment));
        Assert.True(OrderLabel.TryParse(order.Label, out string symbol, out string id));
        Assert.Equal(("EURUSD", "BH-20260112-2200"), (symbol, id));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void BreakoutSell_OneSellStopWithTheSpreadOnTheStop()
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.SellQuote());

        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ExecutionData.BreakoutSell())!.Outcome);

        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Null(request.Order);
        Assert.Equal(
            new StopOrderRequest("EURUSD", Side.Sell, 121000.0, 1.0767, 1.0808, 1.0687, Expiry, "ZFVG|v1|EURUSD|BL-20260112-2200", "e=1.07670;s=1.08080;t=1.06870;r=496.10;h=119a3864"),
            request.StopOrder);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void ZoneFvgBuy_InTheSameHarness_IsStillOneLimitOrder()
    {
        ExecutionHarness h = BuyHarness();

        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ExecutionData.Buy())!.Outcome);

        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Null(request.StopOrder);
        Assert.Equal(
            new LimitOrderRequest("EURUSD", Side.Buy, 250000.0, 1.1, 1.098, 1.104, Expiry, "ZFVG|v1|EURUSD|D-20260114-0400", "e=1.10000;s=1.09800;t=1.10400;r=500.00;h=119a3864"),
            request.Order);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void BreakoutBuy_FillsWhenTheAskReachesTheEntryAndStopsOutOnTheBid()
    {
        ExecutionHarness h = BuyHarness();
        h.EvaluateAndPlace(ExecutionData.BreakoutBuy());

        h.Broker.Tick("EURUSD", 1.0862, 1.0863);
        Assert.Single(h.Broker.GetPendingOrders());
        Assert.Empty(h.Broker.GetPositions());

        h.Broker.Tick("EURUSD", 1.0863, 1.0864);
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(1.0864, position.EntryPrice);
        Assert.Equal(1.0823, position.StopLoss);
        Assert.Equal(1.0943, position.TakeProfit);
        Assert.Empty(h.Broker.GetPendingOrders());
        ScenarioAssert.Global(h.Broker);

        h.Broker.Tick("EURUSD", 1.0823, 1.0824);
        Assert.Empty(h.Broker.GetPositions());
        ClosedTradeInfo closed = Assert.Single(h.Broker.History);
        Assert.Equal(1.0823, closed.ClosingPrice);
        Assert.Equal(-496.1, closed.NetProfit, 6);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void BreakoutSell_FillsWhenTheBidReachesTheEntryAndStopsOutOnTheAsk()
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.SellQuote());
        h.EvaluateAndPlace(ExecutionData.BreakoutSell());

        h.Broker.Tick("EURUSD", 1.0768, 1.0769);
        Assert.Empty(h.Broker.GetPositions());

        h.Broker.Tick("EURUSD", 1.0767, 1.0768);
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(1.0767, position.EntryPrice);
        Assert.Equal(1.0808, position.StopLoss);

        h.Broker.Tick("EURUSD", 1.0807, 1.0808);
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(1.0808, Assert.Single(h.Broker.History).ClosingPrice);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void TryPlaceAgain_PendingOrFilled_SendsNothing()
    {
        ExecutionHarness h = BuyHarness();
        PlannedOrder order = h.OrderOf(ExecutionData.BreakoutBuy());

        Assert.Equal(PlaceOutcome.Placed, h.Orders.TryPlace(order).Outcome);
        Assert.Equal(PlaceOutcome.AlreadyExists, h.Orders.TryPlace(order).Outcome);
        h.Broker.Tick("EURUSD", 1.0863, 1.0864);
        Assert.Equal(PlaceOutcome.AlreadyExists, h.Orders.TryPlace(order).Outcome);

        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void Rejected_RecordedAlertedAndNeverRetried()
    {
        ExecutionHarness h = BuyHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.Rejected, "NotEnoughMoney");

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.BreakoutBuy());
        h.Broker.AdvanceTo(ExecutionData.At(14, 10, 15));
        IReadOnlyList<PlaceResult> resolved = h.Orders.ResolvePending(new[] { ExecutionData.M15(ExecutionData.Now, 1.086, 1.0861, 1.0855, 1.086) });

        Assert.Equal(PlaceOutcome.Rejected, result!.Outcome);
        Assert.Empty(resolved);
        Assert.Equal(SkipReason.OrderRejected, Assert.Single(h.Events.Skips).Reason);
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.OrderRejected));
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-018")]
    [InlineData(BrokerFault.DropStopLoss, "stop loss")]
    [InlineData(BrokerFault.DropTakeProfit, "take profit")]
    [InlineData(BrokerFault.DropExpiry, "expiry")]
    public void ProtectionDropped_CancelSentAndAlerted(BrokerFault fault, string what)
    {
        ExecutionHarness h = BuyHarness();
        h.Broker.Script(BrokerCall.Place, fault);

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.BreakoutBuy());

        Assert.Equal(PlaceOutcome.MismatchCancelled, result!.Outcome);
        Assert.StartsWith(what, result.Detail, StringComparison.Ordinal);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Broker.Count(BrokerCall.Cancel));
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.OrderMismatch));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void FilledAtOnce_IsPlacedAsAPosition()
    {
        ExecutionHarness h = BuyHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.FilledAtOnce);

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.BreakoutBuy());

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        Assert.StartsWith("filled as position", result.Detail, StringComparison.Ordinal);
        Assert.Equal(1.0823, Assert.Single(h.Broker.GetPositions()).StopLoss);
        Assert.Equal(0, h.Broker.Count(BrokerCall.Cancel));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void PartialFill_PositionKeptAndTheRemainderExpires()
    {
        // Half of 121,000 is 60,500, rounded down to the step: 60,000 fills; 61,000 stay pending.
        ExecutionHarness h = BuyHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.PartialFill, fraction: 0.5);
        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ExecutionData.BreakoutBuy())!.Outcome);

        h.Broker.Tick("EURUSD", 1.0863, 1.0864);

        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(60000.0, position.VolumeUnits);
        Assert.Equal(1.0823, position.StopLoss);
        Assert.Equal(61000.0, Assert.Single(h.Broker.GetPendingOrders()).VolumeUnits);
        Assert.Empty(h.Trades.EnsureProtection());

        h.Broker.AdvanceTo(Expiry);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Single(h.Broker.GetPositions());
        Assert.Equal(0, h.Broker.Count(BrokerCall.Cancel));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void SuccessNotApplied_NotFoundAlertedAndNotResent()
    {
        ExecutionHarness h = BuyHarness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.SuccessNotApplied);

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.BreakoutBuy());

        Assert.Equal(PlaceOutcome.NotFound, result!.Outcome);
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.OrderNotFound));
        Assert.Equal(0, h.Orders.UnresolvedCount);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void CancelEntryOrders_CancelsAPendingStopOrder()
    {
        ExecutionHarness h = BuyHarness();
        PlaceResult? placed = h.EvaluateAndPlace(ExecutionData.BreakoutBuy());

        TradeAction action = Assert.Single(h.Trades.CancelEntryOrders("daily budget"));

        Assert.Equal(new TradeAction(placed!.OrderId!.Value, TradeActionOutcome.Cancelled, "daily budget"), action);
        Assert.Empty(h.Broker.GetPendingOrders());
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void FridayClose_CancelsAPendingStopOrder()
    {
        // EntryExpiryBars 40: placed on Friday 11:45, the order would expire at 21:45.
        DateTime placedAt = ExecutionData.At(16, 11, 45);
        var h = new ExecutionHarness(
            ExecutionHarness.DefaultBroker(ExecutionData.BuyQuote(), placedAt),
            execution: new ExecutionParameters { EntryExpiryBars = 40 },
            breakout: new BreakoutParameters());
        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ExecutionData.BreakoutBuy(closeUtc: placedAt))!.Outcome);
        Assert.Equal(ExecutionData.At(16, 21, 45), Assert.Single(h.Broker.GetPendingOrders()).ExpiryUtc);
        h.Broker.AdvanceTo(ExecutionData.At(16, 20, 0));

        IReadOnlyList<TradeAction> actions = h.Trades.FridayClose(ExecutionData.At(16, 20, 0));

        Assert.Equal(TradeActionOutcome.Cancelled, Assert.Single(actions).Outcome);
        Assert.Empty(h.Broker.GetPendingOrders());
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void EnsureProtection_RestoresTheStopOfAFilledBreakoutPositionFromItsComment()
    {
        ExecutionHarness h = BuyHarness();
        h.EvaluateAndPlace(ExecutionData.BreakoutBuy());
        h.Broker.Tick("EURUSD", 1.0863, 1.0864);
        long id = Assert.Single(h.Broker.GetPositions()).Id;
        h.Broker.ModifyPositionProtection(id, null, 1.0943);
        Assert.Null(Assert.Single(h.Broker.GetPositions()).StopLoss);

        TradeAction action = Assert.Single(h.Trades.EnsureProtection());

        Assert.Equal(new TradeAction(id, TradeActionOutcome.StopRestored, "stop 1.0823"), action);
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(1.0823, position.StopLoss);
        Assert.Equal(1.0943, position.TakeProfit);
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.StopLossRestored));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void StopOrder_CountsAsOneRequest()
    {
        ExecutionHarness h = BuyHarness();

        h.EvaluateAndPlace(ExecutionData.BreakoutBuy());

        Assert.Equal(1, h.Counter.Evaluate(ExecutionData.Now).LastHourCount);
        Assert.Equal(1, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void StopOrder_HourLimitReached_IsNotSent()
    {
        ExecutionHarness h = BuyHarness();
        for (int i = 0; i < 20; i++)
        {
            h.Counter.Record(ExecutionData.Now.AddMinutes(-i));
        }

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.BreakoutBuy());

        Assert.Equal(PlaceOutcome.RequestLimitReached, result!.Outcome);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(SkipReason.RequestLimitReached, Assert.Single(h.Events.Skips).Reason);
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.RequestLimitReached));
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void StopOrder_ExpiryShortenedToTheNextNewsWindow_IsSentWithThatExpiry()
    {
        // Event at 11:00: the window starts at 10:30 (EXE-015).
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.BuyQuote(), "2026-01-14 11:00,USD,High,Event\n");

        h.EvaluateAndPlace(ExecutionData.BreakoutBuy());

        Assert.Equal(ExecutionData.At(14, 10, 30), Assert.Single(h.Broker.Requests).StopOrder!.ExpiryUtc);
        h.Broker.AdvanceTo(ExecutionData.At(14, 10, 30));
        Assert.Empty(h.Broker.GetPendingOrders());
    }
}
