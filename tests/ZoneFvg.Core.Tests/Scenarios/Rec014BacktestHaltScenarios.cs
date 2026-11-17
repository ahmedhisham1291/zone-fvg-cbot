using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Recovery;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-014 (owner decision C of 2026-11-17): with <see cref="BotParameters.BacktestHaltsLogOnly"/>
/// the kill switch, the emergency guard, the error limit, the request limit and the daily budget
/// only write "risk event=would-halt" or "risk event=would-block" once per cause and CE(S)T day, and
/// trading continues. Every other check of a new trade still refuses. The flag is refused outside a
/// backtest. Data: <see cref="RecoveryData"/>; Wednesday 2026-03-04, CE(S)T day from 2026-03-03 23:00
/// UTC; InitialCapital 100,000; emergency floor 91,000. The flag-off behaviour is proven by the
/// existing tests of <see cref="RiskActionScenarios"/>, whose fixtures leave the flag off.
/// </summary>
public class Rec014BacktestHaltScenarios
{
    private static readonly DateTime A = RecoveryData.Activation;
    private static readonly DateTime Nine = A.AddHours(1.0);

    private static BotParameters LogOnly(RiskParameters? risk = null)
    {
        return RecoveryData.Parameters() with { BacktestHaltsLogOnly = true, Risk = risk ?? RiskData.Parameters };
    }

    private static string Line(string eventName, string cause)
    {
        return " risk event=" + eventName + " cause=" + cause + " detail=";
    }

    private static ClosedTradeInfo Loss(long positionId, DateTime closeUtc, double net, double balanceAfter)
    {
        return new ClosedTradeInfo(positionId, RecoveryData.Symbol, "ZFVG|v1|USDJPY|D-20260302-0800", closeUtc.AddHours(-1.0), closeUtc, net, 0.0, 0.0, balanceAfter);
    }

    private static long AddReferenceOrder(FakeBroker broker)
    {
        return broker.AddOrder(RecoveryData.Symbol, Side.Buy, RecoveryData.Volume, RecoveryData.Entry, RecoveryData.Stop, RecoveryData.Target, A.AddHours(4.0), RecoveryData.Label, RecoveryData.Comment());
    }

    private static void AssertNoHaltAlerts(BotHarness h)
    {
        Assert.Equal(0, h.Alerts(AlertKind.KillSwitch));
        Assert.Equal(0, h.Alerts(AlertKind.EmergencyGuard));
        Assert.Equal(0, h.Alerts(AlertKind.CircuitBreaker));
        Assert.Equal(0, h.Alerts(AlertKind.ErrorStorm));
        Assert.Equal(0, h.Alerts(AlertKind.DailyLimit));
    }

    // ------------------------------------------------------------------ the flag itself

    [Fact]
    [Trait("Req", "REC-014")]
    public void Validate_FlagOnOutsideABacktest_ThrowsNamingTheFlag()
    {
        BotParameters parameters = LogOnly();

        ArgumentException error = Assert.Throws<ArgumentException>(() => parameters.Validate(isBacktest: false));

        Assert.Equal(nameof(BotParameters.BacktestHaltsLogOnly), error.ParamName);
        Assert.Contains("BacktestHaltsLogOnly", error.Message, StringComparison.Ordinal);
        parameters.Validate(isBacktest: true);
        RecoveryData.Parameters().Validate(isBacktest: false);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void Constructor_FlagOnInALiveRun_RefusesToStart()
    {
        var h = new BotHarness(isBacktest: false);

        ArgumentException error = Assert.Throws<ArgumentException>(() => h.NewCore(LogOnly()));

        Assert.Equal(nameof(BotParameters.BacktestHaltsLogOnly), error.ParamName);
        Assert.Empty(h.Journal.Entries);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void Default_FlagIsOff()
    {
        Assert.False(new BotParameters().BacktestHaltsLogOnly);
        Assert.False(RecoveryData.Parameters().BacktestHaltsLogOnly);
    }

    // ------------------------------------------------------------------ kill switch

    [Fact]
    [Trait("Req", "REC-014")]
    public void KillSwitch_8PercentBelowThePeak_RecordedOncePerCestDayAndTheSignalIsPlaced()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A, LogOnly());
        h.FeedTo(A.AddMinutes(15.0));
        h.Broker.Account = ExecutionData.Account(92000.0, 92000.0, 92000.0);
        h.Broker.AddClosedTrade(Loss(901, RecoveryData.At(2, 15, 0), -8000.0, 92000.0));

        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(BotState.Running, core.State);
        Assert.Empty(core.HaltCauses);
        Assert.Equal(1, h.Journal.Count("risk", Line("would-halt", "KillSwitch") + "kill\\sswitch:"));
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.NotNull(h.ReferenceOrder());
        Assert.Equal(0, h.Count(BrokerCall.Cancel));

        // A second M15 bar of the same CE(S)T day writes no second line.
        h.FeedTo(Nine);
        Assert.Equal(1, h.Journal.Count("risk", Line("would-halt", "KillSwitch")));

        // 23:00 UTC is the first bar of the next CE(S)T day (5 March 00:00 CET).
        h.FeedTo(RecoveryData.At(4, 22, 45));
        Assert.Equal(1, h.Journal.Count("risk", Line("would-halt", "KillSwitch")));
        h.FeedTo(RecoveryData.At(4, 23, 0));
        Assert.Equal(2, h.Journal.Count("risk", Line("would-halt", "KillSwitch")));
        Assert.Equal(BotState.Running, core.State);
        AssertNoHaltAlerts(h);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void KillSwitch_AtStart_RecordedAndNothingCancelled()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(92000.0, 92000.0, 92000.0)));
        h.Broker.AddClosedTrade(Loss(901, RecoveryData.At(2, 15, 0), -8000.0, 92000.0));
        long order = AddReferenceOrder(h.Broker);

        BotCore core = h.Start(Nine, LogOnly());

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(new[] { order }, core.LastReport!.OrdersKept);
        Assert.Empty(core.LastReport.CatchUp);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(1, h.Journal.Count("risk", Line("would-halt", "KillSwitch")));
        AssertNoHaltAlerts(h);
    }

    // ------------------------------------------------------------------ emergency guard

    [Fact]
    [Trait("Req", "REC-014")]
    public void EmergencyGuard_Equity90999_ClosesAndCancelsNothingAndRecordsOnce()
    {
        var h = new BotHarness();
        h.Start(A, LogOnly());
        h.FeedTo(RecoveryData.Confirmation);
        h.FillReferenceOrder(A.AddMinutes(50.0));
        long other = h.Broker.AddOrder(RecoveryData.Symbol, Side.Sell, 1000.0, 101.5, 102.0, 100.0, A.AddHours(4.0), "ZFVG|v1|USDJPY|S-20260101-0000", RecoveryData.Comment());
        int writes = h.Broker.WriteRequestCount;
        h.Broker.Account = ExecutionData.Account(100000.0, 90999.0, 90000.0);

        h.Timer(A.AddMinutes(52.0));
        h.FeedTo(Nine);

        Assert.Equal(writes, h.Broker.WriteRequestCount);
        Assert.NotNull(h.ReferencePosition());
        Assert.Contains(h.Broker.GetPendingOrders(), order => order.Id == other);
        Assert.Equal(BotState.Running, h.Core.State);
        Assert.Equal(1, h.Journal.Count("risk", Line("would-halt", "EmergencyGuard") + "emergency\\sguard:"));
        Assert.Equal(0, h.Journal.Count("risk", Line("would-halt", "KillSwitch")));
        AssertNoHaltAlerts(h);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void EmergencyGuard_AtStart_ClosesNothing()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(100000.0, 90999.0, 90000.0)));
        long position = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, RecoveryData.Volume, RecoveryData.Entry, RecoveryData.Stop, RecoveryData.Target, RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(Nine, LogOnly());

        Assert.Equal(BotState.Running, core.State);
        Assert.Contains(h.Broker.GetPositions(), p => p.Id == position);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(1, h.Journal.Count("risk", Line("would-halt", "EmergencyGuard")));
    }

    // ------------------------------------------------------------------ error limit

    [Fact]
    [Trait("Req", "REC-014")]
    public void ErrorLimit_SixErrorsWithinAnHour_NoHaltOneLine()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A, LogOnly());
        h.Port.PositionsFault = new InvalidOperationException("positions unavailable");

        for (int i = 0; i < 7; i++)
        {
            core.OnTradeEvent();
        }

        Assert.Equal(BotState.Running, core.State);
        Assert.Empty(core.HaltCauses);
        Assert.Equal(1, h.Journal.Count("risk", Line("would-halt", "ErrorLimit") + "error\\sstorm:"));
        Assert.Equal(0, h.Alerts(AlertKind.ErrorStorm));
    }

    // ------------------------------------------------------------------ request limit

    [Fact]
    [Trait("Req", "REC-014")]
    public void RequestLimit_TwentyFirstRequestInAnHour_IsSentAndRecorded()
    {
        // Recovery cancels 20 orders without an expiry at 08:00; the entry at 08:45 is the 21st request.
        var h = new BotHarness();
        for (int i = 0; i < 20; i++)
        {
            h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.0, 99.0, 102.0, null, "ZFVG|v1|USDJPY|X-" + i, RecoveryData.Comment());
        }

        BotCore core = h.Start(A, LogOnly());
        Assert.Equal(20, h.Count(BrokerCall.Cancel));

        h.FeedTo(A.AddHours(2.0));

        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.NotNull(h.ReferenceOrder());
        Assert.Equal(BotState.Running, core.State);
        Assert.Empty(core.HaltCauses);
        Assert.Equal(1, h.Journal.Count("risk", Line("would-halt", "RequestLimit") + "request\\slimit:"));
        Assert.Equal(0, h.Journal.Count("skip", "reason=RequestLimitReached"));
        Assert.Equal(0, h.Alerts(AlertKind.RequestLimitReached));
        AssertNoHaltAlerts(h);
        ScenarioAssert.Global(h.Broker, 21);
    }

    // ------------------------------------------------------------------ daily budget

    [Fact]
    [Trait("Req", "REC-014")]
    public void DailyBudget_ExhaustedWhileRunning_NoBlockNoCancelNothingStoredOneLine()
    {
        // As RiskActionScenarios: at the 09:15 close M - W = 1,100 + 489.13 = 1,589.13 > 1,500.
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(99100.0, 99100.0, 99100.0)));
        h.Broker.AddClosedTrade(Loss(901, A.AddHours(-1.0), -900.0, 99100.0));
        long order = AddReferenceOrder(h.Broker);
        BotCore core = h.Start(Nine, LogOnly());
        h.Broker.AdvanceTo(A.AddMinutes(70.0));
        h.Broker.Account = ExecutionData.Account(98900.0, 98900.0, 98900.0);
        h.Broker.AddClosedTrade(Loss(902, A.AddMinutes(70.0), -200.0, 98900.0));

        h.FeedTo(A.AddMinutes(75.0));
        h.FeedTo(A.AddHours(3.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.False(core.BudgetBlocked);
        Assert.Contains(h.Broker.GetPendingOrders(), o => o.Id == order);
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.False(h.Store.Values.ContainsKey(BotCore.BudgetExhaustedKey));
        Assert.Equal(1, h.Journal.Count("risk", Line("would-block", "DailyBudget") + "daily\\srisk\\sbudget\\sexhausted:"));
        AssertNoHaltAlerts(h);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void DailyBudget_ExhaustedAtStart_NothingCancelledAndRunning()
    {
        // As RecoveryScenarios.CatchUp_DailyBudgetBreached_PendingOrdersCancelled, with the flag on.
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(98400.0, 98400.0, 98400.0)));
        h.Broker.AddClosedTrade(Loss(900, A.AddHours(-1.0), -1600.0, 98400.0));
        long order = h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.675, 100.185, 103.061, A.AddHours(4.0), RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(Nine, LogOnly());

        Assert.Empty(core.LastReport!.CatchUp);
        Assert.Contains(h.Broker.GetPendingOrders(), o => o.Id == order);
        Assert.Equal(BotState.Running, core.State);
        Assert.False(h.Store.Values.ContainsKey(BotCore.BudgetExhaustedKey));
        Assert.Equal(1, h.Journal.Count("risk", Line("would-block", "DailyBudget")));
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void DailyBudget_StoredDayOfToday_DoesNotBlock()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine));
        h.Store.Values[BotCore.BudgetExhaustedKey] = "2026-03-03T23:00:00.0000000Z";
        AddReferenceOrder(h.Broker);

        BotCore core = h.Start(Nine, LogOnly());

        Assert.Equal(BotState.Running, core.State);
        Assert.False(core.BudgetBlocked);
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Equal(1, h.Journal.Count("risk", Line("would-block", "DailyBudget") + "daily\\srisk\\sbudget\\swas\\sexhausted\\searlier\\stoday"));
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void DailyBudget_NewTradeAboveTheBudget_IsPlacedWithOneLine()
    {
        // Lost 1,100 today: M - W = 1,100 before the trade, 1,100 + 489.13 = 1,589.13 with it.
        var h = new BotHarness(broker: BotHarness.NewBroker(A, ExecutionData.Account(98900.0, 98900.0, 98900.0)));
        h.Broker.AddClosedTrade(Loss(901, A.AddHours(-1.0), -1100.0, 98900.0));
        BotCore core = h.Start(A, LogOnly());

        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.NotNull(h.ReferenceOrder());
        Assert.Equal(0, h.Journal.Count("skip", "reason=DailyBudgetExceeded"));
        Assert.Equal(1, h.Journal.Count("risk", Line("would-block", "DailyBudget") + "daily\\srisk\\sbudget:\\sworst-case\\sdaily\\sloss"));

        // The next bars of the day see the budget exhausted by the new order: still one line, no cancel.
        h.FeedTo(A.AddHours(2.0));
        Assert.Equal(1, h.Journal.Count("risk", Line("would-block", "DailyBudget")));
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Equal(BotState.Running, core.State);
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void DailyBudget_NewTradeAboveTheBudgetWithTheFlagOff_IsSkipped()
    {
        // The same trade live: refused by the budget check of RSK-006 (no new behaviour; shown for comparison).
        var h = new BotHarness(broker: BotHarness.NewBroker(A, ExecutionData.Account(98900.0, 98900.0, 98900.0)));
        h.Broker.AddClosedTrade(Loss(901, A.AddHours(-1.0), -1100.0, 98900.0));
        h.Start(A);

        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Journal.Count("skip", "reason=DailyBudgetExceeded"));
        Assert.Equal(0, h.Journal.Count("risk", "event=would-"));
    }

    // ------------------------------------------------------------------ other checks still refuse

    [Fact]
    [Trait("Req", "REC-014")]
    public void SanityCap_RiskAbove055Percent_StillSkipsWithTheFlagOn()
    {
        var h = new BotHarness();
        h.Start(A, LogOnly(RiskData.Parameters with { RiskPercent = 0.6 }));

        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Journal.Count("skip", "reason=RiskAboveSanityCap"));
        Assert.Equal(1, h.Alerts(AlertKind.SanityCap));
    }

    [Fact]
    [Trait("Req", "REC-014")]
    public void EntryLimit_ThirdEntryOnTheSymbolToday_StillSkipsWithTheFlagOn()
    {
        var h = new BotHarness();
        h.Broker.AddClosedTrade(new ClosedTradeInfo(801, RecoveryData.Symbol, "ZFVG|v1|USDJPY|D-20260303-0000", RecoveryData.At(4, 1, 0), RecoveryData.At(4, 2, 0), 0.0, 0.0, 0.0, 100000.0));
        h.Broker.AddClosedTrade(new ClosedTradeInfo(802, RecoveryData.Symbol, "ZFVG|v1|USDJPY|D-20260303-0400", RecoveryData.At(4, 3, 0), RecoveryData.At(4, 4, 0), 0.0, 0.0, 0.0, 100000.0));

        h.Start(A, LogOnly());
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Journal.Count("skip", "reason=SymbolEntryLimitReached"));
    }
}
