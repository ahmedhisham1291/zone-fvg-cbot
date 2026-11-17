using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-015 (owner decision B of 2026-11-17): while connected, the FTMO emergency guard and Friday
/// close run on every timer event in every state, Recovering included, and a pending weekend close
/// runs at timer and trade events in every state, also one deferred by EXE-016 because the market
/// was closed at the Friday close. A halt entered in Recovering stays after recovery completes. While disconnected nothing is sent. Recovery is kept stuck with only 10 D1 bars.
/// 2026-03-06 is a Friday; InitialCapital 100,000.
/// </summary>
public class Rec015DutiesWhileRecoveringScenarios
{
    private static readonly DateTime Nine = RecoveryData.Activation.AddHours(1.0);

    private static (BotHarness Harness, long Position, long Order) StuckWithTrades(DateTime start, double balance = 100000.0)
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(start, ExecutionData.Account(balance, balance, balance)));
        h.History.Available[TimeFrame.D1] = 10;
        long position = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, RecoveryData.Volume, RecoveryData.Entry, RecoveryData.Stop, RecoveryData.Target, RecoveryData.Label, RecoveryData.Comment());
        long order = h.Broker.AddOrder(RecoveryData.Symbol, Side.Sell, 1000.0, 101.5, 102.0, 100.0, start.AddHours(4.0), "ZFVG|v1|USDJPY|S-20260101-0000", RecoveryData.Comment());
        h.Start(start);
        Assert.Equal(BotState.Recovering, h.Core.State);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        return (h, position, order);
    }

    [Fact]
    [Trait("Req", "REC-015")]
    public void FridayClose_RecoveryStuck_NothingAt1959_OneCloseAndOneCancelAt2000_StaysRecovering()
    {
        (BotHarness h, long position, long order) = StuckWithTrades(RecoveryData.At(6, 19, 30));

        h.Timer(RecoveryData.At(6, 19, 59));

        Assert.Equal(0, h.Broker.WriteRequestCount);

        h.Timer(RecoveryData.At(6, 20, 0));

        Assert.Equal(1, h.Count(BrokerCall.Close));
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
        Assert.DoesNotContain(h.Broker.GetPositions(), p => p.Id == position);
        Assert.DoesNotContain(h.Broker.GetPendingOrders(), o => o.Id == order);
        Assert.All(h.Broker.Requests, request => Assert.Equal(RecoveryData.At(6, 20, 0), request.AtUtc));
        Assert.Equal(BotState.Recovering, h.Core.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "REC-015")]
    [InlineData(true)]
    [InlineData(false)]
    public void FridayClose_RecoveryStuckMarketClosed_DeferredThenDoneAtTheReopenOnATimerOrTradeEvent(bool timer)
    {
        // EXE-016 while Recovering: no request against the closed market; the close waits for the reopen.
        (BotHarness h, long position, long order) = StuckWithTrades(RecoveryData.At(6, 19, 30));
        h.Broker.SetSymbol(RecoveryData.UsdJpy(marketOpen: false));

        h.Timer(RecoveryData.At(6, 20, 0));
        h.Timer(RecoveryData.At(6, 20, 1));
        h.Timer(RecoveryData.At(7, 10, 0));

        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.True(h.Core.WeekendClosePending);
        Assert.Equal(1, h.Journal.Count("friday-close", "friday-close event=deferred reason=market-closed"));
        Assert.Equal(BotState.Recovering, h.Core.State);

        DateTime reopen = RecoveryData.At(8, 22, 0);
        h.Broker.SetSymbol(RecoveryData.UsdJpy(marketOpen: true));
        if (timer)
        {
            h.Timer(reopen);
        }
        else
        {
            h.Broker.AdvanceTo(reopen);
            h.Core.OnTradeEvent();
        }

        Assert.Equal(new[] { BrokerCall.Cancel, BrokerCall.Close }, h.Broker.Requests.Select(r => r.Call));
        Assert.All(h.Broker.Requests, request => Assert.Equal(reopen, request.AtUtc));
        Assert.DoesNotContain(h.Broker.GetPositions(), p => p.Id == position);
        Assert.DoesNotContain(h.Broker.GetPendingOrders(), o => o.Id == order);
        Assert.False(h.Core.WeekendClosePending);
        Assert.Equal(BotState.Recovering, h.Core.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-015")]
    public void FridayClose_RecoveryStuckAndDisconnected_SendsNothing()
    {
        (BotHarness h, _, _) = StuckWithTrades(RecoveryData.At(6, 19, 30));
        h.Core.OnDisconnected();

        h.Timer(RecoveryData.At(6, 20, 0));
        h.Timer(RecoveryData.At(6, 20, 1));

        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(BotState.Recovering, h.Core.State);
    }

    [Fact]
    [Trait("Req", "REC-015")]
    public void EmergencyGuard_RecoveryStuckEquity90999_ClosesAllBotTradesHaltsAndTheHaltStaysAfterRecovery()
    {
        // Balance 95,000 with no trade today: daily floor 95,000 - 4,000 = 91,000; overall floor 91,000.
        (BotHarness h, _, _) = StuckWithTrades(Nine, 95000.0);
        h.Broker.Account = ExecutionData.Account(95000.0, 90999.0, 90000.0);

        h.Timer(Nine.AddMinutes(1.0));

        Assert.Empty(h.Broker.GetPositions());
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(BotState.Halted, h.Core.State);
        Assert.Equal(new[] { HaltCause.EmergencyGuard }, h.Core.HaltCauses);
        Assert.Equal(1, h.Alerts(AlertKind.EmergencyGuard));

        // The data becomes complete: recovery completes at its next retry and the halt stays.
        h.History.Available.Remove(TimeFrame.D1);
        h.Timer(Nine.AddMinutes(16.0));

        Assert.True(h.Core.LastReport!.DataComplete);
        Assert.Equal(BotState.Halted, h.Core.State);
        Assert.Equal(new[] { HaltCause.EmergencyGuard }, h.Core.HaltCauses);
        Assert.Equal(1, h.Alerts(AlertKind.EmergencyGuard));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-015")]
    public void EmergencyGuard_RecoveryStuckEquityOneCentAboveTheFloor_DoesNothing()
    {
        (BotHarness h, _, _) = StuckWithTrades(Nine, 95000.0);
        h.Broker.Account = ExecutionData.Account(95000.0, 91000.01, 90000.0);

        h.Timer(Nine.AddMinutes(1.0));

        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(BotState.Recovering, h.Core.State);
        Assert.Equal(0, h.Alerts(AlertKind.EmergencyGuard));
    }

    [Fact]
    [Trait("Req", "REC-015")]
    public void EmergencyGuard_RecoveryStuckAndDisconnected_SendsNothing()
    {
        (BotHarness h, _, _) = StuckWithTrades(Nine, 95000.0);
        h.Core.OnDisconnected();
        h.Broker.Account = ExecutionData.Account(95000.0, 90999.0, 90000.0);

        h.Timer(Nine.AddMinutes(1.0));

        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(BotState.Recovering, h.Core.State);
    }

    [Theory]
    [Trait("Req", "REC-015")]
    [InlineData(true)]
    [InlineData(false)]
    public void WeekendClose_PendingWhileRecoveryIsStuck_DoneAtTheReopenAtATimerOrTradeEvent(bool timer)
    {
        // A start on Saturday leaves the weekend close pending; a reconnect then gets stuck in Recovering.
        DateTime saturday = RecoveryData.At(7, 10, 0);
        var h = new BotHarness(broker: BotHarness.NewBroker(saturday, marketOpen: false));
        h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, RecoveryData.Volume, RecoveryData.Entry, RecoveryData.Stop, RecoveryData.Target, RecoveryData.Label, RecoveryData.Comment());
        h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.675, 99.482, 103.061, RecoveryData.At(9, 10, 0), RecoveryData.Label, RecoveryData.Comment());
        BotCore core = h.Start(saturday);
        Assert.True(core.WeekendClosePending);
        core.OnDisconnected();
        h.History.Available[TimeFrame.D1] = 10;
        h.Broker.AdvanceTo(RecoveryData.At(7, 11, 0));
        core.OnConnected(h.History);
        Assert.Equal(BotState.Recovering, core.State);
        Assert.True(core.WeekendClosePending);

        DateTime reopen = RecoveryData.At(8, 22, 0);
        h.Broker.SetSymbol(RecoveryData.UsdJpy(marketOpen: true));
        if (timer)
        {
            h.Timer(reopen);
        }
        else
        {
            h.Broker.AdvanceTo(reopen);
            core.OnTradeEvent();
        }

        Assert.Empty(h.Broker.GetPositions());
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(new[] { BrokerCall.Cancel, BrokerCall.Close }, h.Broker.Requests.Select(r => r.Call));
        Assert.False(core.WeekendClosePending);
        Assert.Equal(BotState.Recovering, core.State);
        ScenarioAssert.Global(h.Broker);
    }
}
