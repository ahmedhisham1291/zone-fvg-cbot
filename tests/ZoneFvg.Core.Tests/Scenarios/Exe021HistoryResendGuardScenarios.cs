using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-021: when an unclear placement is resolved and the broker shows neither a pending order
/// nor a position with the label, the history since the signal's confirmation close is read
/// before anything else is decided. A closed trade with the label ends the placement as Placed
/// ("filled and closed as position &lt;id&gt;") and nothing is resent; no history list abandons
/// it as UnclearOutcomeNotResent; otherwise EXE-009 and EXE-020 apply unchanged.
/// </summary>
/// <remarks>
/// <para>Data: <see cref="ExecutionData"/>. The breakout buy (signal entry 1.08630) is placed as a
/// buy stop at 1.08640, stop 1.08230, target 1.09430; the breakout sell as a sell stop at 1.07670,
/// stop 1.08080, target 1.06870; the ZoneFvg buy as a buy limit at 1.10000, stop 1.09800. The
/// signals confirm at 10:00; the next bar opens at 10:00 and closes at 10:15.</para>
/// <para>The gap: a buy stop triggers on the ask. With the ask at 1.08640 and the bid at 1.08620
/// the order fills, the bid then reaches the stop, and the bid bar's high 1.08620 stays below the
/// signal's entry: without the history the rule of EXE-020 would send the order a second time.</para>
/// </remarks>
public class Exe021HistoryResendGuardScenarios
{
    private const string Symbol = ExecutionData.Symbol;

    private static readonly DateTime NextClose = ExecutionData.At(14, 10, 15);

    private static Bar NextBar(double high, double low)
    {
        double middle = (high + low) / 2.0;
        return ExecutionData.M15(ExecutionData.Now, middle, high, low, middle);
    }

    private static string Id(long id)
    {
        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static ExecutionHarness Unclear(Signal signal, BrokerFault fault, SymbolInfo quote)
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness(quote);
        h.Broker.Script(BrokerCall.Place, fault, "Timeout");
        Assert.Equal(PlaceOutcome.Unclear, h.EvaluateAndPlace(signal)!.Outcome);
        return h;
    }

    // The buy stop (or buy limit) fills at 10:05 and is stopped out at 10:10.
    private static ClosedTradeInfo FillAndClose(ExecutionHarness h, double fillBid, double fillAsk, double stopBid, double stopAsk, double entry, double stop)
    {
        h.Broker.AdvanceTo(ExecutionData.At(14, 10, 5));
        h.Broker.Tick(Symbol, fillBid, fillAsk);
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(entry, position.EntryPrice, 9);
        Assert.Empty(h.Broker.GetPendingOrders());
        h.Broker.AdvanceTo(ExecutionData.At(14, 10, 10));
        h.Broker.Tick(Symbol, stopBid, stopAsk);
        Assert.Empty(h.Broker.GetPositions());
        ClosedTradeInfo trade = Assert.Single(h.Broker.History);
        Assert.Equal(position.Id, trade.PositionId);
        Assert.Equal(stop, trade.ClosingPrice!.Value, 9);
        return trade;
    }

    private static void AssertClosedAndNotResent(ExecutionHarness h, PlaceResult resolved, ClosedTradeInfo trade)
    {
        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal("filled and closed as position " + Id(trade.PositionId), resolved.Detail);
        Assert.Equal(trade.Label, resolved.Label);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Single(h.Broker.Requests);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Empty(h.Broker.GetPositions());
        Assert.Empty(h.Events.Skips);
        Assert.Empty(h.Events.Alerts);
        Assert.Equal(0, h.Orders.UnresolvedCount);
        Assert.False(h.Orders.HasUnresolved(Symbol));
        Assert.Empty(h.Orders.ResolvePending(new[] { NextBar(1.0862, 1.0823) }));
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ the gap

    [Fact]
    [Trait("Req", "EXE-021")]
    public void BuyStop_AppliedFilledAndStoppedOutBeforeTheNextBar_PlacedAsClosedAndNotResent()
    {
        ExecutionHarness h = Unclear(ExecutionData.BreakoutBuy(), BrokerFault.UnclearApplied, ExecutionData.BuyQuote());
        Assert.Equal(1.0864, Assert.Single(h.Broker.GetPendingOrders()).EntryPrice, 9);

        // A spread of 2 pips: the ask 1.08640 fills the stop while the bid is 1.08620.
        ClosedTradeInfo trade = FillAndClose(h, 1.0862, 1.0864, 1.0823, 1.0825, 1.0864, 1.0823);
        Assert.Equal(ExecutionData.Label(ExecutionData.BreakoutBuyId), trade.Label);
        h.Broker.AdvanceTo(NextClose);

        // The bar's high 1.08620 is below the signal's entry 1.08630: EXE-020 alone would resend.
        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.0862, 1.0823) }));

        Assert.All(h.Broker.Requests, request => Assert.NotNull(request.StopOrder));
        AssertClosedAndNotResent(h, resolved, trade);
    }

    [Fact]
    [Trait("Req", "EXE-021")]
    public void SellStop_AppliedFilledAndStoppedOutBeforeTheNextBar_PlacedAsClosedAndNotResent()
    {
        ExecutionHarness h = Unclear(ExecutionData.BreakoutSell(), BrokerFault.UnclearApplied, ExecutionData.SellQuote());
        Assert.Equal(1.0808, Assert.Single(h.Broker.GetPendingOrders()).StopLoss!.Value, 9);

        // The bid 1.07670 fills the sell stop; the ask 1.08080 reaches its stop.
        ClosedTradeInfo trade = FillAndClose(h, 1.0767, 1.0768, 1.0807, 1.0808, 1.0767, 1.0808);
        Assert.Equal(ExecutionData.Label(ExecutionData.BreakoutSellId), trade.Label);
        h.Broker.AdvanceTo(NextClose);

        // The bar's low 1.07671 stays above the signal's entry 1.07670: EXE-020 alone would resend.
        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.0807, 1.07671) }));

        Assert.All(h.Broker.Requests, request => Assert.NotNull(request.StopOrder));
        AssertClosedAndNotResent(h, resolved, trade);
    }

    [Fact]
    [Trait("Req", "EXE-021")]
    public void LimitOrder_AppliedFilledAndStoppedOutBeforeTheNextBar_PlacedAsClosedAndNotResent()
    {
        ExecutionHarness h = Unclear(ExecutionData.Buy(), BrokerFault.UnclearApplied, ExecutionData.BuyQuote());

        // The ask 1.10000 fills the buy limit; the bid 1.09800 reaches its stop.
        ClosedTradeInfo trade = FillAndClose(h, 1.0999, 1.1, 1.098, 1.0981, 1.1, 1.098);
        Assert.Equal(ExecutionData.Label(ExecutionData.BuyZone), trade.Label);
        h.Broker.AdvanceTo(NextClose);

        // The bar's low 1.10001 stays above the entry: EXE-009 alone would resend.
        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.1008, 1.10001) }));

        Assert.All(h.Broker.Requests, request => Assert.Null(request.StopOrder));
        Assert.All(h.Broker.Requests, request => Assert.NotNull(request.Order));
        AssertClosedAndNotResent(h, resolved, trade);
    }

    [Fact]
    [Trait("Req", "EXE-021")]
    public void ClosedTradeWithAnotherLabel_DoesNotBlock_TheOrderIsResentOnceAsByExe020()
    {
        ExecutionHarness h = Unclear(ExecutionData.BreakoutBuy(), BrokerFault.UnclearNotApplied, ExecutionData.BuyQuote());
        h.Broker.AddClosedTrade(new ClosedTradeInfo(901, Symbol, ExecutionData.Label("BH-20260111-2200"), ExecutionData.At(14, 9, 0), ExecutionData.At(14, 10, 5), -100.0, 0.0, 0.0, 99900.0));
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal("verified", resolved.Detail);
        Assert.Equal(2, h.Broker.Count(BrokerCall.Place));
        Assert.All(h.Broker.Requests, request => Assert.NotNull(request.StopOrder));
        Assert.Equal(ExecutionData.Label(ExecutionData.BreakoutBuyId), Assert.Single(h.Broker.GetPendingOrders()).Label);
        Assert.Equal(0, h.Orders.UnresolvedCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-021")]
    public void BeforeABarHasClosed_ClosedTradeInTheHistory_ResolvedAtOnceAsClosed()
    {
        ExecutionHarness h = Unclear(ExecutionData.BreakoutBuy(), BrokerFault.UnclearApplied, ExecutionData.BuyQuote());
        ClosedTradeInfo trade = FillAndClose(h, 1.0862, 1.0864, 1.0823, 1.0825, 1.0864, 1.0823);
        h.Broker.AdvanceTo(NextClose.AddTicks(-1));

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(Array.Empty<Bar>()));

        AssertClosedAndNotResent(h, resolved, trade);
    }

    [Fact]
    [Trait("Req", "EXE-021")]
    public void BeforeABarHasClosed_EmptyHistory_StaysUnresolved()
    {
        ExecutionHarness h = Unclear(ExecutionData.BreakoutBuy(), BrokerFault.UnclearNotApplied, ExecutionData.BuyQuote());
        h.Broker.AdvanceTo(NextClose.AddTicks(-1));

        Assert.Empty(h.Orders.ResolvePending(Array.Empty<Bar>()));

        Assert.Equal(1, h.Orders.UnresolvedCount);
        Assert.True(h.Orders.HasUnresolved(Symbol));
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Events.Skips);
    }

    // ------------------------------------------------------------------ no history, a failing history

    [Theory]
    [Trait("Req", "EXE-021")]
    [InlineData(false, 0L)]
    [InlineData(true, 0L)]
    [InlineData(false, -1L)]
    [InlineData(true, -1L)]
    public void NoHistoryList_Abandoned_RecordedAsUnclearOutcomeNotResent_NothingSent(bool limit, long ticksFromTheNextClose)
    {
        // The bar does not reach the entry: without the history rule the order would be resent.
        // Before a bar has closed (one tick before 10:15) the history is read all the same.
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.BuyQuote());
        var broker = new HistoryBroker(h.Broker) { Mode = HistoryMode.Null };
        var orders = new OrderManager(broker, h.Counter, h.Events);
        PlannedOrder order = h.OrderOf(limit ? ExecutionData.Buy() : ExecutionData.BreakoutBuy());
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        Assert.Equal(PlaceOutcome.Unclear, orders.TryPlace(order).Outcome);
        DateTime now = NextClose.AddTicks(ticksFromTheNextClose);
        h.Broker.AdvanceTo(now);

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { limit ? NextBar(1.1008, 1.10001) : NextBar(1.0862, 1.0855) }));

        const string detail = "history not available, not resent";
        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Equal(detail, resolved.Detail);
        Assert.Equal(order.Label, resolved.Label);
        SkipRecord skip = Assert.Single(h.Events.Skips);
        Assert.Equal(SkipReason.UnclearOutcomeNotResent, skip.Reason);
        Assert.Equal(detail, skip.Detail);
        Assert.Equal(order.Signal.ZoneId, skip.ZoneId);
        Assert.Equal(Symbol, skip.Symbol);
        Assert.Equal(now, skip.AtUtc);
        Assert.Empty(h.Events.Alerts);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(new[] { order.Signal.ConfirmationCloseUtc }, broker.HistoryCalls);
        Assert.Equal(0, orders.UnresolvedCount);
        Assert.Empty(orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "EXE-021")]
    public void HistoryThrows_ExceptionLeavesResolvePending_NothingSentStillUnresolved_ThenExe020Applies()
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.BuyQuote());
        var broker = new HistoryBroker(h.Broker) { Mode = HistoryMode.Throw };
        var orders = new OrderManager(broker, h.Counter, h.Events);
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        Assert.Equal(PlaceOutcome.Unclear, orders.TryPlace(h.OrderOf(ExecutionData.BreakoutBuy())).Outcome);
        h.Broker.AdvanceTo(NextClose);
        Bar[] bars = { NextBar(1.0862, 1.0855) };

        Assert.Throws<InvalidOperationException>(() => orders.ResolvePending(bars));

        Assert.Equal(1, orders.UnresolvedCount);
        Assert.True(orders.HasUnresolved(Symbol));
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Events.Skips);
        Assert.Single(broker.HistoryCalls);

        // Readable and empty at the next call: EXE-020 resends once.
        broker.Mode = HistoryMode.Inner;
        PlaceResult resolved = Assert.Single(orders.ResolvePending(bars));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal(2, h.Broker.Count(BrokerCall.Place));
        Assert.Equal(0, orders.UnresolvedCount);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ when the history is read

    [Fact]
    [Trait("Req", "EXE-021")]
    public void NothingAtTheBroker_HistoryReadOnceFromTheConfirmationClose()
    {
        // Resolved at 10:20, so that the confirmation (10:00) differs from now minus one bar (10:05).
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.BuyQuote());
        var broker = new HistoryBroker(h.Broker);
        var orders = new OrderManager(broker, h.Counter, h.Events);
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        Assert.Equal(PlaceOutcome.Unclear, orders.TryPlace(h.OrderOf(ExecutionData.BreakoutBuy())).Outcome);
        Assert.Empty(broker.HistoryCalls);
        h.Broker.AdvanceTo(ExecutionData.At(14, 10, 20));

        Assert.Single(orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));

        Assert.Equal(new[] { ExecutionData.Now }, broker.HistoryCalls);
    }

    [Theory]
    [Trait("Req", "EXE-021")]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderOrPositionWithTheLabelExists_HistoryNotRead(bool filled)
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.BuyQuote());
        var broker = new HistoryBroker(h.Broker);
        var orders = new OrderManager(broker, h.Counter, h.Events);
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearApplied, "Timeout");
        Assert.Equal(PlaceOutcome.Unclear, orders.TryPlace(h.OrderOf(ExecutionData.BreakoutBuy())).Outcome);
        if (filled)
        {
            h.Broker.AdvanceTo(ExecutionData.At(14, 10, 5));
            h.Broker.Tick(Symbol, 1.0862, 1.0864);
            Assert.Single(h.Broker.GetPositions());
        }

        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.StartsWith(filled ? "filled as position " : "verified", resolved.Detail, StringComparison.Ordinal);
        Assert.Empty(broker.HistoryCalls);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    private enum HistoryMode
    {
        Inner,
        Null,
        Throw,
    }

    /// <summary>A <see cref="FakeBroker"/> whose history can be missing or fail; every history read is recorded.</summary>
    private sealed class HistoryBroker : IBroker
    {
        private readonly FakeBroker _inner;

        public HistoryBroker(FakeBroker inner)
        {
            _inner = inner;
        }

        public HistoryMode Mode { get; set; }

        public List<DateTime> HistoryCalls { get; } = new();

        public DateTime ServerTimeUtc => _inner.ServerTimeUtc;

        public bool IsConnected => _inner.IsConnected;

        public AccountSnapshot GetAccount() => _inner.GetAccount();

        public SymbolInfo GetSymbol(string symbol) => _inner.GetSymbol(symbol);

        public IReadOnlyList<PositionInfo> GetPositions() => _inner.GetPositions();

        public IReadOnlyList<PendingOrderInfo> GetPendingOrders() => _inner.GetPendingOrders();

        public IReadOnlyList<ClosedTradeInfo> GetHistory(DateTime sinceUtc)
        {
            HistoryCalls.Add(sinceUtc);
            return Mode switch
            {
                HistoryMode.Null => null!,
                HistoryMode.Throw => throw new InvalidOperationException("history unavailable"),
                _ => _inner.GetHistory(sinceUtc),
            };
        }

        public double EstimateMargin(string symbol, Side side, double volumeUnits) => _inner.EstimateMargin(symbol, side, volumeUnits);

        public OrderResult PlaceLimitOrder(LimitOrderRequest request) => _inner.PlaceLimitOrder(request);

        public OrderResult PlaceStopOrder(StopOrderRequest request) => _inner.PlaceStopOrder(request);

        public OrderResult CancelOrder(long orderId) => _inner.CancelOrder(orderId);

        public OrderResult ModifyPositionProtection(long positionId, double? stopLoss, double? takeProfit) => _inner.ModifyPositionProtection(positionId, stopLoss, takeProfit);

        public OrderResult ClosePosition(long positionId) => _inner.ClosePosition(positionId);
    }
}
