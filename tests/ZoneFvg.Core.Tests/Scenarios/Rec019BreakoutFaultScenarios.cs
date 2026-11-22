using System.Globalization;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using Data = ZoneFvg.Core.Tests.BreakoutRecoveryData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-019: the fault scenarios 5, 6, 8 and 14 of spec 12.3 through the core in Breakout mode, with
/// zero duplicate orders and zero bot positions without a stop loss (the WP06 global assertions
/// end each scenario), scenario 6 with the case of EXE-021, and recovery on the sell side. Data and
/// every expected value: <see cref="BreakoutRecoveryData"/>. The Wednesday buy confirms at 09:15
/// and becomes the buy stop ZFVG|v1|USDJPY|BH-20260303-0000: signal entry 100.36, order entry
/// 100.37, stop 99.96, target 101.16, expiry 11:15. Every M15 bar after it has a high of at least
/// 100.4, so an unclear placement with nothing at the broker is not resent (EXE-020).
/// </summary>
public class Rec019BreakoutFaultScenarios
{
    private const string Symbol = RecoveryData.Symbol;

    private static BotHarness Started(DateTime atUtc, bool sell = false, BarSet? bars = null)
    {
        BotHarness h = Data.Harness(atUtc, sell, bars);
        h.Start(atUtc, Data.Parameters());
        return h;
    }

    private static string Id(long id)
    {
        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static int WithLabel(BotHarness h, string label)
    {
        return h.Broker.GetPendingOrders().Count(order => order.Label == label) + h.Broker.GetPositions().Count(position => position.Label == label);
    }

    private static string ResolvedLine(BotHarness h)
    {
        return Assert.Single(h.Journal.Lines("order"), line => line.Contains(" event=resolved ", StringComparison.Ordinal));
    }

    // The bar set of the data, with the bar 09:15 to 09:30 replaced: its high 100.35 stays below the signal's
    // entry 100.36 (the ask reached 100.37 with the bid at 100.35) and its low 99.90 is below the stop 99.96.
    private static BarSet GapBars()
    {
        BarSet bars = Data.Bars();
        List<Bar> m15 = bars.M15
            .Select(bar => bar.OpenTimeUtc == Data.Confirmation ? BarBuilder.One(TimeFrame.M15, bar.OpenTimeUtc, 100.34, 100.35, 99.9, 99.95) : bar)
            .ToList();
        return new BarSet(bars.D1, bars.H4, m15);
    }

    // ------------------------------------------------------------------ fault 5

    [Theory]
    [Trait("Req", "REC-019")]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    public void Fault05_DisconnectAcrossTheConfirmingBar_RecoveryRunsAndTheBreakoutFromTheGapIsNotExecuted(int minutes)
    {
        BotHarness h = Started(Data.Start);
        BotCore core = h.Core;
        h.FeedTo(Data.Confirmation.AddMinutes(-15.0));
        DateTime cut = Data.Confirmation.AddSeconds(-30.0);
        h.Broker.AdvanceTo(cut);
        h.Broker.IsConnected = false;
        core.OnDisconnected();
        Assert.Equal(BotState.Recovering, core.State);

        // The bar 09:00 to 09:15 confirms the breakout while the connection is down; the adapter may still pass bars.
        DateTime back = cut.AddMinutes(minutes);
        h.FeedTo(back);
        h.Broker.IsConnected = true;
        core.OnConnected(h.History);

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(1, core.LastReport!.ReplaySignals);
        Assert.True(h.Journal.Count("skip", "zone=" + Data.BuyId + " reason=SignalFromReplay") >= 1);
        Assert.Equal(0, h.Count(BrokerCall.Place));

        // Every later bar of Wednesday closes above the reference high 100.3: the reference is finished.
        h.FeedTo(Data.Wednesday.AddHours(23.75));

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ fault 6

    [Theory]
    [Trait("Req", "REC-019")]
    [InlineData(BrokerFault.UnclearApplied, 1, "outcome=Placed detail=verified")]
    [InlineData(BrokerFault.UnclearNotApplied, 0, "outcome=Abandoned detail=price\\straded\\sthrough\\sthe\\sentry\\s100.36")]
    public void Fault06_DisconnectAtTheMomentTheStopOrderIsSent_NeverTwoOrdersForTheBreakoutId(BrokerFault fault, int atTheBroker, string resolved)
    {
        BotHarness h = Started(Data.Start);
        BotCore core = h.Core;
        h.FeedTo(Data.Confirmation.AddMinutes(-15.0));
        h.Broker.Script(BrokerCall.Place, fault, "Timeout");
        h.FeedTo(Data.Confirmation);
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(1.0));
        core.OnDisconnected();
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(5.0));
        core.OnConnected(h.History);

        // The next M15 bar (09:30) resolves the unclear placement (EXE-009, EXE-020, EXE-021).
        h.FeedTo(Data.Confirmation.AddMinutes(30.0));

        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.All(h.Broker.Requests, request => Assert.NotNull(request.StopOrder));
        Assert.Equal(atTheBroker, WithLabel(h, Data.BuyLabel));
        Assert.Contains(" " + resolved, ResolvedLine(h), StringComparison.Ordinal);
        Assert.Equal(BotState.Running, core.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-019")]
    public void Fault06_UnclearPlacementAppliedFilledAndStoppedOutBeforeTheNextBar_NotSentAgainAndResolvedAsClosed()
    {
        BotHarness h = Started(Data.Start, bars: GapBars());
        BotCore core = h.Core;
        h.FeedTo(Data.Confirmation.AddMinutes(-15.0));
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearApplied, "Timeout");
        h.FeedTo(Data.Confirmation);
        Assert.Equal(Data.BuyLabel, Assert.Single(h.Broker.GetPendingOrders()).Label);
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(1.0));
        core.OnDisconnected();

        // While the connection is down the ask reaches the entry 100.37 with the bid at 100.35 (a spread of
        // two pips), and then the bid reaches the stop 99.96.
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(2.0));
        h.Broker.Tick(Symbol, 100.35, 100.37);
        Assert.Equal(Data.BuyLabel, Assert.Single(h.Broker.GetPositions()).Label);
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(3.0));
        h.Broker.Tick(Symbol, 99.96, 99.97);
        ClosedTradeInfo trade = Assert.Single(h.Broker.History);
        Assert.Equal(Data.BuyLabel, trade.Label);
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(5.0));
        core.OnConnected(h.History);

        // The bar 09:15 to 09:30 does not reach the signal's entry: only the history stops a second order.
        h.FeedTo(Data.Confirmation.AddMinutes(45.0));

        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Place);
        Assert.Equal(0, WithLabel(h, Data.BuyLabel));
        Assert.Contains(" outcome=Placed detail=filled\\sand\\sclosed\\sas\\sposition\\s" + Id(trade.PositionId), ResolvedLine(h), StringComparison.Ordinal);
        Assert.Equal(0, h.Journal.Count("skip", "reason=UnclearOutcomeNotResent"));
        Assert.Equal(BotState.Running, core.State);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ fault 8

    [Fact]
    [Trait("Req", "REC-019")]
    public void Fault08_TwoCoresOnOneSymbolAndOneBroker_ExactlyOneStopOrder()
    {
        BotHarness h = Data.Harness(Data.Start);
        h.SkipTo(Data.Start);
        BotCore first = h.NewCore(Data.Parameters());
        BotCore second = h.NewCore(Data.Parameters());
        first.OnStart(h.History);
        second.OnStart(h.History);

        h.FeedTo(Data.Wednesday.AddHours(11.0), second, first);

        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.NotNull(Assert.Single(h.Broker.Requests).StopOrder);
        Assert.Equal(Data.BuyLabel, Assert.Single(h.Broker.GetPendingOrders()).Label);
        Assert.Equal(1, h.Journal.Count("skip", "zone=" + Data.BuyId + " reason=SymbolBusy"));
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ fault 14

    [Fact]
    [Trait("Req", "REC-019")]
    public void Fault14_TenRestartsInTenMinutesWithTheStopOrderPending_OneCalendarFetchOrderKeptNoSecondOrder()
    {
        string document = NewsTestData.OneEventDocument("Non-Farm Employment Change", "USD", "2026-03-06T08:30:00-05:00", "High");
        var http = new FakeTransport(NewsTestData.Ok(document));
        // The first start is 45 minutes before the confirmation, as in the REC-013 scenario: every restart
        // then lies within the 60-minute minimum between calendar fetches at a start (NWS-006).
        DateTime start = Data.Confirmation.AddMinutes(-45.0);
        var broker = new FakeBroker(start, ExecutionData.Account(), Data.Quote());
        broker.MarginPerUnit = 0.04;
        var h = new BotHarness(Data.Bars(), broker, isBacktest: false, http: http);
        h.Start(start, Data.Parameters());
        h.FeedTo(Data.Confirmation);
        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(Data.BuyLabel, order.Label);

        for (int i = 0; i < 10; i++)
        {
            BotCore restarted = h.Start(Data.Confirmation.AddMinutes(1.0 + i), Data.Parameters());
            Assert.Equal(BotState.Running, restarted.State);
            Assert.Equal(new[] { order.Id }, restarted.LastReport!.OrdersKept);
            Assert.Empty(restarted.LastReport.OrdersCancelled);
            h.FeedTo(Data.Confirmation.AddMinutes(1.5 + i));
        }

        Assert.Equal(1, http.Calls);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Equal(order.Id, Assert.Single(h.Broker.GetPendingOrders(), pending => pending.Label == Data.BuyLabel).Id);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ sell side of recovery

    [Fact]
    [Trait("Req", "REC-019")]
    public void Restart_PendingSellStopWithAKnownBlId_KeptNoCancelNoNewOrder()
    {
        BotHarness h = Started(Data.Start, sell: true);
        h.FeedTo(Data.Confirmation.AddMinutes(15.0));
        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal((Data.SellLabel, Side.Sell), (order.Label, order.Side));

        BotCore restarted = h.Start(Data.Confirmation.AddMinutes(20.0), Data.Parameters());

        Assert.Equal(1, restarted.LastReport!.ReplaySignals);
        Assert.Equal(new[] { order.Id }, restarted.LastReport.OrdersKept);
        Assert.Empty(restarted.LastReport.OrdersCancelled);
        Assert.Empty(restarted.LastReport.Strangers);
        Assert.Equal(BotState.Running, restarted.State);
        Assert.Single(h.Broker.Requests);
        Assert.Equal(0, h.Count(BrokerCall.Cancel));

        h.FeedTo(Data.Wednesday.AddHours(11.0));
        Assert.Single(h.Broker.Requests);
        Assert.Equal(order.Id, Assert.Single(h.Broker.GetPendingOrders()).Id);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "REC-019")]
    [InlineData(false)]
    [InlineData(true)]
    public void Restart_SellPositionWithAKnownBlId_AdoptedStopAndTargetChecked(bool stopRemoved)
    {
        BotHarness h = Started(Data.Start, sell: true);
        h.FeedTo(Data.Confirmation);

        // The bid reaches the entry 99.64: the sell stop fills.
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(5.0));
        h.Broker.Tick(Symbol, 99.64, 99.65);
        h.Core.OnTradeEvent();
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal((Data.SellLabel, Side.Sell), (position.Label, position.Side));
        h.FeedTo(Data.Confirmation.AddMinutes(15.0));
        if (stopRemoved)
        {
            h.Broker.ModifyPositionProtection(position.Id, null, Data.SellTarget);
        }

        int writes = h.Broker.WriteRequestCount;

        BotCore restarted = h.Start(Data.Confirmation.AddMinutes(20.0), Data.Parameters());

        Assert.Equal(new[] { position.Id }, restarted.LastReport!.PositionsAdopted);
        Assert.Empty(restarted.LastReport.Strangers);
        Assert.Empty(restarted.LastReport.WithoutTakeProfit);
        if (stopRemoved)
        {
            Assert.Equal(new[] { Id(position.Id) + " StopRestored (stop 100.05)" }, restarted.LastReport.Protection);
            Assert.Equal(writes + 1, h.Broker.WriteRequestCount);
        }
        else
        {
            Assert.Empty(restarted.LastReport.Protection);
            Assert.Equal(writes, h.Broker.WriteRequestCount);
        }

        PositionInfo adopted = Assert.Single(h.Broker.GetPositions());
        Assert.Equal((Side.Sell, Data.SellEntry, Data.SellStop, Data.SellTarget), (adopted.Side, adopted.EntryPrice, adopted.StopLoss!.Value, adopted.TakeProfit!.Value));
        Assert.Equal(BotState.Running, restarted.State);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }
}
