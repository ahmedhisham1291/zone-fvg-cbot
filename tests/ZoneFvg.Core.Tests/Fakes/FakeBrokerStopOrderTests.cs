using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Stop orders in the fake broker and the port (EXE-018). A buy stop fills when the ask is at or
/// above its entry and a sell stop when the bid is at or below it, at the entry price; the rest
/// (faults, ids, position, stop, target, partial fill, expiry) is the limit order model. The buy
/// stop: 121,000 units at 1.08640, stop 1.08230, target 1.09430, expiring at 12:00; the sell stop:
/// 121,000 units at 1.07670, stop 1.08080, target 1.06870.
/// </summary>
public class FakeBrokerStopOrderTests
{
    private static readonly DateTime Now = ExecutionData.Now;
    private static readonly DateTime Expiry = ExecutionData.At(14, 12, 0);

    private static StopOrderRequest Request(Side side = Side.Buy)
    {
        return side == Side.Buy
            ? new StopOrderRequest("EURUSD", Side.Buy, 121000.0, 1.0864, 1.0823, 1.0943, Expiry, ExecutionData.Label("BH-20260112-2200"), "c")
            : new StopOrderRequest("EURUSD", Side.Sell, 121000.0, 1.0767, 1.0808, 1.0687, Expiry, ExecutionData.Label("BL-20260112-2200"), "c");
    }

    private static FakeBroker Broker()
    {
        return new FakeBroker(Now, ExecutionData.Account(), ExecutionData.EurUsd());
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void PlaceStopOrder_Success_OrderListedAndRequestRecordedAsAStopOrder()
    {
        FakeBroker broker = Broker();

        OrderResult result = broker.PlaceStopOrder(Request());

        Assert.Equal(new OrderResult(OrderOutcome.Success, 1001L, null), result);
        PendingOrderInfo order = Assert.Single(broker.GetPendingOrders());
        Assert.Equal(1001L, order.Id);
        Assert.Equal(1.0864, order.EntryPrice);
        Assert.Equal(1.0823, order.StopLoss);
        Assert.Equal(1.0943, order.TakeProfit);
        Assert.Equal(Expiry, order.ExpiryUtc);
        Assert.Equal(ExecutionData.Label("BH-20260112-2200"), order.Label);
        BrokerRequest request = Assert.Single(broker.Requests);
        Assert.Equal(BrokerCall.Place, request.Call);
        Assert.Equal(Request(), request.StopOrder);
        Assert.Null(request.Order);
        Assert.Equal(1.0823, request.StopLoss);
        Assert.Equal(1.0943, request.TakeProfit);
        Assert.Equal(OrderOutcome.Success, request.Outcome);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void PlaceLimitOrder_RequestHasNoStopOrder()
    {
        FakeBroker broker = Broker();

        broker.PlaceLimitOrder(new LimitOrderRequest("EURUSD", Side.Buy, 1000.0, 1.08, 1.07, 1.10, Expiry, ExecutionData.Label("D-1"), "c"));

        BrokerRequest request = Assert.Single(broker.Requests);
        Assert.NotNull(request.Order);
        Assert.Null(request.StopOrder);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void BuyStop_AskBelowTheEntryLeavesItPending_AskAtTheEntryFillsAtTheEntry()
    {
        FakeBroker broker = Broker();
        broker.PlaceStopOrder(Request());

        // A buy limit would fill here; a buy stop must not.
        broker.Tick("EURUSD", 1.0862, 1.0863);
        Assert.Single(broker.GetPendingOrders());
        Assert.Empty(broker.GetPositions());

        broker.Tick("EURUSD", 1.0863, 1.0864);
        Assert.Empty(broker.GetPendingOrders());
        PositionInfo position = Assert.Single(broker.GetPositions());
        Assert.Equal(Side.Buy, position.Side);
        Assert.Equal(1.0864, position.EntryPrice);
        Assert.Equal(1.0823, position.StopLoss);
        Assert.Equal(1.0943, position.TakeProfit);
        Assert.Equal(121000.0, position.VolumeUnits);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void BuyStop_AskJumpsAboveTheEntry_FillsAtTheEntry()
    {
        FakeBroker broker = Broker();
        broker.PlaceStopOrder(Request());

        broker.Tick("EURUSD", 1.0869, 1.0870);

        Assert.Equal(1.0864, Assert.Single(broker.GetPositions()).EntryPrice);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void SellStop_BidAboveTheEntryLeavesItPending_BidAtTheEntryFillsAtTheEntry()
    {
        FakeBroker broker = Broker();
        broker.PlaceStopOrder(Request(Side.Sell));

        // A sell limit would fill here; a sell stop must not.
        broker.Tick("EURUSD", 1.0768, 1.0769);
        Assert.Single(broker.GetPendingOrders());
        Assert.Empty(broker.GetPositions());

        broker.Tick("EURUSD", 1.0767, 1.0768);
        PositionInfo position = Assert.Single(broker.GetPositions());
        Assert.Equal(Side.Sell, position.Side);
        Assert.Equal(1.0767, position.EntryPrice);
        Assert.Equal(1.0808, position.StopLoss);
        Assert.Equal(1.0687, position.TakeProfit);
        Assert.Empty(broker.GetPendingOrders());
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void PlaceStopOrder_UsesThePlaceFaultsOfLimitOrdersInScriptOrder()
    {
        FakeBroker broker = Broker();
        broker.Script(BrokerCall.Place, BrokerFault.Rejected, "First");
        broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Second");

        Assert.Equal(new OrderResult(OrderOutcome.Rejected, null, "First"), broker.PlaceStopOrder(Request()));
        Assert.Equal(new OrderResult(OrderOutcome.Unclear, null, "Second"), broker.PlaceLimitOrder(new LimitOrderRequest("EURUSD", Side.Buy, 1000.0, 1.08, 1.07, 1.10, Expiry, ExecutionData.Label("D-1"), "c")));
        Assert.Equal(0, broker.PendingFaults(BrokerCall.Place));
        Assert.Empty(broker.GetPendingOrders());
        Assert.Equal(new[] { OrderOutcome.Rejected, OrderOutcome.Unclear }, broker.Requests.Select(r => r.Outcome));
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void PlaceStopOrder_DisconnectedOrRefused_ChangesNothing()
    {
        FakeBroker broker = Broker();
        broker.IsConnected = false;
        Assert.Equal(new OrderResult(OrderOutcome.Unclear, null, "Disconnected"), broker.PlaceStopOrder(Request()));

        broker.IsConnected = true;
        Assert.Equal("InvalidVolume", broker.PlaceStopOrder(Request() with { VolumeUnits = 1500.0 }).ErrorCode);
        Assert.Equal("InvalidExpiry", broker.PlaceStopOrder(Request() with { ExpiryUtc = Now }).ErrorCode);
        Assert.Empty(broker.GetPendingOrders());
        Assert.Equal(3, broker.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void StopAndLimitOrders_ShareTheIdSequence()
    {
        FakeBroker broker = Broker();

        long? first = broker.PlaceLimitOrder(new LimitOrderRequest("EURUSD", Side.Buy, 1000.0, 1.08, 1.07, 1.10, Expiry, ExecutionData.Label("D-1"), "c")).Id;
        long? second = broker.PlaceStopOrder(Request()).Id;

        Assert.Equal(1001L, first);
        Assert.Equal(1002L, second);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void StopOrder_ExpiresOnAdvanceTo()
    {
        FakeBroker broker = Broker();
        broker.PlaceStopOrder(Request());

        broker.AdvanceTo(Expiry.AddTicks(-1));
        Assert.Single(broker.GetPendingOrders());
        broker.AdvanceTo(Expiry);
        Assert.Empty(broker.GetPendingOrders());
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void FilledBuyStop_ClosesAtItsStopOnTheBid()
    {
        FakeBroker broker = Broker();
        broker.PlaceStopOrder(Request());
        broker.Tick("EURUSD", 1.0863, 1.0864);

        broker.Tick("EURUSD", 1.0824, 1.0825);
        Assert.Single(broker.GetPositions());
        broker.Tick("EURUSD", 1.0823, 1.0824);

        Assert.Empty(broker.GetPositions());
        Assert.Equal(1.0823, Assert.Single(broker.History).ClosingPrice);
    }

    [Fact]
    [Trait("Req", "PLT-011")]
    public void SymbolInfo_BidAndAsk_DefaultToZeroAndAreSetByInit()
    {
        SymbolInfo plain = ExecutionData.EurUsd();
        SymbolInfo quoted = plain with { Bid = 1.0858, Ask = 1.0859 };

        Assert.Equal(0.0, plain.Bid);
        Assert.Equal(0.0, plain.Ask);
        Assert.Equal(1.0858, quoted.Bid);
        Assert.Equal(1.0859, quoted.Ask);
        Assert.NotEqual(plain, quoted);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void StopOrderRequest_HasTheNineMembersOfALimitOrderRequest()
    {
        string[] limit = typeof(LimitOrderRequest).GetProperties().Select(p => p.Name + ":" + p.PropertyType.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        string[] stop = typeof(StopOrderRequest).GetProperties().Select(p => p.Name + ":" + p.PropertyType.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(9, stop.Length);
        Assert.Equal(limit, stop);
    }
}
