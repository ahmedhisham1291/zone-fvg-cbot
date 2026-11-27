using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using Data = ZoneFvg.Core.Tests.OvernightBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-033 and PLT-021 through <see cref="BotCore"/> and a <see cref="FakeBroker"/> on the example
/// XAUUSD of <see cref="OvernightBotData"/>. Trading day Tuesday 2026-03-10 (it starts on Monday
/// evening, summer time in New York): the evaluation bar closes at 22:15 UTC on 2026-03-09, the buy
/// stop at 2650.39 (stop 2610.39, target 2850.39, 1% of 100,000 at 40 USD per ounce = 25 ounces)
/// lives until 23:15 UTC, and the timed exit is due at 06:45 UTC on 2026-03-10.
/// </summary>
public class Exe033OvernightScenarios
{
    private static readonly DateTime Tuesday = new(2026, 3, 10);
    private static readonly DateTime Start = Data.Utc(2026, 3, 9, 12, 0);
    private static readonly DateTime Placement = Data.Utc(2026, 3, 9, 22, 15);
    private static readonly DateTime ExitUtc = Data.Utc(2026, 3, 10, 6, 45);
    private static readonly string Label = Data.Label(Data.Id(Tuesday));

    private static BotHarness Placed(double spread = 0.30)
    {
        BotHarness h = Data.Started(Start, Data.Bars(), spread: spread);
        h.FeedTo(Placement);
        return h;
    }

    // The ask reaches the buy stop 2650.39 at 22:30 UTC.
    private static (BotHarness Harness, long Position) Filled()
    {
        BotHarness h = Placed();
        Assert.Equal(Label, Assert.Single(h.Broker.GetPendingOrders()).Label);
        h.Broker.ReportExitReasons = true;
        h.Broker.AdvanceTo(Data.Utc(2026, 3, 9, 22, 30));
        h.Broker.Tick(Data.Symbol, 2650.2, 2650.5);
        h.Core.OnTradeEvent();
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(Label, position.Label);
        return (h, position.Id);
    }

    private static string SignalLine(BotHarness h)
    {
        return Assert.Single(h.Journal.Lines("signal"), line => line.Contains(" zone=" + Data.Id(Tuesday) + " ", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ placement

    [Fact]
    [Trait("Req", "EXE-033")]
    [Trait("Req", "PLT-021")]
    public void Placement_AtThe1815Bar_ABuyStopWithTheLevelsLabelAndExpiry()
    {
        BotHarness h = Placed();

        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal(Label, order.Label);
        Assert.Equal(Side.Buy, order.Side);
        Assert.Equal(Data.OrderEntry, order.EntryPrice, 9);
        Assert.Equal(Data.OrderStop, order.StopLoss!.Value, 9);
        Assert.Equal(Data.OrderTarget, order.TakeProfit!.Value, 9);
        Assert.Equal(Data.Utc(2026, 3, 9, 23, 15), order.ExpiryUtc);
        Assert.Equal(25.0, order.VolumeUnits);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.NotNull(request.StopOrder);
        Assert.Equal(Placement, request.AtUtc);
        Assert.Equal("e=2650.39;s=2610.39;t=2850.39;r=1000.00;h=" + Data.Hash, order.Comment);

        string line = SignalLine(h);
        Assert.Contains(" result=taken ", line, StringComparison.Ordinal);
        Assert.Contains(" entry=2650.39 stop=2610.39 target=2850.39 ", line, StringComparison.Ordinal);
        Assert.Contains(" spreadPips=30.0 ", line, StringComparison.Ordinal);
        string start = Assert.Single(h.Journal.Lines("start"));
        Assert.Contains(" hash=" + Data.Hash + " ", start, StringComparison.Ordinal);
        Assert.EndsWith(" mode=Overnight brake=off", start, StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void Placement_OneSetupPerTradingDay_NothingMoreUntilTheNextEvening()
    {
        BotHarness h = Placed();

        h.FeedTo(Data.Utc(2026, 3, 10, 22, 0));

        Assert.Equal(1, h.Count(BrokerCall.Place));
        h.FeedTo(Data.Utc(2026, 3, 10, 22, 15));
        Assert.Equal(2, h.Count(BrokerCall.Place));
        Assert.Equal(Data.Label(Data.Id(new DateTime(2026, 3, 11))), Assert.Single(h.Broker.GetPendingOrders()).Label);
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [InlineData(2650.09, 2650.39)]
    [InlineData(2650.30, 2650.60)]
    public void Placement_AskAlreadyAtOrAboveTheEntry_SkippedAsEntryAlreadyReached(double bid, double ask)
    {
        BotHarness h = Data.Started(Start, Data.Bars());
        h.Broker.SetSymbol(Data.Gold() with { Bid = bid, Ask = ask });

        h.FeedTo(Placement);

        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(0, h.Count(BrokerCall.Place));
        string line = SignalLine(h);
        Assert.Contains(" result=skipped ", line, StringComparison.Ordinal);
        Assert.Contains(" skip=EntryAlreadyReached", line, StringComparison.Ordinal);
        Assert.Contains(" spreadPips=30.0 ", line, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "EXE-033")]
    [InlineData(0.41, false, "41.0")]
    [InlineData(0.40, true, "40.0")]
    public void Placement_SpreadAboveMaxSpreadPips_SkippedAsSpreadTooWide_BothLinesCarryTheSpread(double spread, bool taken, string spreadPips)
    {
        BotHarness h = Placed(spread);

        string line = SignalLine(h);
        Assert.Contains(" spreadPips=" + spreadPips + " ", line, StringComparison.Ordinal);
        Assert.Contains(taken ? " result=taken " : " result=skipped ", line, StringComparison.Ordinal);
        if (taken)
        {
            // The spread at placement is added to all three levels: 2650.09 + 0.40.
            Assert.Equal(2650.49, Assert.Single(h.Broker.GetPendingOrders()).EntryPrice, 9);
            Assert.Equal(2610.49, Assert.Single(h.Broker.GetPendingOrders()).StopLoss!.Value, 9);
        }
        else
        {
            Assert.Contains(" skip=SpreadTooWide", line, StringComparison.Ordinal);
            Assert.Empty(h.Broker.GetPendingOrders());
            Assert.Contains(h.Journal.Lines("skip"), l => l.Contains(" reason=SpreadTooWide measured=41.0000 limit=40.0000 ", StringComparison.Ordinal));
        }
    }

    // ------------------------------------------------------------------ the timed exit

    [Fact]
    [Trait("Req", "EXE-033")]
    public void TimedExit_At0245NewYork_ClosedOnceAtMarketByTheBot()
    {
        (BotHarness h, long position) = Filled();

        h.FeedTo(ExitUtc.AddMinutes(-15.0));
        h.Timer(ExitUtc.AddMinutes(-1.0));
        Assert.Equal(position, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(0, h.Count(BrokerCall.Close));

        h.FeedTo(ExitUtc);
        h.Core.OnTradeEvent();

        Assert.Empty(h.Broker.GetPositions());
        BrokerRequest close = Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close);
        Assert.Equal(position, close.TargetId);
        Assert.Equal(ExitUtc, close.AtUtc);
        Assert.Equal(TradeExitReason.ClosedByBot, Assert.Single(h.Broker.History).ExitReason);
        Assert.Equal(1, h.Journal.Count("action", "duty=night-exit "));
        Assert.Equal(0, h.Journal.Count("action", "duty=range-exit "));

        h.FeedTo(ExitUtc.AddHours(1.0));
        h.Timer(ExitUtc.AddHours(1.0).AddMinutes(1.0));
        Assert.Equal(1, h.Count(BrokerCall.Close));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void TimedExit_MarketReportedClosedAtTheExitBar_TheCloseIsStillSent()
    {
        (BotHarness h, long position) = Filled();
        h.FeedTo(ExitUtc.AddMinutes(-15.0));
        h.Broker.SetSymbol(Data.Gold(marketOpen: false));

        h.FeedTo(ExitUtc);

        Assert.Equal(position, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close).TargetId);
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(0, h.Journal.Count("night-exit", "night-exit event=deferred"));
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void TimedExit_ByTheTimerWithTheMarketClosed_DeferredOnceAndClosedAtTheReopen()
    {
        (BotHarness h, long position) = Filled();
        h.FeedTo(ExitUtc.AddMinutes(-15.0));
        h.Broker.SetSymbol(Data.Gold(marketOpen: false));

        h.Timer(ExitUtc);
        h.Timer(ExitUtc.AddMinutes(1.0));
        h.Timer(ExitUtc.AddMinutes(10.0));

        Assert.Equal(0, h.Count(BrokerCall.Close));
        Assert.Equal(position, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(1, h.Journal.Count("night-exit", "night-exit event=deferred reason=market-closed"));

        h.Broker.SetSymbol(Data.Gold());
        h.Timer(ExitUtc.AddMinutes(11.0));

        Assert.Equal(position, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close).TargetId);
        Assert.Empty(h.Broker.GetPositions());
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void TimedExit_CloseRefused_AlertedAndRetriedAtTheNextBar()
    {
        (BotHarness h, long position) = Filled();
        h.FeedTo(ExitUtc.AddMinutes(-15.0));
        h.Broker.Script(BrokerCall.Close, BrokerFault.Rejected, "MarketClosed");

        h.FeedTo(ExitUtc);

        Assert.Equal(position, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(1, h.Journal.Count("action", "duty=night-exit id=" + position + " outcome=CloseFailed "));

        h.FeedTo(ExitUtc.AddMinutes(15.0));

        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(2, h.Count(BrokerCall.Close));
        Assert.Equal(1, h.Journal.Count("action", "duty=night-exit id=" + position + " outcome=Closed "));
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void TimedExit_ASessionRangePositionIsNotClosedByTheOvernightInstance()
    {
        BotHarness h = Data.Started(Start, Data.Bars());
        string rangeLabel = Data.Label(Data.RangeId(Tuesday));
        long range = h.Broker.AddPosition(Data.Symbol, Side.Buy, 10.0, 2662.3, 2638.0, 2710.0, rangeLabel, "comment");

        h.FeedTo(Data.Utc(2026, 3, 10, 21, 0));
        h.Timer(Data.Utc(2026, 3, 10, 21, 1));

        Assert.Equal(range, Assert.Single(h.Broker.GetPositions(), p => p.Label == rangeLabel).Id);
        Assert.DoesNotContain(h.Broker.Requests, request => request.TargetId == range);
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void TimedExit_AnOvernightPositionIsNotClosedByTheSessionRangeInstance()
    {
        BotHarness h = Data.Harness(Start, Data.Bars(Tuesday));
        h.Start(Start, Data.Parameters(EntryMode.SessionRange, 0.75));
        long night = h.Broker.AddPosition(Data.Symbol, Side.Buy, 25.0, Data.OrderEntry, Data.OrderStop, Data.OrderTarget, Label, "comment");

        // Through the Overnight exit (06:45 UTC) and the SessionRange exit (20:30 UTC) of 2026-03-10.
        h.FeedTo(Data.Utc(2026, 3, 10, 21, 0));
        h.Timer(Data.Utc(2026, 3, 10, 21, 1));

        Assert.Equal(night, Assert.Single(h.Broker.GetPositions(), p => p.Label == Label).Id);
        Assert.DoesNotContain(h.Broker.Requests, request => request.TargetId == night);
        Assert.Equal(0, h.Journal.Count("action", "duty=range-exit "));
    }

    // ------------------------------------------------------------------ recovery

    [Fact]
    [Trait("Req", "EXE-033")]
    public void Recovery_RestartBetweenTheEntryAnd0245_PositionKeptWithItsLevelsAndClosedAtTheExit()
    {
        (BotHarness h, long position) = Filled();
        h.FeedTo(Data.Utc(2026, 3, 10, 1, 0));

        BotCore restarted = h.Start(Data.Utc(2026, 3, 10, 1, 5), Data.Parameters());

        Assert.Equal(new[] { position }, restarted.LastReport!.PositionsAdopted);
        Assert.Empty(restarted.LastReport.Strangers);
        PositionInfo kept = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(Data.OrderStop, kept.StopLoss!.Value, 9);
        Assert.Equal(Data.OrderTarget, kept.TakeProfit!.Value, 9);
        Assert.Equal(0, h.Count(BrokerCall.Close));
        Assert.Equal(0, h.Count(BrokerCall.Modify));

        h.FeedTo(ExitUtc);

        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(position, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close).TargetId);
        Assert.Equal(1, h.Journal.Count("action", "duty=night-exit "));
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void Recovery_RestartWhileTheOrderIsPending_OrderKept()
    {
        BotHarness h = Placed();
        long order = Assert.Single(h.Broker.GetPendingOrders()).Id;

        BotCore restarted = h.Start(Data.Utc(2026, 3, 9, 22, 40), Data.Parameters());

        Assert.Equal(new[] { order }, restarted.LastReport!.OrdersKept);
        Assert.Empty(restarted.LastReport.OrdersCancelled);
        Assert.Equal(order, Assert.Single(h.Broker.GetPendingOrders()).Id);
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
    }
}
