using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-013: the fault-injection tests 1 to 11, 13 and 14 of spec 12.3 as FakeBroker scenarios,
/// with zero duplicate orders and zero bot positions without a stop loss (the WP06 global
/// assertions run at the end of each). Data: <see cref="RecoveryData"/>; the reference signal
/// confirms at Wednesday 2026-03-04 08:45 and becomes the buy limit
/// ZFVG|v1|USDJPY|D-20260303-1600, entry 100.675, stop 99.482, target 103.061, 41,000 units.
/// </summary>
public class Rec013FaultScenarios
{
    private static readonly DateTime A = RecoveryData.Activation;

    /// <summary>A running core that has placed the reference order at 08:45.</summary>
    private static BotHarness WithReferenceOrder(BotHarness? harness = null)
    {
        BotHarness h = harness ?? new BotHarness();
        h.Start(A);
        h.FeedTo(RecoveryData.Confirmation);
        PendingOrderInfo? order = h.ReferenceOrder();
        Assert.NotNull(order);
        Assert.Equal(RecoveryData.Entry, order.EntryPrice);
        Assert.Equal(RecoveryData.Stop, order.StopLoss);
        Assert.Equal(RecoveryData.Target, order.TakeProfit);
        Assert.Equal(RecoveryData.Volume, order.VolumeUnits);
        Assert.Equal(RecoveryData.Expiry, order.ExpiryUtc);
        return h;
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault01_CoreDestroyedWhileAPositionIsOpen_NewCoreAdoptsItAndSendsNoOrder()
    {
        BotHarness h = WithReferenceOrder();
        h.FillReferenceOrder(A.AddMinutes(50.0));
        PositionInfo? position = h.ReferencePosition();
        Assert.NotNull(position);
        int requests = h.Broker.WriteRequestCount;

        BotCore restarted = h.Start(A.AddMinutes(60.0));
        h.FeedTo(A.AddHours(3.0));

        Assert.Equal(BotState.Running, restarted.State);
        Assert.Equal(new[] { position.Id }, restarted.LastReport!.PositionsAdopted);
        Assert.Empty(restarted.LastReport.Strangers);
        Assert.Equal(requests, h.Broker.WriteRequestCount);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault02_CoreDestroyedWithALimitOrderPending_OrderKeptWhileValid()
    {
        BotHarness h = WithReferenceOrder();
        long orderId = h.ReferenceOrder()!.Id;

        BotCore restarted = h.Start(A.AddMinutes(50.0));

        Assert.Equal(new[] { orderId }, restarted.LastReport!.OrdersKept);
        Assert.Empty(restarted.LastReport.OrdersCancelled);
        Assert.Equal(1, h.Broker.WriteRequestCount);
        Assert.Equal(BotState.Running, restarted.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault03_OrderFillsWhileNoCoreExists_PositionAdoptedWithStopAndTargetVerified()
    {
        BotHarness h = WithReferenceOrder();
        h.FillReferenceOrder(A.AddMinutes(50.0));

        BotCore restarted = h.Start(A.AddMinutes(60.0));

        PositionInfo position = h.ReferencePosition()!;
        Assert.Equal(new[] { position.Id }, restarted.LastReport!.PositionsAdopted);
        Assert.Equal(RecoveryData.Stop, position.StopLoss);
        Assert.Equal(RecoveryData.Target, position.TakeProfit);
        Assert.Empty(restarted.LastReport.Protection);
        Assert.Empty(restarted.LastReport.WithoutTakeProfit);
        Assert.Equal(0, h.Count(BrokerCall.Modify));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault04_TradeHitsTargetWhileNoCoreExists_JournalBackfilledOnceAndZoneNotTradedAgain()
    {
        BotHarness h = WithReferenceOrder();
        h.FillReferenceOrder(A.AddMinutes(50.0));
        long positionId = h.ReferencePosition()!.Id;
        h.Broker.AdvanceTo(A.AddMinutes(90.0));
        h.Broker.Tick(RecoveryData.Symbol, 103.07, 103.08);
        Assert.Null(h.ReferencePosition());

        BotCore first = h.Start(A.AddHours(2.0));
        h.Start(A.AddHours(2.25));
        h.FeedTo(A.AddHours(6.0));

        string closed = "event=closed symbol=USDJPY position=" + positionId.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ";
        Assert.Equal(1, h.Journal.Count(TradeJournal.Category, closed));
        Assert.Equal(1, first.LastReport!.ReplaySignals);
        Assert.Empty(first.LastReport.ActiveZones);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "REC-013")]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    public void Fault05_DisconnectAcrossTheSignal_RecoveryRunsAndTheSignalFromTheGapIsNotExecuted(int minutes)
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.FeedTo(A.AddMinutes(30.0));
        DateTime cut = A.AddMinutes(44.5);
        h.Broker.AdvanceTo(cut);
        h.Broker.IsConnected = false;
        core.OnDisconnected();
        Assert.Equal(BotState.Recovering, core.State);

        // m2 confirms the signal at 08:45 while the connection is down; the adapter may still pass bars.
        DateTime back = cut.AddMinutes(minutes);
        h.FeedTo(back);
        h.Broker.IsConnected = true;
        core.OnConnected(h.History);
        h.FeedTo(back.AddHours(2.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(1, core.LastReport!.ReplaySignals);
        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.True(h.Journal.Count("skip", "zone=" + RecoveryData.ZoneId + " reason=SignalFromReplay") >= 1);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "REC-013")]
    [InlineData(BrokerFault.UnclearApplied, 1)]
    [InlineData(BrokerFault.UnclearNotApplied, 2)]
    public void Fault06_DisconnectAtTheMomentOfPlacement_ExactlyOneOrderAfterwards(BrokerFault fault, int placeRequests)
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.FeedTo(A.AddMinutes(30.0));
        h.Broker.Script(BrokerCall.Place, fault, "Timeout");
        h.FeedTo(RecoveryData.Confirmation);
        h.Broker.AdvanceTo(A.AddMinutes(46.0));
        core.OnDisconnected();
        h.Broker.AdvanceTo(A.AddMinutes(50.0));
        core.OnConnected(h.History);

        // The next M15 bar (09:00) resolves the unclear placement (EXE-009).
        h.FeedTo(A.AddMinutes(75.0));

        Assert.Equal(placeRequests, h.Count(BrokerCall.Place));
        Assert.Single(h.Broker.GetPendingOrders(), order => order.Label == RecoveryData.Label);
        Assert.Equal(BotState.Running, core.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault07_StopLossRemovedExternally_RestoredAtTheNextEventWithAnAlert()
    {
        BotHarness h = WithReferenceOrder();
        h.FillReferenceOrder(A.AddMinutes(50.0));
        PositionInfo position = h.ReferencePosition()!;
        h.Broker.ModifyPositionProtection(position.Id, null, position.TakeProfit);

        h.Core.OnTradeEvent();

        Assert.Equal(RecoveryData.Stop, h.ReferencePosition()!.StopLoss);
        Assert.Equal(1, h.Hooks.Count(AlertKind.StopLossMissing));
        Assert.Equal(1, h.Hooks.Count(AlertKind.StopLossRestored));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault08_TwoCoresOnOneSymbolAndOneBroker_AtMostOneOrder()
    {
        var h = new BotHarness();
        h.SkipTo(A);
        BotCore first = h.NewCore();
        BotCore second = h.NewCore();
        first.OnStart(h.History);
        second.OnStart(h.History);

        h.FeedTo(A.AddHours(2.0), second, first);

        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Journal.Count("skip", "reason=SymbolBusy"));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault09_CalendarReturnsGarbage_CacheUsedAndStaleCacheBlocksEntries()
    {
        string document = NewsTestData.OneEventDocument("Non-Farm Employment Change", "USD", "2026-03-06T08:30:00-05:00", "High");
        var http = new FakeTransport(NewsTestData.Ok(document));
        var h = new BotHarness(isBacktest: false, http: http, broker: BotHarness.NewBroker(RecoveryData.At(2, 9, 0)));

        BotCore fresh = h.Start(RecoveryData.At(2, 9, 0));
        Assert.Equal(BotState.Running, fresh.State);

        http.Respond = _ => NewsTestData.Ok("<html>maintenance</html>");
        BotCore cached = h.Start(RecoveryData.At(2, 11, 0));
        Assert.Equal(BotState.Running, cached.State);

        // 49 hours after the last good download the cache is no longer trusted (NewsMaxCacheHours 48).
        BotCore stale = h.Start(RecoveryData.At(4, 10, 0));

        Assert.Equal(BotState.Blocked, stale.State);
        Assert.Equal(3, http.Calls);
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.CalendarUnavailable));
        Assert.Equal(0, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault10_CoreAbsentAcrossFridayClose_PositionClosedAtOnceOnRecovery()
    {
        BotHarness h = WithReferenceOrder();
        h.FillReferenceOrder(A.AddMinutes(50.0));
        long positionId = h.ReferencePosition()!.Id;

        BotCore restarted = h.Start(RecoveryData.At(6, 21, 0));

        Assert.Null(h.ReferencePosition());
        Assert.Equal(1, h.Count(BrokerCall.Close));
        Assert.Equal(RecoveryData.At(6, 21, 0), h.Broker.Requests.Single(r => r.Call == BrokerCall.Close).AtUtc);
        Assert.Contains(positionId + " Closed (weekend close)", restarted.LastReport!.CatchUp);
        Assert.Contains(h.Hooks.Alerts, alert => alert.Kind == BotAlertKind.RecoveryReport && alert.Message.Contains("Closed (weekend close)", StringComparison.Ordinal));
        Assert.False(restarted.WeekendClosePending);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault11_ParameterChangeWhileATradeIsOpen_PositionKeptAsAStranger()
    {
        BotHarness h = WithReferenceOrder();
        h.FillReferenceOrder(A.AddMinutes(50.0));
        PositionInfo position = h.ReferencePosition()!;
        int requests = h.Broker.WriteRequestCount;
        BotParameters changed = RecoveryData.Parameters() with { Execution = new ExecutionParameters { MaxSpreadPips = 1.4 } };

        BotCore restarted = h.Start(A.AddMinutes(60.0), changed);

        string stranger = Assert.Single(restarted.LastReport!.Strangers);
        Assert.StartsWith(position.Id + " (parameter hash " + RecoveryData.Hash + " differs)", stranger, StringComparison.Ordinal);
        Assert.Empty(restarted.LastReport.PositionsAdopted);
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.Stranger));
        Assert.Equal(requests, h.Broker.WriteRequestCount);
        Assert.Equal(RecoveryData.Stop, h.ReferencePosition()!.StopLoss);
        Assert.Equal(RecoveryData.Target, h.ReferencePosition()!.TakeProfit);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault13_AHandlerThrowsRepeatedly_BotHaltsAfterTheLimitAndKeepsRunning()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.Port.PositionsFault = new InvalidOperationException("positions unavailable");

        for (int i = 1; i <= 5; i++)
        {
            h.Broker.AdvanceTo(A.AddMinutes(i));
            core.OnTradeEvent();
        }

        Assert.Equal(BotState.Running, core.State);
        h.Broker.AdvanceTo(A.AddMinutes(6.0));
        core.OnTradeEvent();

        Assert.Equal(BotState.Halted, core.State);
        Assert.Equal(new[] { HaltCause.ErrorLimit }, core.HaltCauses);
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.ErrorStorm));

        // The process continues: the timer still reaches the heartbeat hook in Halted.
        h.Timer(A.AddMinutes(7.0));
        Assert.Equal(BotState.Halted, h.Hooks.Heartbeats[^1].State);
        h.Port.PositionsFault = null;
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-013")]
    public void Fault14_TenRestartsInTenMinutes_AtMostOneCalendarFetchAndNoDuplicateOrder()
    {
        string document = NewsTestData.OneEventDocument("Non-Farm Employment Change", "USD", "2026-03-06T08:30:00-05:00", "High");
        var http = new FakeTransport(NewsTestData.Ok(document));
        BotHarness h = WithReferenceOrder(new BotHarness(isBacktest: false, http: http));

        for (int i = 0; i < 10; i++)
        {
            BotCore restarted = h.Start(A.AddMinutes(46.0 + i));
            Assert.Equal(BotState.Running, restarted.State);
            h.FeedTo(A.AddMinutes(46.5 + i));
        }

        Assert.Equal(1, http.Calls);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Single(h.Broker.GetPendingOrders(), order => order.Label == RecoveryData.Label);
        ScenarioAssert.Global(h.Broker);
    }
}
