using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-023: when the history read of EXE-021 returns a list with a missing entry, the list counts
/// as no history list: nothing is resent, the placement is abandoned and recorded as
/// UnclearOutcomeNotResent with "history not available, not resent", also when another entry of
/// the list carries the order's label. Data as in <see cref="Exe021HistoryResendGuardScenarios"/>:
/// <see cref="ExecutionData"/>, signals confirmed at 10:00, the next bar closes at 10:15. The
/// broker's history is replaced through <see cref="SwitchableBroker.History"/>.
/// </summary>
public class Exe023HistoryMissingEntryScenarios
{
    private const string Symbol = ExecutionData.Symbol;
    private const string Detail = "history not available, not resent";

    private static readonly DateTime NextClose = ExecutionData.At(14, 10, 15);

    private static Bar NextBar(double high, double low)
    {
        double middle = (high + low) / 2.0;
        return ExecutionData.M15(ExecutionData.Now, middle, high, low, middle);
    }

    // A bar after the confirmation that does not reach the entry: without the history rule the order would be resent.
    private static Bar QuietBar(bool limit)
    {
        return limit ? NextBar(1.1008, 1.10001) : NextBar(1.0862, 1.0855);
    }

    private static ClosedTradeInfo Trade(long positionId, string label)
    {
        return new ClosedTradeInfo(positionId, Symbol, label, ExecutionData.At(14, 10, 5), ExecutionData.At(14, 10, 10), -100.0, 0.0, 0.0, 99900.0);
    }

    // The placement with an unclear outcome, made through a port whose history is the given list.
    private static (ExecutionHarness Harness, OrderManager Orders, PlannedOrder Order, List<DateTime> Reads) Unclear(bool limit, IReadOnlyList<ClosedTradeInfo> history)
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.BuyQuote());
        var reads = new List<DateTime>();
        var port = new SwitchableBroker(h.Broker)
        {
            History = since =>
            {
                reads.Add(since);
                return history;
            },
        };
        var orders = new OrderManager(port, h.Counter, h.Events);
        PlannedOrder order = h.OrderOf(limit ? ExecutionData.Buy() : ExecutionData.BreakoutBuy());
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        Assert.Equal(PlaceOutcome.Unclear, orders.TryPlace(order).Outcome);
        return (h, orders, order, reads);
    }

    private static void AssertAbandoned(ExecutionHarness h, OrderManager orders, PlannedOrder order, PlaceResult resolved, DateTime now, bool limit)
    {
        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Equal(Detail, resolved.Detail);
        Assert.Equal(order.Label, resolved.Label);
        Assert.Null(resolved.OrderId);
        SkipRecord skip = Assert.Single(h.Events.Skips);
        Assert.Equal(SkipReason.UnclearOutcomeNotResent, skip.Reason);
        Assert.Equal(Detail, skip.Detail);
        Assert.Equal(order.Signal.ZoneId, skip.ZoneId);
        Assert.Equal(Symbol, skip.Symbol);
        Assert.Equal(now, skip.AtUtc);
        Assert.Empty(h.Events.Alerts);

        // Nothing sent: the one request is the first placement; nothing at the broker; nothing unresolved.
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Single(h.Broker.Requests);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(0, orders.UnresolvedCount);
        Assert.False(orders.HasUnresolved(Symbol));

        // A later bar resolves nothing and sends nothing.
        Assert.Empty(orders.ResolvePending(new[] { QuietBar(limit) }));
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-023")]
    [InlineData(false, 0L)]
    [InlineData(true, 0L)]
    [InlineData(false, -1L)]
    [InlineData(true, -1L)]
    public void HistoryWithANullEntryAndNoTradeWithTheLabel_Abandoned_RecordedAsUnclearOutcomeNotResent_NothingSent(bool limit, long ticksFromTheNextClose)
    {
        // The list holds a null entry and a trade of another breakout. At 10:15 EXE-020 or EXE-009 alone would
        // resend; one tick before it (no bar closed yet) the placement would be kept.
        ClosedTradeInfo other = Trade(901, ExecutionData.Label("BH-20260111-2200"));
        (ExecutionHarness h, OrderManager orders, PlannedOrder order, List<DateTime> reads) = Unclear(limit, new[] { other, null! });
        DateTime now = NextClose.AddTicks(ticksFromTheNextClose);
        h.Broker.AdvanceTo(now);

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { QuietBar(limit) }));

        AssertAbandoned(h, orders, order, resolved, now, limit);
        Assert.Equal(new[] { order.Signal.ConfirmationCloseUtc }, reads);
        Assert.All(h.Broker.Requests, request => Assert.Equal(limit, request.StopOrder is null));
    }

    [Theory]
    [Trait("Req", "EXE-023")]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryWithOnlyANullEntry_Abandoned_RecordedAsUnclearOutcomeNotResent_NothingSent(bool limit)
    {
        (ExecutionHarness h, OrderManager orders, PlannedOrder order, _) = Unclear(limit, new ClosedTradeInfo[] { null! });
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { QuietBar(limit) }));

        AssertAbandoned(h, orders, order, resolved, NextClose, limit);
    }

    [Theory]
    [Trait("Req", "EXE-023")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HistoryWithANullEntryAndATradeWithTheLabel_AbandonedAsWithoutAList(bool limit, bool nullFirst)
    {
        // Another entry carries the order's label: the list still counts as no history list (fail closed).
        string label = ExecutionData.Label(limit ? ExecutionData.BuyZone : ExecutionData.BreakoutBuyId);
        ClosedTradeInfo labelled = Trade(902, label);
        ClosedTradeInfo[] history = nullFirst ? new[] { null!, labelled } : new[] { labelled, null! };
        (ExecutionHarness h, OrderManager orders, PlannedOrder order, _) = Unclear(limit, history);
        Assert.Equal(label, order.Label);
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { QuietBar(limit) }));

        AssertAbandoned(h, orders, order, resolved, NextClose, limit);
        Assert.DoesNotContain(902L.ToString(CultureInfo.InvariantCulture), resolved.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-023")]
    public void HistoryWithoutANullEntry_TheRulesOfExe021AndExe020ApplyAsBefore()
    {
        // The same list without the null entry: the trade of another breakout does not block, and the order is resent once.
        ClosedTradeInfo other = Trade(901, ExecutionData.Label("BH-20260111-2200"));
        (ExecutionHarness h, OrderManager orders, _, _) = Unclear(false, new[] { other });
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { QuietBar(false) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal("verified", resolved.Detail);
        Assert.Equal(2, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Events.Skips);
        Assert.Equal(0, orders.UnresolvedCount);
        ScenarioAssert.Global(h.Broker);
    }
}
