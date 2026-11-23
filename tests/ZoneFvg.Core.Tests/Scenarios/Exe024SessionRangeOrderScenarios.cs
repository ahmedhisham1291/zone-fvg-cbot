using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using Data = ZoneFvg.Core.Tests.SessionRangeBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-024 through <see cref="BotCore"/> and a <see cref="FakeBroker"/>: the session range order
/// fills and ends at its target or stop, expires at 12:00 New York (on a Friday at the cap), is
/// shortened by a news window, is cancelled when the setup becomes void, and is not placed when
/// price has already reached the entry or the symbol is busy; across a restart it is adopted and
/// the void still cancels it. Data and every expected value: <see cref="SessionRangeBotData"/>
/// (winter day 2026-03-04: placement 08:00 UTC, expiry 17:00 UTC; the void bar opens 10:00 UTC,
/// 05:00 New York, and reaches 99.99, below the range low 100.0).
/// </summary>
public class Exe024SessionRangeOrderScenarios
{
    private static readonly DateTime Start = Data.Utc(2026, 3, 4, 6, 0);
    private static readonly DateTime Placement = Data.Utc(2026, 3, 4, 8, 0);
    private static readonly DateTime VoidOpen = Data.Utc(2026, 3, 4, 10, 0);
    private static readonly DateTime VoidClose = Data.Utc(2026, 3, 4, 10, 15);
    private static readonly string BuyLabel = Data.Label(Data.BuyId(Data.Winter));

    private static BotHarness Placed(BarSet? bars = null, BotParameters? parameters = null)
    {
        BotHarness h = Data.Started(Start, bars ?? Data.Bars(Data.Winter), parameters: parameters);
        h.FeedTo(Placement);
        Assert.Equal(BuyLabel, Assert.Single(h.Broker.GetPendingOrders()).Label);
        return h;
    }

    // The ask reaches the buy stop 100.41.
    private static void Fill(BotHarness h, DateTime atUtc)
    {
        h.Broker.AdvanceTo(atUtc);
        h.Broker.Tick(Data.Symbol, 100.4, 100.41);
        h.Core.OnTradeEvent();
    }

    private static PositionInfo OnlyPosition(BotHarness h)
    {
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(BuyLabel, position.Label);
        Assert.Equal(Data.Stop, position.StopLoss!.Value, 9);
        Assert.Equal(Data.Target, position.TakeProfit!.Value, 9);
        return position;
    }

    private static string TradeLine(BotHarness h)
    {
        return Assert.Single(h.Journal.Lines("trade"), line => line.Contains(" trade symbol=", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Fill_ThenTarget_ClosedAtTheTargetAndJournaled()
    {
        BotHarness h = Placed(parameters: Data.Parameters() with { WriteTradeLog = true });
        h.Broker.ReportExitReasons = true;

        Fill(h, Data.Utc(2026, 3, 4, 8, 20));
        OnlyPosition(h);
        Assert.Empty(h.Broker.GetPendingOrders());
        h.FeedTo(Data.Utc(2026, 3, 4, 9, 0));
        OnlyPosition(h);
        h.Broker.AdvanceTo(Data.Utc(2026, 3, 4, 9, 5));
        h.Broker.Tick(Data.Symbol, 101.2, 101.21);
        h.Core.OnTradeEvent();

        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(Data.Target, Assert.Single(h.Broker.History).ClosingPrice!.Value, 9);
        string trade = TradeLine(h);
        Assert.Contains(" label=ZFVG|v1|USDJPY|RH-20260304 side=Buy ", trade, StringComparison.Ordinal);
        Assert.Contains(" exit=TakeProfit ", trade, StringComparison.Ordinal);
        string tradeLog = Assert.Single(h.Journal.Lines("tradelog"));
        Assert.Contains(" row=USDJPY,RH-20260304,", tradeLog, StringComparison.Ordinal);
        Assert.Single(h.Broker.Requests);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Fill_ThenStop_ClosedAtTheStop()
    {
        BotHarness h = Placed();
        h.Broker.ReportExitReasons = true;

        Fill(h, Data.Utc(2026, 3, 4, 8, 20));
        h.Broker.AdvanceTo(Data.Utc(2026, 3, 4, 8, 40));
        h.Broker.Tick(Data.Symbol, 100.0, 100.01);
        h.Core.OnTradeEvent();

        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(TradeExitReason.StopLoss, Assert.Single(h.Broker.History).ExitReason);
        Assert.Contains(" exit=StopLoss ", TradeLine(h), StringComparison.Ordinal);
        Assert.Single(h.Broker.Requests);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void NoFill_TheOrderExpiresAt1200NewYork_NoCancelRequest()
    {
        BotHarness h = Placed();

        h.FeedTo(Data.Utc(2026, 3, 4, 16, 45));
        Assert.Single(h.Broker.GetPendingOrders());

        h.FeedTo(Data.Utc(2026, 3, 4, 17, 0));

        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Single(h.Broker.Requests);
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Friday_TheOrderEndsAtTheFridayCap()
    {
        BotHarness h = Data.Started(Data.Utc(2026, 3, 13, 5, 0), Data.Bars(Data.Friday));

        h.FeedTo(Data.Utc(2026, 3, 13, 7, 0));

        // 12:00 New York is 16:00 UTC; FridayNoEntryUtc 12:00 UTC comes first.
        StopOrderRequest order = Assert.Single(h.Broker.Requests).StopOrder!;
        Assert.Equal(Data.Utc(2026, 3, 13, 12, 0), order.ExpiryUtc);
        Assert.Equal(Data.Label(Data.BuyId(Data.Friday)), order.Label);
        h.FeedTo(Data.Utc(2026, 3, 13, 12, 0));
        Assert.Empty(h.Broker.GetPendingOrders());
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void NewsWindow_BeforeTheEndOfTheOrder_TheOrderEndsAtTheWindowStart()
    {
        // 2026-03-11: the USD event at 13:30 UTC makes a window from 13:00; 12:00 New York is 16:00 UTC.
        BotHarness h = Data.Started(Data.Utc(2026, 3, 11, 5, 0), Data.Bars(Data.NewsDay));

        h.FeedTo(Data.Utc(2026, 3, 11, 7, 0));

        StopOrderRequest order = Assert.Single(h.Broker.Requests).StopOrder!;
        Assert.Equal(Data.Utc(2026, 3, 11, 13, 0), order.ExpiryUtc);
        Assert.Equal(Data.Label(Data.BuyId(Data.NewsDay)), order.Label);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Void_WhileTheOrderIsPending_TheOrderIsCancelledOnce()
    {
        BotHarness h = Placed(Data.Bars(false, VoidOpen, Data.Winter));
        long order = Assert.Single(h.Broker.GetPendingOrders()).Id;

        h.FeedTo(VoidOpen);
        Assert.Equal(0, h.Count(BrokerCall.Cancel));

        h.FeedTo(VoidClose);

        BrokerRequest cancel = Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Cancel);
        Assert.Equal(order, cancel.TargetId);
        Assert.Equal(VoidClose, cancel.AtUtc);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Journal.Count("range", "range event=void id=RH-20260304"));
        Assert.Equal(1, h.Journal.Count("action", "duty=range-void "));

        h.FeedTo(Data.Utc(2026, 3, 4, 16, 0));
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
        Assert.Equal(2, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Void_AfterTheFill_NothingPendingNoRequestThePositionStays()
    {
        BotHarness h = Placed(Data.Bars(false, VoidOpen, Data.Winter));
        Fill(h, Data.Utc(2026, 3, 4, 8, 20));

        h.FeedTo(Data.Utc(2026, 3, 4, 11, 0));

        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Single(h.Broker.Requests);
        OnlyPosition(h);
        Assert.Empty(h.Journal.Lines("range"));
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Void_AfterTheOrderExpired_NoRequest()
    {
        // The void bar opens 11:00 New York, 16:00 UTC; with the news of 2026-03-11 the order ended at 13:00 UTC.
        DateTime voidOpen = Data.Utc(2026, 3, 11, 15, 0);
        BotHarness h = Data.Started(Data.Utc(2026, 3, 11, 5, 0), Data.Bars(false, voidOpen, Data.NewsDay));
        h.FeedTo(Data.Utc(2026, 3, 11, 7, 0));
        Assert.Single(h.Broker.GetPendingOrders());

        h.FeedTo(Data.Utc(2026, 3, 11, 16, 0));

        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Single(h.Broker.Requests);
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void UnclearPlacementApplied_ThenVoid_NotResentAndTheOrderAlreadyAtTheBrokerIsCancelled()
    {
        BotHarness h = Data.Started(Start, Data.Bars(false, Data.Utc(2026, 3, 4, 8, 0), Data.Winter));
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearApplied);

        // The bar opening 08:00 UTC is the void bar; the order was applied but the result was unclear.
        h.FeedTo(Placement);
        Assert.Single(h.Broker.GetPendingOrders());
        h.FeedTo(Data.Utc(2026, 3, 4, 8, 15));

        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Journal.Count("order", "outcome=Abandoned "));

        h.FeedTo(Data.Utc(2026, 3, 4, 10, 0));
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
    }

    // The placement at 08:00 has an unclear outcome and nothing at the broker; the bar opening 08:00 is the void bar.
    private static BotHarness UnclearThenVoid()
    {
        BotHarness h = Data.Started(Start, Data.Bars(false, Data.Utc(2026, 3, 4, 8, 0), Data.Winter));
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied);
        h.FeedTo(Placement);
        Assert.Empty(h.Broker.GetPendingOrders());
        h.FeedTo(Data.Utc(2026, 3, 4, 8, 15));
        Assert.Equal(1, h.Journal.Count("range", "range event=void id=RH-20260304"));
        Assert.Equal(1, h.Journal.Count("order", "outcome=Abandoned "));
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        return h;
    }

    // The order of the unclear placement appears at the broker late, with its label.
    private static long AppearsLate(BotHarness h, DateTime atUtc, string id)
    {
        h.Broker.AdvanceTo(atUtc);
        return h.Broker.AddOrder(Data.Symbol, Side.Buy, 1000.0, Data.OrderEntry, Data.Stop, Data.Target, Data.Utc(2026, 3, 5, 17, 0), Data.Label(id), "late");
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void UnclearPlacementNotApplied_ThenVoid_TheOrderFirstAppearsLaterAndIsCancelledAtTheNextBar()
    {
        BotHarness h = UnclearThenVoid();
        long late = AppearsLate(h, Data.Utc(2026, 3, 4, 8, 20), Data.BuyId(Data.Winter));

        h.FeedTo(Data.Utc(2026, 3, 4, 8, 30));

        BrokerRequest cancel = Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Cancel);
        Assert.Equal(late, cancel.TargetId);
        Assert.Equal(Data.Utc(2026, 3, 4, 8, 30), cancel.AtUtc);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void UnclearPlacementNotApplied_ThenVoid_ThenRestart_TheOrderAppearsAfterwardsAndIsCancelled()
    {
        BotHarness h = UnclearThenVoid();
        BotCore restarted = h.Start(Data.Utc(2026, 3, 4, 8, 20), Data.Parameters());
        Assert.Empty(restarted.LastReport!.OrdersCancelled);
        long late = AppearsLate(h, Data.Utc(2026, 3, 4, 8, 25), Data.BuyId(Data.Winter));

        h.FeedTo(Data.Utc(2026, 3, 4, 8, 30));

        BrokerRequest cancel = Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Cancel);
        Assert.Equal(late, cancel.TargetId);
        Assert.Equal(Data.Utc(2026, 3, 4, 8, 30), cancel.AtUtc);
        Assert.Empty(h.Broker.GetPendingOrders());
        h.FeedTo(Data.Utc(2026, 3, 4, 10, 0));
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
    }

    [Theory]
    [Trait("Req", "EXE-024")]
    [InlineData(6)]
    [InlineData(9)]
    public void Restart_OnTheNextTradingDay_TheVoidOfTheDayBeforeIsNotCarriedOver(int hour)
    {
        // The void of 2026-03-04 is in the replay; at 06:00 on 2026-03-05 that day has no setup yet, at 09:00 it has its own.
        BotHarness h = Data.Started(Data.Utc(2026, 3, 5, hour, 0), Data.Bars(false, VoidOpen, Data.Winter, new DateTime(2026, 3, 5)));
        long late = AppearsLate(h, Data.Utc(2026, 3, 5, hour, 5), Data.BuyId(Data.Winter));

        h.FeedTo(Data.Utc(2026, 3, 5, hour, 30));

        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Equal(late, Assert.Single(h.Broker.GetPendingOrders()).Id);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void PriceAlreadyAtTheEntry_SkippedEntryAlreadyReached()
    {
        BotHarness h = Data.Harness(Start, Data.Bars(Data.Winter));
        h.Broker.SetSymbol(Data.Quote() with { Bid = 100.4, Ask = 100.41 });
        h.Start(Start, Data.Parameters());

        h.FeedTo(Placement);

        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.EndsWith(" skip=EntryAlreadyReached kind=range level=100.4", Assert.Single(h.Journal.Lines("signal")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void SymbolBusy_AnotherBotPosition_SkippedAndTheVoidSendsNothing()
    {
        BotHarness h = Data.Harness(Start, Data.Bars(false, VoidOpen, Data.Winter));
        long other = h.Broker.AddPosition(Data.Symbol, Side.Buy, 1000.0, 100.2, 99.5, 101.0, BreakoutRecoveryData.BuyLabel, BreakoutRecoveryData.BuyComment());
        h.Start(Start, Data.Parameters());

        h.FeedTo(Data.Utc(2026, 3, 4, 11, 0));

        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.EndsWith(" skip=SymbolBusy kind=range level=100.4", Assert.Single(h.Journal.Lines("signal")), StringComparison.Ordinal);
        Assert.Equal(other, Assert.Single(h.Broker.GetPositions()).Id);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Restart_BetweenPlacementAndFill_OrderAdoptedAndTheVoidStillCancelsIt()
    {
        BotHarness h = Placed(Data.Bars(false, VoidOpen, Data.Winter));
        long order = Assert.Single(h.Broker.GetPendingOrders()).Id;

        BotCore restarted = h.Start(Data.Utc(2026, 3, 4, 9, 0), Data.Parameters());

        Assert.Equal(new[] { order }, restarted.LastReport!.OrdersKept);
        Assert.Empty(restarted.LastReport.OrdersCancelled);
        Assert.Empty(restarted.LastReport.Strangers);
        Assert.Equal(BotState.Running, restarted.State);
        Assert.Equal(0, h.Count(BrokerCall.Cancel));

        h.FeedTo(VoidOpen);
        Assert.Single(h.Broker.GetPendingOrders());
        h.FeedTo(VoidClose);

        Assert.Equal(order, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Cancel).TargetId);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Restart_AfterAVoidWhileAway_TheOrderIsCancelledByRecovery()
    {
        BotHarness h = Placed(Data.Bars(false, VoidOpen, Data.Winter));
        long order = Assert.Single(h.Broker.GetPendingOrders()).Id;

        // The bot was away from 08:00; the void bar closed at 10:15; the restart is at 10:30.
        h.SkipTo(Data.Utc(2026, 3, 4, 10, 30));
        BotCore restarted = h.Start(Data.Utc(2026, 3, 4, 10, 30), Data.Parameters());

        Assert.Empty(restarted.LastReport!.OrdersKept);
        Assert.Equal(new[] { order.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (setup RH-20260304 void)" }, restarted.LastReport.OrdersCancelled);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
    }
}
