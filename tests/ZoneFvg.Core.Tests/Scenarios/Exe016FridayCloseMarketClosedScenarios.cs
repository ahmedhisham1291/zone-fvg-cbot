using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-016: when the Friday close is due and the broker reports the market closed, no close or
/// cancel request is sent; the weekend close is marked pending and done at the first event with
/// the market open again. With the market open, EXE-013 is unchanged.
/// </summary>
/// <remarks>
/// One USDJPY instance on flat, continuous bars (no zone) up to Friday 2027-12-24, news disabled.
/// It starts at 19:00 (Running); at 19:30 the broker holds a bot position and a bot pending entry
/// without expiry on the symbol. FridayCloseUtc is 20:00. Sunday 2027-12-26 22:00 is the reopen.
/// </remarks>
public class Exe016FridayCloseMarketClosedScenarios
{
    private static readonly DateTime Friday = new(2027, 12, 24, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Close = Friday.AddHours(20.0);

    private static BarSet FlatBars()
    {
        static void Flat(BarBuilder b, int index) => b.Ohlc(100.95, 101.0, 100.9, 100.95);
        IReadOnlyList<Bar> d1 = new BarBuilder(TimeFrame.D1).At(Friday.AddDays(-300.0)).Repeat(305, Flat).Build();
        IReadOnlyList<Bar> h4 = new BarBuilder(TimeFrame.H4).At(Friday.AddDays(-100.0)).Repeat(620, Flat).Build();
        IReadOnlyList<Bar> m15 = new BarBuilder(TimeFrame.M15).At(Friday.AddDays(-30.0)).Repeat(3200, Flat).Build();
        return new BarSet(d1, h4, m15);
    }

    private static (BotHarness Harness, BotCore Core) Setup()
    {
        var h = new BotHarness(FlatBars(), BotHarness.NewBroker(Friday.AddHours(19.0)));
        BotCore core = h.Start(Friday.AddHours(19.0), RecoveryData.Parameters(newsEnabled: false));
        Assert.Equal(BotState.Running, core.State);
        h.Broker.AdvanceTo(Friday.AddHours(19.5));
        h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());
        h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.5, 99.5, 103.0, null, "ZFVG|v1|USDJPY|D-20271223-0800", RecoveryData.Comment());
        return (h, core);
    }

    private static void TimerEveryMinute(BotHarness h, DateTime from, DateTime to)
    {
        for (DateTime at = from; at <= to; at = at.AddMinutes(1.0))
        {
            h.Timer(at);
        }
    }

    [Fact]
    [Trait("Req", "EXE-016")]
    public void MarketClosedAtTheFridayClose_SendsNothingUntilTheReopenThenClosesAndCancels()
    {
        (BotHarness h, BotCore core) = Setup();
        h.Broker.SetSymbol(RecoveryData.UsdJpy(marketOpen: false));

        TimerEveryMinute(h, Close, Close.AddHours(1.0));

        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(0, h.Alerts(AlertKind.CloseFailed));
        Assert.Equal(0, h.Alerts(AlertKind.CancelFailed));
        Assert.NotEqual(BotState.Halted, core.State);
        Assert.Empty(core.HaltCauses);
        Assert.True(core.WeekendClosePending);
        string deferred = Assert.Single(h.Journal.Lines("friday-close"));
        Assert.Equal(JournalFormat.Time(Close) + " friday-close event=deferred reason=market-closed", deferred);

        h.Timer(Friday.AddDays(1.0).AddHours(10.0));
        h.Timer(Friday.AddDays(2.0).AddHours(21.0).AddMinutes(59.0));
        Assert.Equal(0, h.Broker.WriteRequestCount);

        DateTime reopen = Friday.AddDays(2.0).AddHours(22.0);
        h.Broker.SetSymbol(RecoveryData.UsdJpy(marketOpen: true));
        h.Timer(reopen);

        Assert.Equal(new[] { BrokerCall.Cancel, BrokerCall.Close }, h.Broker.Requests.Select(r => r.Call));
        Assert.All(h.Broker.Requests, request => Assert.Equal(reopen, request.AtUtc));
        Assert.Empty(h.Broker.GetPositions());
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.False(core.WeekendClosePending);
        Assert.Single(h.Journal.Lines("friday-close"));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-016")]
    public void MarketOpenAtTheFridayClose_ClosesAndCancelsAtOnceAsBefore()
    {
        (BotHarness h, BotCore core) = Setup();

        h.Timer(Close);

        Assert.Equal(new[] { BrokerCall.Cancel, BrokerCall.Close }, h.Broker.Requests.Select(r => r.Call));
        Assert.All(h.Broker.Requests, request => Assert.Equal(Close, request.AtUtc));
        Assert.Empty(h.Broker.GetPositions());
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.False(core.WeekendClosePending);
        Assert.Empty(h.Journal.Lines("friday-close"));
        Assert.Equal(2, h.Journal.Count("action", "duty=friday-close "));
    }

    [Fact]
    [Trait("Req", "EXE-016")]
    public void MarketReopensTheSameFriday_ClosesAtTheReopen()
    {
        (BotHarness h, BotCore core) = Setup();
        h.Broker.SetSymbol(RecoveryData.UsdJpy(marketOpen: false));
        TimerEveryMinute(h, Close, Close.AddMinutes(4.0));
        Assert.Equal(0, h.Broker.WriteRequestCount);

        h.Broker.SetSymbol(RecoveryData.UsdJpy(marketOpen: true));
        h.Timer(Close.AddMinutes(5.0));

        Assert.Equal(2, h.Broker.WriteRequestCount);
        Assert.All(h.Broker.Requests, request => Assert.Equal(Close.AddMinutes(5.0), request.AtUtc));
        Assert.Empty(h.Broker.GetPositions());
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.False(core.WeekendClosePending);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-016")]
    public void MarketClosedWithoutBotTrades_WritesNothing()
    {
        var h = new BotHarness(FlatBars(), BotHarness.NewBroker(Friday.AddHours(19.0)));
        BotCore core = h.Start(Friday.AddHours(19.0), RecoveryData.Parameters(newsEnabled: false));
        h.Broker.SetSymbol(RecoveryData.UsdJpy(marketOpen: false));

        TimerEveryMinute(h, Close, Close.AddMinutes(3.0));

        Assert.Empty(h.Journal.Lines("friday-close"));
        Assert.False(core.WeekendClosePending);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-016")]
    public void FridayCloseDue_FromFridayCloseUtcOnFridaysOnly()
    {
        var h = new ExecutionHarness();

        Assert.True(h.Trades.FridayCloseDue(ExecutionData.At(16, 20, 0)));
        Assert.False(h.Trades.FridayCloseDue(ExecutionData.At(16, 20, 0).AddTicks(-1)));
        Assert.False(h.Trades.FridayCloseDue(ExecutionData.At(17, 20, 0)));
        Assert.Throws<ArgumentException>(() => h.Trades.FridayCloseDue(new DateTime(2026, 1, 16, 20, 0, 0, DateTimeKind.Unspecified)));
    }
}
