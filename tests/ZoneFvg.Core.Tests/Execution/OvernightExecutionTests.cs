using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-033 in the execution layer, with the EURUSD values of <see cref="ExecutionData"/>: the
/// Overnight buy ON-20260114 confirms at 10:00 UTC with the entry 1.0863, stop 1.0823 (40 pips)
/// and target 1.0943, expiry 11:00 UTC, ATR(H4) 0.004 (not used: the largest stop of spec 3.6 does not
/// apply to this kind, answer 1 on pull request 109). The order adds the
/// spread 0.0001 to all three levels: 1.0864, 1.0824 and 1.0944. EXE-020, EXE-021 and EXE-023 for
/// this kind are the cases of <c>SessionRangeUnclearPlacementTests</c>: the next bar opens 10:00
/// and closes 10:15.
/// </summary>
public class OvernightExecutionTests
{
    private const string Id = "ON-20260114";

    private static readonly DateTime NextClose = ExecutionData.At(14, 10, 15);

    private static readonly DateTime Expiry = ExecutionData.At(14, 11, 0);

    private static Signal NightBuy(DateTime? expiryUtc = null, double atrH4 = 0.004, Side side = Side.Buy)
    {
        return new Signal(Id, side, 1.0863, 1.0823, 1.0943, null, ExecutionData.Now, false, atrH4)
        {
            Kind = SignalKind.Overnight,
            Level = 1.0863,
            ExpiryUtc = expiryUtc ?? Expiry,
        };
    }

    // ------------------------------------------------------------------ the filters

    [Fact]
    [Trait("Req", "EXE-033")]
    public void Evaluate_OvernightBuy_AStopOrderWithTheSpreadOnEntryStopAndTargetAndTheSignalsExpiry()
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.BuyQuote()));

        PlannedOrder order = h.OrderOf(NightBuy());

        Assert.Equal(EntryOrderType.Stop, order.EntryType);
        Assert.Equal(1.0864, order.Entry, 9);
        Assert.Equal(1.0824, order.StopLoss, 9);
        Assert.Equal(1.0944, order.TakeProfit, 9);
        Assert.Equal(40.0, order.StopPips, 6);
        Assert.Equal(Expiry, order.ExpiryUtc);
        Assert.Equal("ZFVG|v1|EURUSD|ON-20260114", order.Label);
        PlaceResult placed = h.Orders.TryPlace(order);
        Assert.Equal(PlaceOutcome.Placed, placed.Outcome);
        StopOrderRequest request = Assert.Single(h.Broker.Requests).StopOrder!;
        Assert.Equal(Side.Buy, request.Side);
        Assert.Equal(Expiry, request.ExpiryUtc);
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [InlineData(1.0863, 1.0864)]
    [InlineData(1.0864, 1.0865)]
    public void Evaluate_AskAtOrAboveTheOrderEntry_SkippedAsEntryAlreadyReached(double bid, double ask)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.Quoted(bid, ask)));

        SkipRecord skip = h.SkipOf(NightBuy());

        Assert.Equal(SkipReason.EntryAlreadyReached, skip.Reason);
        Assert.Equal(Id, skip.ZoneId);
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void Evaluate_AskOneTickBelowTheOrderEntry_Placed()
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.Quoted(1.08629, 1.08639)));

        Assert.Equal(1.0864, h.OrderOf(NightBuy()).Entry, 9);
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [InlineData(0.00016, false)]
    [InlineData(0.00015, true)]
    public void Evaluate_SpreadAboveMaxSpreadPips_SkippedAsSpreadTooWide(double spread, bool placed)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.Quoted(1.0850, 1.0850 + spread, spread)));

        PreTradeResult result = h.Evaluate(NightBuy());

        Assert.Equal(placed, result.IsAllowed);
        if (!placed)
        {
            Assert.Equal(SkipReason.SpreadTooWide, result.Skip!.Reason);
        }
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [InlineData(0.002)]
    [InlineData(0.00199)]
    [InlineData(0.0005)]
    [InlineData(0.0)]
    public void Evaluate_StopFarAbove2AtrH4_StillPlaced(double atrH4)
    {
        // Orchestrating session's answer 1 on pull request 109: MaxStopAtr x ATR(H4) does not apply to an Overnight
        // signal; the 40-pip stop (1 x ATR(D1)) is placed with ATR(H4) 0.0005 (largest stop 10 pips) or none.
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.BuyQuote()));

        PlannedOrder order = h.OrderOf(NightBuy(atrH4: atrH4));

        Assert.Equal(40.0, order.StopPips, 6);
        Assert.Equal(1.0824, order.StopLoss, 9);
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [InlineData(0.0005)]
    [InlineData(0.00199)]
    public void Evaluate_SessionRangeWithTheSameDistances_StillSkippedAsStopTooLarge(double atrH4)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.BuyQuote()));
        Signal range = NightBuy(atrH4: atrH4) with { ZoneId = "RH-20260114", Kind = SignalKind.SessionRange };

        SkipRecord skip = h.SkipOf(range);

        Assert.Equal(SkipReason.StopTooLarge, skip.Reason);
        Assert.Equal(2.0 * atrH4 / 0.0001, skip.Limit, 6);
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [InlineData(1.0851, true)]
    [InlineData(1.08562, false)]
    public void Evaluate_StopBelowMinStopPips_StillSkippedAsStopTooSmall(double stop, bool placed)
    {
        // MinStopPips (12 pips for EURUSD by default) still applies to Overnight: a 12-pip stop is placed, a 6.8-pip stop refused.
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.BuyQuote()));

        PreTradeResult result = h.Evaluate(NightBuy() with { Stop = stop });

        Assert.Equal(placed, result.IsAllowed);
        if (!placed)
        {
            Assert.Equal(SkipReason.StopTooSmall, result.Skip!.Reason);
        }
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void Evaluate_ANewsWindowBeforeTheExpiry_ShortensIt()
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.BuyQuote()), "2026-01-14 11:00,USD,High,Retail Sales m/m\n");

        PlannedOrder order = h.OrderOf(NightBuy(ExecutionData.At(14, 11, 45)));

        Assert.Equal(ExecutionData.At(14, 10, 30), order.ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void Evaluate_InsideANewsWindow_SkippedAsNewsWindow()
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.BuyQuote()), "2026-01-14 10:15,USD,High,Retail Sales m/m\n");

        Assert.Equal(SkipReason.NewsWindow, h.SkipOf(NightBuy()).Reason);
    }

    public static IEnumerable<object[]> Inconsistent()
    {
        yield return new object[] { NightBuy(side: Side.Sell) };
        yield return new object[] { NightBuy() with { ExpiryUtc = null } };
        yield return new object[] { NightBuy() with { Level = null } };
        yield return new object[] { NightBuy() with { ExpiryUtc = new DateTime(2026, 1, 14, 11, 0, 0, DateTimeKind.Unspecified) } };
        yield return new object[] { NightBuy() with { Fvg = new Fvg(Side.Buy, 1.0860, 1.0850, ExecutionData.Now, ExecutionData.Now, ExecutionData.Now) } };
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [MemberData(nameof(Inconsistent))]
    public void Evaluate_OvernightSignalNotABuyWithLevelAndUtcExpiry_InvalidSignal(Signal signal)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.BuyQuote()));

        Assert.Equal(SkipReason.InvalidSignal, h.SkipOf(signal).Reason);
    }

    // ------------------------------------------------------------------ unclear placements (EXE-020, EXE-021, EXE-023)

    private static Bar NextBar(double high, double low)
    {
        double middle = (high + low) / 2.0;
        return ExecutionData.M15(ExecutionData.Now, middle, high, low, middle);
    }

    private static ClosedTradeInfo Trade(long positionId, string label)
    {
        return new ClosedTradeInfo(positionId, ExecutionData.Symbol, label, ExecutionData.At(14, 10, 5), ExecutionData.At(14, 10, 10), -100.0, 0.0, 0.0, 99900.0);
    }

    private static (ExecutionHarness Harness, OrderManager Orders) Unclear(DateTime? expiryUtc = null, IReadOnlyList<ClosedTradeInfo>? history = null)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.BuyQuote()));
        var port = new SwitchableBroker(h.Broker);
        if (history != null)
        {
            port.History = _ => history;
        }

        var orders = new OrderManager(port, h.Counter, h.Events);
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        Assert.Equal(PlaceOutcome.Unclear, orders.TryPlace(h.OrderOf(NightBuy(expiryUtc))).Outcome);
        Assert.Equal(1, orders.UnresolvedCount);
        h.Broker.AdvanceTo(NextClose);
        return (h, orders);
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [Trait("Req", "EXE-020")]
    [InlineData(1.0862)]
    [InlineData(1.08629)]
    public void Unclear_NextBarBelowTheSignalEntry_ResentOnceAsAStopOrderWithTheSameExpiry(double high)
    {
        (ExecutionHarness h, OrderManager orders) = Unclear();

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { NextBar(high, 1.0855) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal(2, h.Broker.Count(BrokerCall.Place));
        Assert.All(h.Broker.Requests, request => Assert.NotNull(request.StopOrder));
        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(1.0864, order.EntryPrice, 9);
        Assert.Equal(1.0824, order.StopLoss!.Value, 9);
        Assert.Equal(Expiry, order.ExpiryUtc);
        Assert.Equal(0, orders.UnresolvedCount);
        Assert.Empty(orders.ResolvePending(new[] { NextBar(high, 1.0855) }));
        Assert.Equal(2, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [Trait("Req", "EXE-020")]
    [InlineData(1.0863)]
    [InlineData(1.08635)]
    public void Unclear_NextBarReachedTheSignalEntry_NotResent(double high)
    {
        (ExecutionHarness h, OrderManager orders) = Unclear();

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { NextBar(high, 1.0855) }));

        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Contains("traded through", resolved.Detail, StringComparison.Ordinal);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());
        SkipRecord skip = h.Events.Skips[^1];
        Assert.Equal(SkipReason.UnclearOutcomeNotResent, skip.Reason);
        Assert.Equal(Id, skip.ZoneId);
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [Trait("Req", "EXE-020")]
    [InlineData(30, true)]
    [InlineData(29, false)]
    public void Unclear_OrderLifeLeftToTheSignalsOwnExpiry_AtLeast15MinutesForAResend(int minutesAfterTen, bool resent)
    {
        DateTime expiry = ExecutionData.Now.AddMinutes(minutesAfterTen);
        (ExecutionHarness h, OrderManager orders) = Unclear(expiry);

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));

        Assert.Equal(resent ? PlaceOutcome.Placed : PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Equal(resent ? 2 : 1, h.Broker.Count(BrokerCall.Place));
        if (!resent)
        {
            Assert.Equal("less than 15 minutes of order life left", resolved.Detail);
        }
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    [Trait("Req", "EXE-021")]
    public void Unclear_ClosedTradeWithTheLabelInTheHistory_PlacedAndNothingResent()
    {
        string label = ExecutionData.Label(Id);
        (ExecutionHarness h, OrderManager orders) = Unclear(history: new[] { Trade(901L, ExecutionData.Label("RH-20260114")), Trade(902L, label) });

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal("filled and closed as position 902", resolved.Detail);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [Trait("Req", "EXE-023")]
    [InlineData(false)]
    [InlineData(true)]
    public void Unclear_HistoryListWithAMissingEntry_UnclearOutcomeNotResent(bool labelledEntryToo)
    {
        ClosedTradeInfo[] history = labelledEntryToo ? new[] { Trade(902L, ExecutionData.Label(Id)), null! } : new ClosedTradeInfo[] { null! };
        (ExecutionHarness h, OrderManager orders) = Unclear(history: history);

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));

        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Equal("history not available, not resent", resolved.Detail);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Equal(SkipReason.UnclearOutcomeNotResent, h.Events.Skips[^1].Reason);
    }

    // ------------------------------------------------------------------ the instance scope (row BA)

    [Theory]
    [Trait("Req", "EXE-033")]
    [InlineData("ZFVG|v1|XAUUSD|ON-20260310", true)]
    [InlineData("ZFVG|v1|XAUUSD|RH-20260310", false)]
    [InlineData("ZFVG|v1|XAUUSD|BH-20260310-0000", false)]
    [InlineData("ZFVG|v1|XAUUSD|D-20260303-1600", false)]
    [InlineData("ZFVG|v1|XAUUSD|ON-2026031", false)]
    [InlineData("something else", false)]
    [InlineData(null, false)]
    public void InstanceScope_OvernightIdsBelongToTheOvernightInstanceOnly(string? label, bool overnight)
    {
        Assert.Equal(overnight, InstanceScope.Owns(true, label));
        Assert.Equal(!overnight, InstanceScope.Owns(false, label));
    }
}
