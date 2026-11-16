using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The risk actions completed in WP07 through <see cref="BotCore"/> and a <see cref="FakeBroker"/>:
/// daily budget (RSK-006), kill switch (RSK-008), emergency guard actions and bad broker data
/// (RSK-009, architecture section 10), request and entry limits (RSK-010), and NewsEnabled (owner
/// decision of 2026-11-16). Data: <see cref="RecoveryData"/>; Wednesday 2026-03-04, CE(S)T day
/// from 2026-03-03 23:00 UTC; RiskEpochDate 2026-01-01; InitialCapital 100,000.
/// </summary>
/// <remarks>
/// The reference pending order (41,000 units, entry 100.675, stop 99.482) risks
/// 41,000 x 119.3 x 0.0001 = 489.13 at its stop.
/// </remarks>
public class RiskActionScenarios
{
    private static readonly DateTime A = RecoveryData.Activation;
    private static readonly DateTime Nine = A.AddHours(1.0);

    private static ClosedTradeInfo Loss(long positionId, DateTime closeUtc, double net, double balanceAfter)
    {
        return new ClosedTradeInfo(positionId, RecoveryData.Symbol, "ZFVG|v1|USDJPY|D-20260302-0800", closeUtc.AddHours(-1.0), closeUtc, net, 0.0, 0.0, balanceAfter);
    }

    private static long AddReferenceOrder(FakeBroker broker)
    {
        return broker.AddOrder(RecoveryData.Symbol, Side.Buy, RecoveryData.Volume, RecoveryData.Entry, RecoveryData.Stop, RecoveryData.Target, A.AddHours(4.0), RecoveryData.Label, RecoveryData.Comment());
    }

    // ------------------------------------------------------------------ RSK-006

    [Fact]
    [Trait("Req", "RSK-006")]
    public void DailyBudget_ExhaustedWhileRunning_CancelsPendingEntriesAndBlocksUntilTheNextCestDay()
    {
        // 09:00: lost 900 today, M = 100,000; the order risks 489.13: M - W = 1,389.13, within 1,500.
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(99100.0, 99100.0, 99100.0)));
        h.Broker.AddClosedTrade(Loss(901, A.AddHours(-1.0), -900.0, 99100.0));
        long order = AddReferenceOrder(h.Broker);
        BotCore core = h.Start(Nine);
        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(new[] { order }, core.LastReport!.OrdersKept);

        // 09:10: another 200 lost. At the 09:15 close M - W = 1,100 + 489.13 = 1,589.13 > 1,500.
        h.Broker.AdvanceTo(A.AddMinutes(70.0));
        h.Broker.Account = ExecutionData.Account(98900.0, 98900.0, 98900.0);
        h.Broker.AddClosedTrade(Loss(902, A.AddMinutes(70.0), -200.0, 98900.0));
        h.FeedTo(A.AddMinutes(75.0));

        Assert.Equal(BotState.Blocked, core.State);
        Assert.True(core.BudgetBlocked);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.DailyLimit));

        // Without the order M - W = 1,100: the risk has fallen, but the cause clears only at the day change.
        h.FeedTo(A.AddHours(3.0));
        h.Timer(RecoveryData.At(4, 22, 59, 59));
        Assert.Equal(BotState.Blocked, core.State);

        h.Timer(RecoveryData.At(4, 23, 0));
        Assert.Equal(BotState.Running, core.State);
        Assert.False(core.BudgetBlocked);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void DailyBudget_ExhaustedAcrossARestart_StaysBlockedUntilTheNextCestDay()
    {
        // Lost 1,600 today: M - W = 1,600 + 489.13 with the order, 1,600 without it.
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(98400.0, 98400.0, 98400.0)));
        h.Broker.AddClosedTrade(Loss(901, A.AddHours(-1.0), -1600.0, 98400.0));
        long order = AddReferenceOrder(h.Broker);

        BotCore first = h.Start(Nine);
        Assert.Equal(BotState.Blocked, first.State);
        Assert.Equal(new[] { order + " Cancelled (daily budget exhausted)" }, first.LastReport!.CatchUp);

        BotCore second = h.Start(A.AddHours(2.0));
        Assert.Equal(BotState.Blocked, second.State);
        Assert.Equal(1, h.Broker.WriteRequestCount);

        // 23:30 UTC is 00:30 CET on 5 March: M = 98,400, nothing at risk.
        BotCore nextDay = h.Start(RecoveryData.At(4, 23, 30));
        Assert.Equal(BotState.Running, nextDay.State);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ RSK-008

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_AtExactly8Percent_HaltsCancelsEntryOrdersAndHaltsAgainAfterARestart()
    {
        // Lost 8,000 on Monday: peak 100,000, equity 92,000, drawdown 8% exactly. No emergency: floors 88,000 and 91,000.
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(92000.0, 92000.0, 92000.0)));
        h.Broker.AddClosedTrade(Loss(901, RecoveryData.At(2, 15, 0), -8000.0, 92000.0));
        AddReferenceOrder(h.Broker);

        BotCore core = h.Start(Nine);

        Assert.Equal(BotState.Halted, core.State);
        Assert.Equal(new[] { HaltCause.KillSwitch }, core.HaltCauses);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(1, h.Count(BrokerCall.Cancel));
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.KillSwitch));

        BotCore restarted = h.Start(A.AddHours(2.0));
        Assert.Equal(BotState.Halted, restarted.State);
        Assert.Equal(1, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_OneCentAbove8Percent_DoesNotHalt()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(92000.0, 92000.01, 92000.0)));
        h.Broker.AddClosedTrade(Loss(901, RecoveryData.At(2, 15, 0), -8000.0, 92000.0));

        BotCore core = h.Start(Nine);

        Assert.Equal(BotState.Running, core.State);
    }

    [Fact]
    [Trait("Req", "RSK-008")]
    public void KillSwitch_ReachedWhileRunning_HaltsAtTheNextBarAndPlacesNoOrder()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.FeedTo(A.AddMinutes(15.0));
        h.Broker.Account = ExecutionData.Account(92000.0, 92000.0, 92000.0);
        h.Broker.AddClosedTrade(Loss(901, RecoveryData.At(2, 15, 0), -8000.0, 92000.0));

        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(BotState.Halted, core.State);
        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Journal.Count("skip", "reason=BotNotRunning"));
    }

    // ------------------------------------------------------------------ RSK-009 actions (prompt WP07 item 5)

    [Fact]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_FiresWhileRunning_ClosesBotPositionsCancelsOrdersAndHalts()
    {
        var h = new BotHarness();
        h.Start(A);
        h.FeedTo(RecoveryData.Confirmation);
        h.FillReferenceOrder(A.AddMinutes(50.0));
        long manual = h.Broker.AddPosition(RecoveryData.Symbol, Side.Sell, 1000.0, 101.0, 102.0, 99.0, "manual", "x");
        long other = h.Broker.AddOrder(RecoveryData.Symbol, Side.Sell, 1000.0, 101.5, 102.0, 100.0, A.AddHours(4.0), "ZFVG|v1|USDJPY|S-20260101-0000", RecoveryData.Comment());
        h.Broker.Account = ExecutionData.Account(100000.0, 90500.0, 90000.0);

        h.FeedTo(Nine);

        Assert.Equal(BotState.Halted, h.Core.State);
        Assert.Equal(new[] { HaltCause.EmergencyGuard }, h.Core.HaltCauses);
        Assert.Null(h.ReferencePosition());
        Assert.DoesNotContain(h.Broker.GetPendingOrders(), order => order.Id == other);
        Assert.Contains(h.Broker.GetPositions(), position => position.Id == manual);
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.EmergencyGuard));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-009")]
    public void EmergencyGuard_BrokenSnapshotWithEquityBelowTheFloor_StillClosesEverythingAndHalts()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(95000.0, 90500.0, 90000.0)));
        h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, RecoveryData.Volume, RecoveryData.Entry, RecoveryData.Stop, RecoveryData.Target, RecoveryData.Label, RecoveryData.Comment());
        h.Port.History = _ => throw new InvalidOperationException("history unavailable");

        BotCore core = h.Start(Nine);

        Assert.Equal(BotState.Halted, core.State);
        Assert.Equal(new[] { HaltCause.EmergencyGuard }, core.HaltCauses);
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.RiskDataUnavailable));
        Assert.Contains("journaled=failed(InvalidOperationException: history unavailable)", core.LastReport!.Text, StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-009")]
    public void BadBrokerData_EquityAboveTheFloor_NoNewEntries()
    {
        var h = new BotHarness();
        h.Port.History = _ => throw new InvalidOperationException("history unavailable");

        BotCore core = h.Start(A);
        h.FeedTo(A.AddHours(2.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Journal.Count("skip", "zone=" + RecoveryData.ZoneId + " reason=BrokerDataInvalid"));
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.RiskDataUnavailable));
    }

    // ------------------------------------------------------------------ RSK-010

    [Fact]
    [Trait("Req", "RSK-010")]
    public void RequestLimit_TwentyFirstRequestInAnHour_IsNotSentAndHalts()
    {
        // Recovery cancels 20 orders without an expiry at 08:00; the entry at 08:45 would be the 21st request.
        var h = new BotHarness();
        for (int i = 0; i < 20; i++)
        {
            h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.0, 99.0, 102.0, null, "ZFVG|v1|USDJPY|X-" + i, RecoveryData.Comment());
        }

        BotCore core = h.Start(A);
        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(20, h.Count(BrokerCall.Cancel));

        h.FeedTo(A.AddHours(2.0));

        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(BotState.Halted, core.State);
        Assert.Equal(new[] { HaltCause.RequestLimit }, core.HaltCauses);
        Assert.Equal(1, h.Journal.Count("skip", "reason=RequestLimitReached"));
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.CircuitBreaker));
        ScenarioAssert.Global(h.Broker, 20);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void RequestLimit_TwentyFirstProtectiveRequest_IsSentAndHalts()
    {
        // Cancels protect the account and are always sent (owner decision of 2026-11-16); the 21st halts the instance.
        var h = new BotHarness();
        for (int i = 0; i < 21; i++)
        {
            h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.0, 99.0, 102.0, null, "ZFVG|v1|USDJPY|X-" + i, RecoveryData.Comment());
        }

        BotCore core = h.Start(A);

        Assert.Equal(21, h.Count(BrokerCall.Cancel));
        Assert.Equal(BotState.Halted, core.State);
        Assert.Equal(new[] { HaltCause.RequestLimit }, core.HaltCauses);
        ScenarioAssert.Global(h.Broker, 21);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void RequestLimit_TwentyRequests_StillAllowTheNextHour()
    {
        // The 20 cancels at 08:00 have left the window at 09:00; a placement at 09:00 would be allowed.
        var h = new BotHarness();
        for (int i = 0; i < 20; i++)
        {
            h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.0, 99.0, 102.0, null, "ZFVG|v1|USDJPY|X-" + i, RecoveryData.Comment());
        }

        BotCore core = h.Start(A);
        h.Timer(Nine);

        Assert.Equal(BotState.Running, core.State);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void EntryLimit_ThirdEntryOnTheSymbolToday_IsSkipped()
    {
        var h = new BotHarness();
        h.Broker.AddClosedTrade(new ClosedTradeInfo(801, RecoveryData.Symbol, "ZFVG|v1|USDJPY|D-20260303-0000", RecoveryData.At(4, 1, 0), RecoveryData.At(4, 2, 0), 0.0, 0.0, 0.0, 100000.0));
        h.Broker.AddClosedTrade(new ClosedTradeInfo(802, RecoveryData.Symbol, "ZFVG|v1|USDJPY|D-20260303-0400", RecoveryData.At(4, 3, 0), RecoveryData.At(4, 4, 0), 0.0, 0.0, 0.0, 100000.0));

        h.Start(A);
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Journal.Count("skip", "reason=SymbolEntryLimitReached"));
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void ErrorLimit_SixthErrorInAnHour_Halts()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.Port.PositionsFault = new InvalidOperationException("positions unavailable");
        for (int i = 0; i < 5; i++)
        {
            core.OnTradeEvent();
        }

        Assert.Equal(BotState.Running, core.State);
        core.OnTradeEvent();

        Assert.Equal(BotState.Halted, core.State);
        Assert.Equal(new[] { HaltCause.ErrorLimit }, core.HaltCauses);
    }

    // ------------------------------------------------------------------ NewsEnabled (owner decision of 2026-11-16)

    [Fact]
    [Trait("Req", "REC-010")]
    public void NewsEnabled_True_NewsWindowSkipsTheSignalAndTheSummaryShowsIt()
    {
        // A USD event at 09:00 opens a window from 08:30 to 09:30 around the 08:45 confirmation.
        var h = new BotHarness();
        BotCore core = h.Start(A, RecoveryData.Parameters(true, "2026-03-04 09:00,USD,High,ISM Services PMI\n"));
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Journal.Count("skip", "reason=NewsWindow"));
        Assert.True(core.LastReport!.NewsEnabled);
        Assert.Equal(1, h.Journal.Count("start", " news=enabled "));
    }

    [Fact]
    [Trait("Req", "REC-010")]
    public void NewsEnabled_False_NoNewsCheckNoDownloadAndTheSummaryShowsIt()
    {
        var http = new FakeTransport(NewsTestData.Failed());
        var h = new BotHarness(isBacktest: false, http: http);
        BotCore core = h.Start(A, RecoveryData.Parameters(false, "2026-03-04 09:00,USD,High,ISM Services PMI\n"));
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Equal(0, http.Calls);
        Assert.False(core.LastReport!.NewsEnabled);
        Assert.Contains(" news=disabled ", core.LastReport.Text, StringComparison.Ordinal);
        Assert.Equal(1, h.Journal.Count("start", " news=disabled "));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-010")]
    public void NewsEnabled_TrueWithTheFeedDown_FetchesAndBlocks()
    {
        var http = new FakeTransport(NewsTestData.Failed());
        var h = new BotHarness(isBacktest: false, http: http);

        BotCore core = h.Start(A, RecoveryData.Parameters(true));

        Assert.Equal(1, http.Calls);
        Assert.Equal(BotState.Blocked, core.State);
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.CalendarUnavailable));
    }
}
