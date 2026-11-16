using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The fake broker is the base of every execution scenario, so its behaviour is tested on its
/// own: each scripted fault, fills, stops, targets, expiry, history and request recording.
/// Numbers: EURUSD pip 0.0001, pip value 0.0001 USD per unit; an order of 250,000 units at
/// 1.10000 with stop 1.09800 and target 1.10400 expiring at 12:00.
/// </summary>
public class FakeBrokerTests
{
    private static readonly DateTime Now = ExecutionData.Now;
    private static readonly DateTime Expiry = ExecutionData.At(14, 12, 0);

    private static LimitOrderRequest Request(Side side = Side.Buy, double volume = 250000.0)
    {
        double stop = side == Side.Buy ? 1.098 : 1.102;
        double target = side == Side.Buy ? 1.104 : 1.096;
        return new LimitOrderRequest("EURUSD", side, volume, 1.1, stop, target, Expiry, ExecutionData.Label("D-1"), "c");
    }

    private static FakeBroker Broker(double commission = 0.0)
    {
        return new FakeBroker(Now, ExecutionData.Account(), ExecutionData.EurUsd(commissionPerUnit: commission));
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void PlaceLimitOrder_Success_OrderListedWithItsProtectionExpiryLabelAndComment()
    {
        FakeBroker broker = Broker();

        OrderResult result = broker.PlaceLimitOrder(Request());

        Assert.Equal(OrderOutcome.Success, result.Outcome);
        PendingOrderInfo order = Assert.Single(broker.GetPendingOrders());
        Assert.Equal(result.Id, order.Id);
        Assert.Equal(1001L, order.Id);
        Assert.Equal(1.098, order.StopLoss);
        Assert.Equal(1.104, order.TakeProfit);
        Assert.Equal(Expiry, order.ExpiryUtc);
        Assert.Equal(ExecutionData.Label("D-1"), order.Label);
        Assert.Equal("c", order.Comment);
        Assert.Equal(Now, order.CreatedTimeUtc);
        Assert.Equal(1, broker.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void ScriptedRejection_ReturnsTheCodeAndChangesNothing()
    {
        FakeBroker broker = Broker();
        broker.Script(BrokerCall.Place, BrokerFault.Rejected, "NotEnoughMoney");

        OrderResult result = broker.PlaceLimitOrder(Request());

        Assert.Equal(new OrderResult(OrderOutcome.Rejected, null, "NotEnoughMoney"), result);
        Assert.Empty(broker.GetPendingOrders());
        Assert.Equal(OrderOutcome.Rejected, Assert.Single(broker.Requests).Outcome);
        Assert.Equal(0, broker.PendingFaults(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void ScriptedUnclearApplied_OrderExistsButResultIsUnclear()
    {
        FakeBroker broker = Broker();
        broker.Script(BrokerCall.Place, BrokerFault.UnclearApplied, "Timeout");

        OrderResult result = broker.PlaceLimitOrder(Request());

        Assert.Equal(OrderOutcome.Unclear, result.Outcome);
        Assert.Null(result.Id);
        Assert.Single(broker.GetPendingOrders());
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void ScriptedUnclearNotApplied_NoOrderAndResultIsUnclear()
    {
        FakeBroker broker = Broker();
        broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");

        OrderResult result = broker.PlaceLimitOrder(Request());

        Assert.Equal(OrderOutcome.Unclear, result.Outcome);
        Assert.Empty(broker.GetPendingOrders());
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void Disconnected_EveryRequestIsUnclearAndChangesNothing()
    {
        FakeBroker broker = Broker();
        long position = broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.1, 1.098, 1.104, ExecutionData.Label("D-2"), "c");
        long order = broker.AddOrder("EURUSD", Side.Buy, 1000.0, 1.1, 1.098, 1.104, Expiry, ExecutionData.Label("D-3"), "c");
        broker.IsConnected = false;

        Assert.Equal(OrderOutcome.Unclear, broker.PlaceLimitOrder(Request()).Outcome);
        Assert.Equal(OrderOutcome.Unclear, broker.CancelOrder(order).Outcome);
        Assert.Equal(OrderOutcome.Unclear, broker.ModifyPositionProtection(position, 1.097, 1.104).Outcome);
        Assert.Equal(OrderOutcome.Unclear, broker.ClosePosition(position).Outcome);

        Assert.Single(broker.GetPendingOrders());
        Assert.Equal(1.098, Assert.Single(broker.GetPositions()).StopLoss);
        Assert.Equal(4, broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-010")]
    public void ScriptedDropStopLoss_OrderAcceptedWithoutItsStop()
    {
        FakeBroker broker = Broker();
        broker.Script(BrokerCall.Place, BrokerFault.DropStopLoss);

        OrderResult result = broker.PlaceLimitOrder(Request());

        Assert.Equal(OrderOutcome.Success, result.Outcome);
        PendingOrderInfo order = Assert.Single(broker.GetPendingOrders());
        Assert.Null(order.StopLoss);
        Assert.Equal(1.104, order.TakeProfit);
    }

    [Fact]
    [Trait("Req", "EXE-010")]
    public void ScriptedDropTakeProfitAndDropExpiry_OrderAcceptedWithoutThem()
    {
        FakeBroker broker = Broker();
        broker.Script(BrokerCall.Place, BrokerFault.DropTakeProfit);
        broker.Script(BrokerCall.Place, BrokerFault.DropExpiry);

        broker.PlaceLimitOrder(Request());
        broker.PlaceLimitOrder(Request() with { Label = ExecutionData.Label("D-2") });

        PendingOrderInfo[] orders = broker.GetPendingOrders().ToArray();
        Assert.Null(orders[0].TakeProfit);
        Assert.Equal(Expiry, orders[0].ExpiryUtc);
        Assert.Equal(1.104, orders[1].TakeProfit);
        Assert.Null(orders[1].ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "EXE-010")]
    public void ScriptedPlaceSuccessNotApplied_ReturnsAnIdButNoOrderExists()
    {
        FakeBroker broker = Broker();
        broker.Script(BrokerCall.Place, BrokerFault.SuccessNotApplied);

        OrderResult result = broker.PlaceLimitOrder(Request());

        Assert.Equal(new OrderResult(OrderOutcome.Success, 1001L, null), result);
        Assert.Empty(broker.GetPendingOrders());
        Assert.Empty(broker.GetPositions());
    }

    [Fact]
    [Trait("Req", "EXE-010")]
    public void ScriptedFilledAtOnce_PositionWithTheOrdersProtectionAndNoPendingOrder()
    {
        FakeBroker broker = Broker();
        broker.Script(BrokerCall.Place, BrokerFault.FilledAtOnce);

        OrderResult result = broker.PlaceLimitOrder(Request());

        Assert.Equal(OrderOutcome.Success, result.Outcome);
        Assert.Empty(broker.GetPendingOrders());
        PositionInfo position = Assert.Single(broker.GetPositions());
        Assert.Equal(250000.0, position.VolumeUnits);
        Assert.Equal(1.098, position.StopLoss);
        Assert.Equal(ExecutionData.Label("D-1"), position.Label);
    }

    [Fact]
    public void ReportedKind_ChangesOnlyTheKindOfTheServerTime()
    {
        FakeBroker broker = Broker();
        broker.ReportedKind = DateTimeKind.Local;

        Assert.Equal(DateTimeKind.Local, broker.ServerTimeUtc.Kind);
        Assert.Equal(Now.Ticks, broker.ServerTimeUtc.Ticks);
    }

    [Fact]
    [Trait("Req", "EXE-012")]
    public void ScriptedPartialFill_FirstFillIsTheFractionAndTheRemainderExpires()
    {
        // 40% of 250,000 = 100,000 fills; 150,000 stays pending, never fills, and expires at 12:00.
        FakeBroker broker = Broker();
        broker.Script(BrokerCall.Place, BrokerFault.PartialFill, fraction: 0.4);
        broker.PlaceLimitOrder(Request());

        broker.Tick("EURUSD", 1.0999, 1.1);
        broker.Tick("EURUSD", 1.0998, 1.0999);

        PositionInfo position = Assert.Single(broker.GetPositions());
        Assert.Equal(100000.0, position.VolumeUnits);
        Assert.Equal(1.098, position.StopLoss);
        Assert.Equal(1.104, position.TakeProfit);
        Assert.Equal(ExecutionData.Label("D-1"), position.Label);
        Assert.Equal(150000.0, Assert.Single(broker.GetPendingOrders()).VolumeUnits);

        broker.AdvanceTo(Expiry.AddTicks(-1));
        Assert.Single(broker.GetPendingOrders());
        broker.AdvanceTo(Expiry);
        Assert.Empty(broker.GetPendingOrders());
        Assert.Single(broker.GetPositions());
    }

    [Theory]
    [Trait("Req", "EXE-012")]
    [InlineData(1.1, 1.10001, false)] // ask above the buy limit: no fill
    [InlineData(1.0999, 1.1, true)] // ask at the limit: fill
    public void Tick_BuyLimit_FillsWhenTheAskReachesTheEntry(double bid, double ask, bool fills)
    {
        FakeBroker broker = Broker();
        broker.PlaceLimitOrder(Request());

        broker.Tick("EURUSD", bid, ask);

        Assert.Equal(fills ? 1 : 0, broker.GetPositions().Count);
        Assert.Equal(fills ? 0 : 1, broker.GetPendingOrders().Count);
    }

    [Theory]
    [Trait("Req", "EXE-012")]
    [InlineData(1.09999, 1.10009, false)] // bid below the sell limit: no fill
    [InlineData(1.1, 1.1001, true)] // bid at the limit: fill
    public void Tick_SellLimit_FillsWhenTheBidReachesTheEntry(double bid, double ask, bool fills)
    {
        FakeBroker broker = Broker();
        broker.PlaceLimitOrder(Request(Side.Sell));

        broker.Tick("EURUSD", bid, ask);

        Assert.Equal(fills ? 1 : 0, broker.GetPositions().Count);
    }

    [Fact]
    [Trait("Req", "EXE-012")]
    public void Tick_BuyTargetReached_PositionClosedAtTheTargetWithAHistoryRow()
    {
        // 40 pips x 0.0001 USD x 250,000 units = 1,000 USD, minus commission 0.00002 x 250,000 = 5:
        // net 995; balance 100,995.
        FakeBroker broker = Broker(commission: 0.00002);
        broker.PlaceLimitOrder(Request());
        broker.Tick("EURUSD", 1.0999, 1.1);
        broker.AdvanceTo(ExecutionData.At(14, 11, 0));

        broker.Tick("EURUSD", 1.104, 1.1041);

        Assert.Empty(broker.GetPositions());
        ClosedTradeInfo trade = Assert.Single(broker.GetHistory(Now));
        Assert.Equal(995.0, trade.NetProfit, 6);
        Assert.Equal(-5.0, trade.Commission, 6);
        Assert.Equal(100995.0, trade.BalanceAfterClose, 6);
        Assert.Equal(ExecutionData.At(14, 11, 0), trade.CloseTimeUtc);
        Assert.Equal(Now, trade.EntryTimeUtc);
        Assert.Equal(100995.0, broker.GetAccount().Balance, 6);
    }

    [Fact]
    [Trait("Req", "EXE-012")]
    public void Tick_SellStopReached_PositionClosedAtTheStop()
    {
        // Sell 250,000 at 1.10000, stop 1.10200 reached by the ask: -20 pips = -500 USD.
        FakeBroker broker = Broker();
        broker.PlaceLimitOrder(Request(Side.Sell));
        broker.Tick("EURUSD", 1.1, 1.1001);

        broker.Tick("EURUSD", 1.1019, 1.102);

        Assert.Empty(broker.GetPositions());
        Assert.Equal(-500.0, Assert.Single(broker.History).NetProfit, 6);
        Assert.Equal(99500.0, broker.GetAccount().Equity, 6);
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void Modify_ValidStop_IsAppliedAndAStopOnTheWrongSideOfPriceIsRejected()
    {
        FakeBroker broker = Broker();
        long id = broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.1, null, 1.104, ExecutionData.Label("D-2"), "c");
        broker.Tick("EURUSD", 1.0990, 1.0991);

        OrderResult above = broker.ModifyPositionProtection(id, 1.0995, 1.104);
        OrderResult below = broker.ModifyPositionProtection(id, 1.098, 1.104);

        Assert.Equal(new OrderResult(OrderOutcome.Rejected, null, "InvalidStopLoss"), above);
        Assert.Equal(OrderOutcome.Success, below.Outcome);
        Assert.Equal(1.098, Assert.Single(broker.GetPositions()).StopLoss);
    }

    [Theory]
    [Trait("Req", "EXE-011")]
    [InlineData(BrokerFault.Rejected, OrderOutcome.Rejected, false)]
    [InlineData(BrokerFault.UnclearApplied, OrderOutcome.Unclear, true)]
    [InlineData(BrokerFault.UnclearNotApplied, OrderOutcome.Unclear, false)]
    [InlineData(BrokerFault.SuccessNotApplied, OrderOutcome.Success, false)]
    public void Modify_ScriptedFault_ResultAndEffectAsScripted(BrokerFault fault, OrderOutcome outcome, bool applied)
    {
        FakeBroker broker = Broker();
        long id = broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.1, null, 1.104, ExecutionData.Label("D-2"), "c");
        broker.Script(BrokerCall.Modify, fault);

        OrderResult result = broker.ModifyPositionProtection(id, 1.098, 1.104);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(applied, Assert.Single(broker.GetPositions()).StopLoss.HasValue);
    }

    [Theory]
    [Trait("Req", "EXE-013")]
    [InlineData(BrokerFault.Rejected, OrderOutcome.Rejected, false)]
    [InlineData(BrokerFault.UnclearApplied, OrderOutcome.Unclear, true)]
    [InlineData(BrokerFault.UnclearNotApplied, OrderOutcome.Unclear, false)]
    public void CloseAndCancel_ScriptedFault_ResultAndEffectAsScripted(BrokerFault fault, OrderOutcome outcome, bool applied)
    {
        FakeBroker broker = Broker();
        long position = broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.1, 1.098, 1.104, ExecutionData.Label("D-2"), "c");
        long order = broker.AddOrder("EURUSD", Side.Buy, 1000.0, 1.1, 1.098, 1.104, Expiry, ExecutionData.Label("D-3"), "c");
        broker.Script(BrokerCall.Close, fault);
        broker.Script(BrokerCall.Cancel, fault);

        Assert.Equal(outcome, broker.ClosePosition(position).Outcome);
        Assert.Equal(outcome, broker.CancelOrder(order).Outcome);

        Assert.Equal(applied ? 0 : 1, broker.GetPositions().Count);
        Assert.Equal(applied ? 0 : 1, broker.GetPendingOrders().Count);
    }

    [Fact]
    [Trait("Req", "EXE-013")]
    public void ClosePosition_AtTheCurrentPrice_BooksTheResult()
    {
        // Buy 1,000 at 1.10000 closed at the bid 1.10100: +10 pips x 0.0001 x 1,000 = 1 USD.
        FakeBroker broker = Broker();
        long id = broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.1, 1.098, 1.104, ExecutionData.Label("D-2"), "c");
        broker.Tick("EURUSD", 1.101, 1.1011);

        Assert.Equal(new OrderResult(OrderOutcome.Success, id, null), broker.ClosePosition(id));
        Assert.Equal(1.0, Assert.Single(broker.History).NetProfit, 6);
        Assert.Equal(OrderOutcome.Rejected, broker.ClosePosition(id).Outcome);
        Assert.Equal(OrderOutcome.Rejected, broker.CancelOrder(99).Outcome);
    }

    [Theory]
    [Trait("Req", "EXE-009")]
    [InlineData(500.0, "InvalidVolume")] // below the minimum 1,000
    [InlineData(1500.0, "InvalidVolume")] // not a multiple of the step 1,000
    [InlineData(10001000.0, "InvalidVolume")] // above the maximum 10,000,000
    public void PlaceLimitOrder_BadVolume_IsRejected(double volume, string code)
    {
        FakeBroker broker = Broker();

        Assert.Equal(new OrderResult(OrderOutcome.Rejected, null, code), broker.PlaceLimitOrder(Request(volume: volume)));
    }

    [Fact]
    [Trait("Req", "EXE-009")]
    public void PlaceLimitOrder_MarketClosedOrExpiryNotInTheFuture_IsRejected()
    {
        FakeBroker broker = Broker();
        broker.SetSymbol(ExecutionData.EurUsd(marketOpen: false));
        Assert.Equal("MarketClosed", broker.PlaceLimitOrder(Request()).ErrorCode);

        broker.SetSymbol(ExecutionData.EurUsd());
        Assert.Equal("InvalidExpiry", broker.PlaceLimitOrder(Request() with { ExpiryUtc = Now }).ErrorCode);
        Assert.Equal("UnknownSymbol", broker.PlaceLimitOrder(Request() with { Symbol = "GBPUSD" }).ErrorCode);
    }

    [Fact]
    public void Faults_AreUsedOnceInTheOrderScripted()
    {
        FakeBroker broker = Broker();
        broker.Script(BrokerCall.Place, BrokerFault.Rejected, "First");
        broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Second");

        Assert.Equal("First", broker.PlaceLimitOrder(Request()).ErrorCode);
        Assert.Equal("Second", broker.PlaceLimitOrder(Request()).ErrorCode);
        Assert.Equal(OrderOutcome.Success, broker.PlaceLimitOrder(Request()).Outcome);
        Assert.Equal(3, broker.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "RSK-003")]
    public void EstimateMargin_ReturnsVolumeTimesMarginPerUnitAndRecordsTheCall()
    {
        FakeBroker broker = Broker();
        broker.MarginPerUnit = 0.04;

        double margin = broker.EstimateMargin("EURUSD", Side.Sell, 250000.0);

        Assert.Equal(10000.0, margin, 9);
        Assert.Equal(("EURUSD", Side.Sell, 250000.0, margin), Assert.Single(broker.MarginRequests));
    }

    [Fact]
    public void AdvanceTo_BackwardsOrNotUtc_Throws()
    {
        FakeBroker broker = Broker();

        Assert.Throws<InvalidOperationException>(() => broker.AdvanceTo(Now.AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => broker.AdvanceTo(new DateTime(2026, 1, 14, 11, 0, 0, DateTimeKind.Unspecified)));
        Assert.Throws<ArgumentException>(() => broker.GetSymbol("GBPUSD"));
    }
}
