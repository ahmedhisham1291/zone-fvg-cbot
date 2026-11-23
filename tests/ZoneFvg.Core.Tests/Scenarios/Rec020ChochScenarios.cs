using System.Globalization;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Recovery;
using Data = ZoneFvg.Core.Tests.ChochBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The Choch model through the core and a <see cref="FakeBroker"/> (EXE-027, OPS-011, REC-020): the
/// order of tail A, its fill, target, stop and expiry, restarts, strangers and the fault scenarios
/// 5, 6, 8 and 14 of spec 12.3, each ending with the WP06 global assertions (no duplicate order, no
/// bot position without a stop loss). Data and every expected value: <see cref="ChochBotData"/>.
/// </summary>
public class Rec020ChochScenarios
{
    private const string Symbol = RecoveryData.Symbol;
    private const string Label = RecoveryData.Label;

    private static BotHarness Started(DateTime atUtc)
    {
        BotHarness h = Data.Harness(atUtc);
        h.Start(atUtc, Data.Parameters());
        return h;
    }

    private static BotHarness WithOrder()
    {
        BotHarness h = Started(Data.Start);
        h.FeedTo(Data.Confirmation);
        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(Label, order.Label);
        return h;
    }

    private static void Fill(BotHarness h, DateTime atUtc)
    {
        h.Broker.AdvanceTo(atUtc);
        h.Broker.Tick(Symbol, 100.99, 101.0);
    }

    // ------------------------------------------------------------------ the order (EXE-027, OPS-011)

    [Fact]
    [Trait("Req", "EXE-027")]
    [Trait("Req", "OPS-011")]
    public void TailA_PlacedAsABuyLimitAt101_WithItsJournalLine()
    {
        BotHarness h = Started(Data.Start);
        h.FeedTo(Data.Confirmation.AddMinutes(-15.0));
        Assert.Empty(h.Broker.Requests);

        h.FeedTo(Data.Confirmation);

        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Null(request.StopOrder);
        var expected = new LimitOrderRequest(Symbol, Side.Buy, Data.Volume, Data.Entry, Data.Stop, Data.Target, Data.Expiry, Label, Data.Comment());
        Assert.Equal(expected, request.Order);
        Assert.Equal(Data.SignalLine, Assert.Single(h.Journal.Lines("signal")));
        Assert.Equal(BotState.Running, h.Core.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-027")]
    public void TailA_FilledOnTheRetest_ThenTheTarget()
    {
        BotHarness h = WithOrder();

        Fill(h, Data.Confirmation.AddMinutes(20.0));
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(Data.Stop, position.StopLoss);
        Assert.Equal(Data.Target, position.TakeProfit);
        h.FeedTo(Data.Confirmation.AddMinutes(30.0));
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(40.0));
        h.Broker.Tick(Symbol, 103.254, 103.264);
        h.Core.OnTradeEvent();
        h.FeedTo(Data.Confirmation.AddHours(3.0));

        Assert.Empty(h.Broker.GetPositions());
        Assert.True(Assert.Single(h.Broker.History).NetProfit > 0.0);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-027")]
    public void TailA_FilledOnTheRetest_ThenTheStop()
    {
        BotHarness h = WithOrder();

        Fill(h, Data.Confirmation.AddMinutes(20.0));
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(40.0));
        h.Broker.Tick(Symbol, 99.873, 99.883);
        h.Core.OnTradeEvent();
        h.FeedTo(Data.Confirmation.AddHours(3.0));

        Assert.Empty(h.Broker.GetPositions());
        Assert.True(Assert.Single(h.Broker.History).NetProfit < 0.0);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-027")]
    public void TailA_NoRetest_ExpiresAt1300_NoSecondOrder()
    {
        BotHarness h = WithOrder();

        h.FeedTo(Data.Expiry.AddMinutes(-15.0));
        Assert.Single(h.Broker.GetPendingOrders());
        h.FeedTo(Data.Expiry.AddHours(4.0));

        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "OPS-011")]
    public void DailyZoneCounts_NameTheWindowEndNoChoch()
    {
        DateTime nextDay = new(2026, 3, 4, 23, 0, 0, DateTimeKind.Utc);
        BotHarness h = Started(Data.Start);

        h.FeedTo(nextDay);

        Assert.Equal(
            new[] { JournalFormat.Time(nextDay) + " zones day=2026-03-04 created=1 signalled=1 no-choch=0 bias=0 invalid=0 expired=0 superseded=0" },
            h.Journal.Lines("zones"));
    }

    // ------------------------------------------------------------------ restarts and strangers (REC-020)

    [Fact]
    [Trait("Req", "REC-020")]
    public void RestartBetweenPlacementAndFill_OrderKept_NoSecondOrder()
    {
        BotHarness h = WithOrder();
        long id = Assert.Single(h.Broker.GetPendingOrders()).Id;

        BotCore restarted = h.Start(Data.Confirmation.AddMinutes(5.0), Data.Parameters());
        h.FeedTo(Data.Confirmation.AddHours(1.0));

        Assert.Equal(new[] { id }, restarted.LastReport!.OrdersKept);
        Assert.Empty(restarted.LastReport.Strangers);
        Assert.Equal(1, restarted.LastReport.ReplaySignals);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Equal(BotState.Running, restarted.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-020")]
    public void RestartWithTheOpenPosition_Adopted()
    {
        BotHarness h = WithOrder();
        Fill(h, Data.Confirmation.AddMinutes(20.0));
        long id = Assert.Single(h.Broker.GetPositions()).Id;

        BotCore restarted = h.Start(Data.Confirmation.AddMinutes(30.0), Data.Parameters());

        Assert.Equal(new[] { id }, restarted.LastReport!.PositionsAdopted);
        Assert.Empty(restarted.LastReport.Strangers);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-020")]
    public void FvgModelPosition_IsAStrangerInTheChochModel()
    {
        BotHarness h = Data.Harness(Data.Confirmation.AddMinutes(-60.0));
        long id = h.Broker.AddPosition(Symbol, Side.Buy, Data.Volume, Data.Entry, Data.Stop, Data.Target, Label, Data.Comment(RecoveryData.Hash));

        BotCore core = h.Start(Data.Confirmation.AddMinutes(-60.0), Data.Parameters());

        string stranger = Assert.Single(core.LastReport!.Strangers);
        Assert.StartsWith(id.ToString(CultureInfo.InvariantCulture) + " (parameter hash " + RecoveryData.Hash + " differs)", stranger, StringComparison.Ordinal);
        Assert.Empty(core.LastReport.PositionsAdopted);
        Assert.NotEqual(Data.Hash, RecoveryData.Hash);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-020")]
    public void ChochPosition_IsAStrangerInTheFvgModel()
    {
        BotHarness h = Data.Harness(Data.Confirmation.AddMinutes(-60.0));
        long id = h.Broker.AddPosition(Symbol, Side.Buy, Data.Volume, Data.Entry, Data.Stop, Data.Target, Label, Data.Comment());

        BotCore core = h.Start(Data.Confirmation.AddMinutes(-60.0), RecoveryData.Parameters());

        Assert.StartsWith(id.ToString(CultureInfo.InvariantCulture) + " (parameter hash " + Data.Hash + " differs)", Assert.Single(core.LastReport!.Strangers), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ fault 5

    [Theory]
    [Trait("Req", "REC-020")]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    public void Fault05_DisconnectAcrossTheChochBar_RecoveryRunsAndTheReplayedSignalIsNotExecuted(int minutes)
    {
        BotHarness h = Started(Data.Start);
        BotCore core = h.Core;
        h.FeedTo(Data.Confirmation.AddMinutes(-15.0));
        DateTime cut = Data.Confirmation.AddSeconds(-30.0);
        h.Broker.AdvanceTo(cut);
        h.Broker.IsConnected = false;
        core.OnDisconnected();
        Assert.Equal(BotState.Recovering, core.State);

        DateTime back = cut.AddMinutes(minutes);
        h.FeedTo(back);
        h.Broker.IsConnected = true;
        core.OnConnected(h.History);
        h.FeedTo(back.AddHours(2.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(1, core.LastReport!.ReplaySignals);
        Assert.True(h.Journal.Count("skip", "zone=" + RecoveryData.ZoneId + " reason=SignalFromReplay") >= 1);
        Assert.Equal(0, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ fault 6

    [Theory]
    [Trait("Req", "REC-020")]
    [InlineData(BrokerFault.UnclearApplied, 1)]
    [InlineData(BrokerFault.UnclearNotApplied, 2)]
    public void Fault06_DisconnectAtTheMomentOfPlacement_ExactlyOneOrderAfterwards(BrokerFault fault, int placeRequests)
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

        // The next M15 bar (11:15) resolves the unclear placement (EXE-009); its low 101.02 stays above the entry.
        h.FeedTo(Data.Confirmation.AddMinutes(30.0));

        Assert.Equal(placeRequests, h.Count(BrokerCall.Place));
        Assert.All(h.Broker.Requests.Where(r => r.Call == BrokerCall.Place), request => Assert.Null(request.StopOrder));
        Assert.Single(h.Broker.GetPendingOrders(), order => order.Label == Label);
        Assert.Equal(BotState.Running, core.State);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ fault 8

    [Fact]
    [Trait("Req", "REC-020")]
    public void Fault08_TwoCoresOnOneSymbolAndOneBroker_ExactlyOneLimitOrder()
    {
        BotHarness h = Data.Harness(Data.Start);
        h.SkipTo(Data.Start);
        BotCore first = h.NewCore(Data.Parameters());
        BotCore second = h.NewCore(Data.Parameters());
        first.OnStart(h.History);
        second.OnStart(h.History);

        h.FeedTo(Data.Confirmation.AddHours(1.0), second, first);

        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.NotNull(Assert.Single(h.Broker.Requests).Order);
        Assert.Equal(Label, Assert.Single(h.Broker.GetPendingOrders()).Label);
        Assert.Equal(1, h.Journal.Count("skip", "zone=" + RecoveryData.ZoneId + " reason=SymbolBusy"));
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ fault 14

    [Fact]
    [Trait("Req", "REC-020")]
    public void Fault14_TenRestartsInTenMinutesWithTheLimitOrderPending_OneCalendarFetchNoSecondOrder()
    {
        string document = NewsTestData.OneEventDocument("Non-Farm Employment Change", "USD", "2026-03-06T08:30:00-05:00", "High");
        var http = new FakeTransport(NewsTestData.Ok(document));
        DateTime start = Data.Confirmation.AddMinutes(-45.0);
        BotHarness h = Data.Harness(start, isBacktest: false, http: http);
        h.Start(start, Data.Parameters());
        h.FeedTo(Data.Confirmation);
        Assert.Equal(Label, Assert.Single(h.Broker.GetPendingOrders()).Label);

        for (int i = 0; i < 10; i++)
        {
            BotCore restarted = h.Start(Data.Confirmation.AddMinutes(1.0 + i), Data.Parameters());
            Assert.Equal(BotState.Running, restarted.State);
            h.FeedTo(Data.Confirmation.AddMinutes(1.5 + i));
        }

        Assert.Equal(1, http.Calls);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Single(h.Broker.GetPendingOrders(), order => order.Label == Label);
        ScenarioAssert.Global(h.Broker);
    }
}
