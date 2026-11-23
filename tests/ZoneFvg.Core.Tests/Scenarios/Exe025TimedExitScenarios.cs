using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using Data = ZoneFvg.Core.Tests.SessionRangeBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-025 through <see cref="BotCore"/> and a <see cref="FakeBroker"/>: an open session range
/// position is closed at market at the first closed M15 bar at or after 16:30 New York of its
/// trading day (21:30 UTC in winter, 20:30 UTC in summer), once, with exit reason ClosedByBot; with
/// the market closed it is closed at the reopen; the Friday close at 20:00 UTC comes first on a
/// Friday; the duty runs after a restart and while Recovering. Data: <see cref="SessionRangeBotData"/>.
/// </summary>
public class Exe025TimedExitScenarios
{
    private static readonly DateTime Start = Data.Utc(2026, 3, 4, 6, 0);
    private static readonly DateTime WinterExit = Data.Utc(2026, 3, 4, 21, 30);

    // Placed at 03:00 New York and filled 20 minutes later: the ask reaches the buy stop 100.41.
    private static (BotHarness Harness, long Position) Filled(DateTime start, DateTime tradingDate, bool reportExitReasons = true)
    {
        BotHarness h = Data.Started(start, Data.Bars(tradingDate));
        DateTime placement = Data.Evaluation(tradingDate);
        h.FeedTo(placement);
        h.Broker.ReportExitReasons = reportExitReasons;
        h.Broker.AdvanceTo(placement.AddMinutes(20.0));
        h.Broker.Tick(Data.Symbol, 100.4, 100.41);
        h.Core.OnTradeEvent();
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(Data.Label(Data.BuyId(tradingDate)), position.Label);
        return (h, position.Id);
    }

    public static IEnumerable<object[]> Days()
    {
        yield return new object[] { Data.Utc(2026, 3, 4, 6, 0), new DateTime(2026, 3, 4), Data.Utc(2026, 3, 4, 21, 30) };
        yield return new object[] { Data.Utc(2026, 3, 12, 5, 0), new DateTime(2026, 3, 12), Data.Utc(2026, 3, 12, 20, 30) };
    }

    [Theory]
    [Trait("Req", "EXE-025")]
    [MemberData(nameof(Days))]
    public void MarketOpen_At1630NewYork_ClosedOnceAtMarketByTheBot(DateTime start, DateTime tradingDate, DateTime exitUtc)
    {
        (BotHarness h, long position) = Filled(start, tradingDate);

        h.FeedTo(exitUtc.AddMinutes(-15.0));
        h.Timer(exitUtc.AddMinutes(-1.0));
        Assert.Equal(position, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(0, h.Count(BrokerCall.Close));

        h.FeedTo(exitUtc);
        h.Core.OnTradeEvent();

        Assert.Empty(h.Broker.GetPositions());
        BrokerRequest close = Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close);
        Assert.Equal(position, close.TargetId);
        Assert.Equal(exitUtc, close.AtUtc);
        Assert.Equal(TradeExitReason.ClosedByBot, Assert.Single(h.Broker.History).ExitReason);
        Assert.Equal(1, h.Journal.Count("action", "duty=range-exit "));
        Assert.Contains(" exit=ClosedByBot ", Assert.Single(h.Journal.Lines("trade"), line => line.Contains(" trade symbol=", StringComparison.Ordinal)), StringComparison.Ordinal);

        h.FeedTo(exitUtc.AddHours(1.0));
        h.Timer(exitUtc.AddHours(1.0).AddMinutes(1.0));
        Assert.Equal(1, h.Count(BrokerCall.Close));
        Assert.Equal(2, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void MarketOpen_TimerBeforeTheBar_ClosedByTheTimerAtTheExitTime()
    {
        (BotHarness h, long position) = Filled(Start, Data.Winter);
        h.FeedTo(WinterExit.AddMinutes(-15.0));

        h.Timer(WinterExit);

        Assert.Equal(position, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close).TargetId);
        Assert.Empty(h.Broker.GetPositions());
        h.FeedTo(WinterExit.AddMinutes(30.0));
        Assert.Equal(1, h.Count(BrokerCall.Close));
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void MarketClosed_AtTheExit_NoRequestOneDeferredLine_ClosedAtTheReopen()
    {
        (BotHarness h, long position) = Filled(Start, Data.Winter);
        h.FeedTo(WinterExit.AddMinutes(-15.0));
        h.Broker.SetSymbol(Data.Quote() with { IsMarketOpen = false });

        h.Timer(WinterExit);
        h.Timer(WinterExit.AddMinutes(1.0));
        h.Timer(WinterExit.AddMinutes(10.0));

        Assert.Equal(0, h.Count(BrokerCall.Close));
        Assert.Equal(position, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(1, h.Journal.Count("range-exit", "range-exit event=deferred reason=market-closed"));

        h.Broker.SetSymbol(Data.Quote());
        h.Timer(WinterExit.AddMinutes(30.0));

        Assert.Equal(position, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close).TargetId);
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(1, h.Journal.Count("range-exit", "range-exit event=deferred"));
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void Friday_TheFridayCloseAt2000UtcComesBeforeTheExitAt2030Utc()
    {
        (BotHarness h, long position) = Filled(Data.Utc(2026, 3, 13, 5, 0), Data.Friday);
        h.FeedTo(Data.Utc(2026, 3, 13, 20, 0));
        Assert.Equal(0, h.Count(BrokerCall.Close));

        h.Timer(Data.Utc(2026, 3, 13, 20, 0));

        Assert.Equal(position, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close).TargetId);
        Assert.Equal(1, h.Journal.Count("action", "duty=friday-close "));
        h.FeedTo(Data.Utc(2026, 3, 13, 21, 0));
        h.Timer(Data.Utc(2026, 3, 13, 21, 1));
        Assert.Equal(1, h.Count(BrokerCall.Close));
        Assert.Equal(0, h.Journal.Count("action", "duty=range-exit "));
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void Restart_WithAnOpenPosition_AdoptedAndClosedAtTheExit()
    {
        (BotHarness h, long position) = Filled(Start, Data.Winter);

        BotCore restarted = h.Start(Data.Utc(2026, 3, 4, 12, 0), Data.Parameters());

        Assert.Equal(new[] { position }, restarted.LastReport!.PositionsAdopted);
        Assert.Empty(restarted.LastReport.Strangers);
        h.FeedTo(WinterExit.AddMinutes(-15.0));
        Assert.Single(h.Broker.GetPositions());

        h.FeedTo(WinterExit);

        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(position, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close).TargetId);
        Assert.Equal(WinterExit, h.Broker.Requests.Single(request => request.Call == BrokerCall.Close).AtUtc);
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void Restart_BetweenPlacementAndFill_TheAdoptedOrderFillsAndTheExitStillHappens()
    {
        BotHarness h = Data.Started(Start, Data.Bars(Data.Winter));
        h.FeedTo(Data.Utc(2026, 3, 4, 8, 0));
        long order = Assert.Single(h.Broker.GetPendingOrders()).Id;

        BotCore restarted = h.Start(Data.Utc(2026, 3, 4, 9, 0), Data.Parameters());
        Assert.Equal(new[] { order }, restarted.LastReport!.OrdersKept);
        h.Broker.AdvanceTo(Data.Utc(2026, 3, 4, 9, 10));
        h.Broker.Tick(Data.Symbol, 100.4, 100.41);
        restarted.OnTradeEvent();
        Assert.Single(h.Broker.GetPositions());

        h.FeedTo(WinterExit);

        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(1, h.Count(BrokerCall.Close));
        Assert.Equal(1, h.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void Recovering_AfterTheExitTime_TheTimerClosesThePositionAndTheStateStaysRecovering()
    {
        (BotHarness h, long position) = Filled(Start, Data.Winter);
        h.FeedTo(Data.Utc(2026, 3, 4, 21, 0));

        // Recovery cannot complete with 10 D1 bars: the restarted bot stays Recovering.
        h.History.Available[TimeFrame.D1] = 10;
        BotCore restarted = h.Start(Data.Utc(2026, 3, 4, 21, 40), Data.Parameters());
        Assert.Equal(BotState.Recovering, restarted.State);
        Assert.Equal(0, h.Count(BrokerCall.Close));

        h.Timer(Data.Utc(2026, 3, 4, 21, 41));

        Assert.Equal(position, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close).TargetId);
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(BotState.Recovering, restarted.State);
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void OtherKinds_PositionsOfOtherModesAreNotTouchedByTheExit()
    {
        BotHarness h = Data.Harness(Start, Data.Bars());
        long breakout = h.Broker.AddPosition(Data.Symbol, Side.Buy, 1000.0, 100.2, 99.5, 101.0, BreakoutRecoveryData.BuyLabel, BreakoutRecoveryData.BuyComment());
        h.Start(Start, Data.Parameters());

        h.FeedTo(WinterExit.AddMinutes(30.0));
        h.Timer(WinterExit.AddMinutes(31.0));

        Assert.Equal(breakout, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void OtherModes_ASessionRangePositionIsNotClosedByABreakoutInstance()
    {
        // The duty belongs to SessionRange mode: a Breakout instance keeps such a position with its stop and target.
        BotHarness h = Data.Harness(Start, Data.Bars());
        long range = h.Broker.AddPosition(Data.Symbol, Side.Buy, 1000.0, 100.41, 100.0, 101.2, Data.Label(Data.BuyId(Data.Winter)), BreakoutRecoveryData.BuyComment());
        h.Start(Start, BreakoutRecoveryData.Parameters());

        h.FeedTo(WinterExit.AddMinutes(30.0));
        h.Timer(WinterExit.AddMinutes(31.0));

        Assert.Equal(range, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(0, h.Count(BrokerCall.Close));
    }
}
