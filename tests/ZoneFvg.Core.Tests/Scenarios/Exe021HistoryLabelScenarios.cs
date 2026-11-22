using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-021, follow-ups of the review of WP29: with two closed trades of the order's label in the
/// history, the detail names the position of the first in the list; a closed trade whose label
/// differs only in letter case does not count, and the rules of EXE-020 apply. Data as in
/// <see cref="Exe021HistoryResendGuardScenarios"/>: the breakout buy confirms at 10:00, the next
/// bar closes at 10:15 and stays below the signal's entry 1.08630, so EXE-020 alone resends.
/// </summary>
public class Exe021HistoryLabelScenarios
{
    private const string Symbol = ExecutionData.Symbol;

    private static readonly DateTime NextClose = ExecutionData.At(14, 10, 15);

    private static Bar QuietBar()
    {
        return ExecutionData.M15(ExecutionData.Now, 1.08585, 1.0862, 1.0855, 1.08585);
    }

    private static ClosedTradeInfo Trade(long positionId, string label)
    {
        return new ClosedTradeInfo(positionId, Symbol, label, ExecutionData.At(14, 10, 5), ExecutionData.At(14, 10, 10), -100.0, 0.0, 0.0, 99900.0);
    }

    private static (ExecutionHarness Harness, OrderManager Orders) Unclear(IReadOnlyList<ClosedTradeInfo> history)
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.BuyQuote());
        var port = new SwitchableBroker(h.Broker) { History = _ => history };
        var orders = new OrderManager(port, h.Counter, h.Events);
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        Assert.Equal(PlaceOutcome.Unclear, orders.TryPlace(h.OrderOf(ExecutionData.BreakoutBuy())).Outcome);
        h.Broker.AdvanceTo(NextClose);
        return (h, orders);
    }

    [Theory]
    [Trait("Req", "EXE-021")]
    [InlineData(901L, 902L)]
    [InlineData(902L, 901L)]
    public void TwoClosedTradesWithTheLabel_DetailNamesTheFirstInTheList(long first, long second)
    {
        string label = ExecutionData.Label(ExecutionData.BreakoutBuyId);
        (ExecutionHarness h, OrderManager orders) = Unclear(new[] { Trade(first, label), Trade(second, label) });

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { QuietBar() }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal("filled and closed as position " + first.ToString(CultureInfo.InvariantCulture), resolved.Detail);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Events.Skips);
        Assert.Equal(0, orders.UnresolvedCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-021")]
    [InlineData("zfvg|v1|eurusd|bh-20260112-2200")]
    [InlineData("ZFVG|v1|EURUSD|bh-20260112-2200")]
    [InlineData("ZFVG|V1|EURUSD|BH-20260112-2200")]
    public void ClosedTradeWhoseLabelDiffersOnlyInLetterCase_DoesNotCount_ResentOnceAsByExe020(string label)
    {
        Assert.Equal(ExecutionData.Label(ExecutionData.BreakoutBuyId), label, ignoreCase: true);
        Assert.NotEqual(ExecutionData.Label(ExecutionData.BreakoutBuyId), label);
        (ExecutionHarness h, OrderManager orders) = Unclear(new[] { Trade(901, label) });

        PlaceResult resolved = Assert.Single(orders.ResolvePending(new[] { QuietBar() }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal("verified", resolved.Detail);
        Assert.Equal(2, h.Broker.Count(BrokerCall.Place));
        Assert.All(h.Broker.Requests, request => Assert.NotNull(request.StopOrder));
        Assert.Equal(ExecutionData.Label(ExecutionData.BreakoutBuyId), Assert.Single(h.Broker.GetPendingOrders()).Label);
        Assert.Empty(h.Events.Skips);
        Assert.Equal(0, orders.UnresolvedCount);
        ScenarioAssert.Global(h.Broker);
    }
}
