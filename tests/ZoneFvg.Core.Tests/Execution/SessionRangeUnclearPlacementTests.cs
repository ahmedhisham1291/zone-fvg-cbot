using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-024 ("EXE-021 to EXE-023 apply to this kind as to Breakout"), with a session range
/// placement whose outcome was unclear and whose setup is not void. EURUSD values of
/// <see cref="ExecutionData"/>: the buy RH-20260114 confirms at 10:00 UTC with the entry 1.0863
/// (order 1.0864, spread added), its expiry is 12:00 New York, 17:00 UTC; the next bar opens 10:00
/// and closes 10:15. EXE-020: resent once when no closed bar since the confirmation reached the
/// signal's entry 1.0863 and at least 15 minutes of order life are left, measured against this
/// kind's own expiry. EXE-021: a closed trade with the label means the order was applied and
/// closed: Placed, nothing resent. EXE-023: a history list with a missing entry counts as no list.
/// </summary>
public class SessionRangeUnclearPlacementTests
{
    private const string Id = "RH-20260114";

    private static readonly DateTime NextClose = ExecutionData.At(14, 10, 15);

    private static readonly DateTime Expiry = ExecutionData.At(14, 17, 0);

    private static Signal RangeBuy(DateTime? expiryUtc = null)
    {
        return new Signal(Id, Side.Buy, 1.0863, 1.0823, 1.0943, null, ExecutionData.Now, false, 0.004)
        {
            Kind = SignalKind.SessionRange,
            Level = 1.0863,
            ExpiryUtc = expiryUtc ?? Expiry,
        };
    }

    private static Bar NextBar(double high, double low)
    {
        double middle = (high + low) / 2.0;
        return ExecutionData.M15(ExecutionData.Now, middle, high, low, middle);
    }

    private static ClosedTradeInfo Trade(long positionId, string label)
    {
        return new ClosedTradeInfo(positionId, ExecutionData.Symbol, label, ExecutionData.At(14, 10, 5), ExecutionData.At(14, 10, 10), -100.0, 0.0, 0.0, 99900.0);
    }

    // The placement with an unclear outcome (not applied), through a port whose history can be replaced.
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
        Assert.Equal(PlaceOutcome.Unclear, orders.TryPlace(h.OrderOf(RangeBuy(expiryUtc))).Outcome);
        Assert.Equal(1, orders.UnresolvedCount);
        h.Broker.AdvanceTo(NextClose);
        return (h, orders);
    }

    [Theory]
    [Trait("Req", "EXE-024")]
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
        Assert.Equal(Expiry, h.Broker.Requests[1].StopOrder!.ExpiryUtc);
        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(1.0864, order.EntryPrice, 9);
        Assert.Equal(Expiry, order.ExpiryUtc);
        Assert.Equal(0, orders.UnresolvedCount);
        Assert.Empty(orders.ResolvePending(new[] { NextBar(high, 1.0855) }));
        Assert.Equal(2, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-024")]
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
        Assert.Equal(0, orders.UnresolvedCount);
    }

    [Theory]
    [Trait("Req", "EXE-024")]
    [Trait("Req", "EXE-020")]
    [InlineData(30, true)]
    [InlineData(29, false)]
    public void Unclear_OrderLifeLeftToTheSignalsOwnExpiry_AtLeast15MinutesForAResend(int minutesAfterTen, bool resent)
    {
        // Expiry 10:30 leaves 15 minutes at 10:15, expiry 10:29 leaves 14. EntryExpiryBars (8 bars) would give 12:00.
        DateTime expiry = ExecutionData.Now.AddMinutes(minutesAfterTen);
        (ExecutionHarness h, OrderManager orders) = Unclear(expiry);

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));

        Assert.Equal(resent ? PlaceOutcome.Placed : PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Equal(resent ? 2 : 1, h.Broker.Count(BrokerCall.Place));
        if (resent)
        {
            Assert.Equal(expiry, Assert.Single(h.Broker.GetPendingOrders()).ExpiryUtc);
        }
        else
        {
            Assert.Equal("less than 15 minutes of order life left", resolved.Detail);
            Assert.Equal(SkipReason.UnclearOutcomeNotResent, h.Events.Skips[^1].Reason);
        }
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    [Trait("Req", "EXE-021")]
    public void Unclear_ClosedTradeWithTheLabelInTheHistory_PlacedAndNothingResent()
    {
        string label = ExecutionData.Label(Id);
        (ExecutionHarness h, OrderManager orders) = Unclear(history: new[] { Trade(901L, ExecutionData.Label("RL-20260114")), Trade(902L, label) });

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal("filled and closed as position 902", resolved.Detail);
        Assert.Equal(label, resolved.Label);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(0, orders.UnresolvedCount);
    }

    [Theory]
    [Trait("Req", "EXE-024")]
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
        SkipRecord skip = h.Events.Skips[^1];
        Assert.Equal(SkipReason.UnclearOutcomeNotResent, skip.Reason);
        Assert.Equal(Id, skip.ZoneId);
        Assert.Equal(0, orders.UnresolvedCount);
    }
}
