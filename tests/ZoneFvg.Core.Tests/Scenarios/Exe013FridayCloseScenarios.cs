using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-013: at or after FridayCloseUtc (20:00) on a Friday every bot position on the symbol is
/// closed and every bot pending entry order cancelled. 2026-01-15 is a Thursday, 2026-01-16 a
/// Friday. The EURUSD instance holds a bot position and a bot order; a manual EURUSD position and
/// a GBPUSD bot position belong to others and stay.
/// </summary>
public class Exe013FridayCloseScenarios
{
    private static (ExecutionHarness Harness, long Position, long Order) Setup(DateTime now)
    {
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(now: now));
        h.Broker.SetSymbol(RiskData.GbpUsd);
        long position = h.Broker.AddPosition("EURUSD", Side.Buy, 100000.0, 1.1, 1.098, 1.104, ExecutionData.Label("D-20260116-0400"), "c");
        long order = h.Broker.AddOrder("EURUSD", Side.Sell, 100000.0, 1.11, 1.112, 1.106, null, ExecutionData.Label("S-20260116-0400"), "c");
        h.Broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.1, 1.09, 1.11, "manual", "c");
        h.Broker.AddPosition("GBPUSD", Side.Buy, 1000.0, 1.25, 1.248, 1.254, "ZFVG|v1|GBPUSD|D-20260116-0400", "c");
        return (h, position, order);
    }

    [Fact]
    [Trait("Req", "EXE-013")]
    public void FridayAt2000_ClosesAndCancelsTheSymbolsBotTrades()
    {
        (ExecutionHarness h, long position, long order) = Setup(ExecutionData.At(16, 20, 0));

        IReadOnlyList<TradeAction> actions = h.Trades.FridayClose(ExecutionData.At(16, 20, 0));

        Assert.Equal(
            new[] { new TradeAction(order, TradeActionOutcome.Cancelled, "Friday close"), new TradeAction(position, TradeActionOutcome.Closed, "Friday close") },
            actions);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(new[] { "manual", "ZFVG|v1|GBPUSD|D-20260116-0400" }, h.Broker.GetPositions().Select(p => p.Label));
        Assert.Equal(2, h.Broker.WriteRequestCount);
        Assert.Equal(2, h.Counter.Evaluate(ExecutionData.At(16, 20, 0)).LastHourCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-013")]
    [InlineData(15, 20, 0)] // Thursday 20:00
    [InlineData(16, 19, 59)] // Friday 19:59
    [InlineData(17, 10, 0)] // Saturday
    public void NotFridayAfter2000_DoesNothing(int day, int hour, int minute)
    {
        (ExecutionHarness h, _, _) = Setup(ExecutionData.At(day, hour, minute));

        Assert.Empty(h.Trades.FridayClose(ExecutionData.At(day, hour, minute)));

        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-013")]
    public void OneTickBefore2000_DoesNothing()
    {
        (ExecutionHarness h, _, _) = Setup(ExecutionData.At(16, 19, 59));

        Assert.Empty(h.Trades.FridayClose(ExecutionData.At(16, 20, 0).AddTicks(-1)));
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-013")]
    public void RestartAfterTheTime_StillCloses()
    {
        // A new instance started at 21:30 on Friday, after the close time: it closes what the broker holds.
        (ExecutionHarness h, long position, _) = Setup(ExecutionData.At(16, 21, 30));
        var restarted = new TradeManager("EURUSD", new ExecutionParameters(), h.Broker, new RequestCounter(ExecutionData.RiskDefaults), h.Events);

        IReadOnlyList<TradeAction> actions = restarted.FridayClose(ExecutionData.At(16, 21, 30));

        Assert.Equal(2, actions.Count);
        Assert.Contains(actions, a => a.Id == position && a.Outcome == TradeActionOutcome.Closed);
        Assert.Empty(restarted.FridayClose(ExecutionData.At(16, 21, 31)));
        Assert.Equal(2, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-013")]
    public void FailedCloseAndCancel_AreAlerted()
    {
        (ExecutionHarness h, _, _) = Setup(ExecutionData.At(16, 20, 0));
        h.Broker.Script(BrokerCall.Cancel, BrokerFault.Rejected, "MarketClosed");
        h.Broker.Script(BrokerCall.Close, BrokerFault.UnclearNotApplied);

        IReadOnlyList<TradeAction> actions = h.Trades.FridayClose(ExecutionData.At(16, 20, 0));

        Assert.Equal(new[] { TradeActionOutcome.CancelFailed, TradeActionOutcome.CloseFailed }, actions.Select(a => a.Outcome));
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.CancelFailed));
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.CloseFailed));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-013")]
    public void ServerTimeNotUtc_Throws()
    {
        var h = new ExecutionHarness();

        Assert.Throws<ArgumentException>(() => h.Trades.FridayClose(new DateTime(2026, 1, 16, 20, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    [Trait("Req", "EXE-013")]
    public void CancelEntryOrders_CancelsOnlyTheSymbolsBotOrders()
    {
        var h = new ExecutionHarness();
        h.Broker.SetSymbol(RiskData.GbpUsd);
        long mine = h.Broker.AddOrder("EURUSD", Side.Buy, 1000.0, 1.09, 1.088, 1.094, null, ExecutionData.Label("D-20260113-0800"), "c");
        h.Broker.AddOrder("EURUSD", Side.Buy, 1000.0, 1.09, 1.088, 1.094, null, "manual", "c");
        h.Broker.AddOrder("GBPUSD", Side.Buy, 1000.0, 1.24, 1.238, 1.244, null, "ZFVG|v1|GBPUSD|D-20260113-0800", "c");

        IReadOnlyList<TradeAction> actions = h.Trades.CancelEntryOrders("daily budget");

        Assert.Equal(new TradeAction(mine, TradeActionOutcome.Cancelled, "daily budget"), Assert.Single(actions));
        Assert.Equal(2, h.Broker.GetPendingOrders().Count);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-013")]
    public void Construction_RejectsAnEmptySymbolAndNulls()
    {
        var h = new ExecutionHarness();
        var counter = new RequestCounter(ExecutionData.RiskDefaults);

        Assert.Throws<ArgumentException>(() => new TradeManager(string.Empty, new ExecutionParameters(), h.Broker, counter, h.Events));
        Assert.Throws<ArgumentNullException>(() => new TradeManager("EURUSD", null!, h.Broker, counter, h.Events));
        Assert.Throws<ArgumentNullException>(() => new TradeManager("EURUSD", new ExecutionParameters(), null!, counter, h.Events));
        Assert.Throws<ArgumentNullException>(() => new TradeManager("EURUSD", new ExecutionParameters(), h.Broker, null!, h.Events));
        Assert.Throws<ArgumentNullException>(() => new TradeManager("EURUSD", new ExecutionParameters(), h.Broker, counter, null!));
    }
}
