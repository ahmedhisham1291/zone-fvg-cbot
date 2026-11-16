using System.Globalization;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The recovery procedure of spec 8.3 through <see cref="BotCore"/> and a <see cref="FakeBroker"/>
/// (REC-002 to REC-010). Data: <see cref="RecoveryData"/>. A core started at 09:00 or later
/// replays the reference signal of 08:45, so the zone D-20260303-1600 is known.
/// </summary>
public class RecoveryScenarios
{
    private static readonly DateTime A = RecoveryData.Activation;
    private static readonly DateTime Nine = A.AddHours(1.0);
    private static readonly DateTime OrderExpiry = A.AddHours(4.0);

    private static string Id(long id)
    {
        return id.ToString(CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ REC-002

    [Fact]
    [Trait("Req", "REC-002")]
    public void Recovery_RunTwiceWithNothingToRepair_IdenticalReportsAndNoBrokerWrites()
    {
        var h = new BotHarness();
        h.Start(A);
        h.FeedTo(RecoveryData.Confirmation);
        h.FillReferenceOrder(A.AddMinutes(50.0));
        h.Broker.AdvanceTo(Nine);
        int writes = h.Broker.WriteRequestCount;

        h.Core.OnConnected(h.History);
        string first = h.Core.LastReport!.Text;
        h.Core.OnConnected(h.History);
        string second = h.Core.LastReport!.Text;
        h.Core.OnStart(h.History);
        string fromStart = h.Core.LastReport!.Text;

        Assert.Equal(first, second);
        Assert.Equal(first, fromStart);
        Assert.Equal(writes, h.Broker.WriteRequestCount);
        Assert.Equal(BotState.Running, h.Core.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-002")]
    public void Recovery_SecondRunAfterRepairs_WritesNothingAndRepeatsItsReport()
    {
        var h = new BotHarness();
        h.Start(A);
        h.FeedTo(RecoveryData.Confirmation);
        h.FillReferenceOrder(A.AddMinutes(50.0));
        PositionInfo position = h.ReferencePosition()!;
        h.Broker.ModifyPositionProtection(position.Id, null, position.TakeProfit);
        long invalid = h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.0, 99.0, 102.0, null, "ZFVG|v1|USDJPY|D-20260101-0000", RecoveryData.Comment());
        h.Broker.AdvanceTo(Nine);

        h.Core.OnConnected(h.History);
        int afterFirst = h.Broker.WriteRequestCount;
        h.Core.OnConnected(h.History);
        string second = h.Core.LastReport!.Text;
        h.Core.OnConnected(h.History);
        string third = h.Core.LastReport!.Text;

        Assert.Equal(afterFirst, h.Broker.WriteRequestCount);
        Assert.Equal(second, third);
        Assert.Equal(RecoveryData.Stop, h.ReferencePosition()!.StopLoss);
        Assert.DoesNotContain(h.Broker.GetPendingOrders(), order => order.Id == invalid);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ REC-003

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_TooFewBars_StaysRecoveringRequestsMoreOnceAndAlertsOnce()
    {
        var h = new BotHarness();
        h.History.Available[TimeFrame.M15] = 1000;

        BotCore core = h.Start(A);

        Assert.Equal(BotState.Recovering, core.State);
        Assert.False(core.LastReport!.DataComplete);
        Assert.Equal(new[] { "M15: 1000 bars, 1968 needed" }, core.LastReport.DataProblems);
        Assert.Equal(new[] { (TimeFrame.M15, 3000) }, h.History.MoreRequests);
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.DataIncomplete));

        // Bars are ignored while Recovering; a retry after 15 minutes is still incomplete and does not alert again.
        h.FeedTo(A.AddMinutes(15.0));
        h.Timer(A.AddMinutes(15.0));
        Assert.Equal(BotState.Recovering, core.State);
        Assert.Equal(2, h.History.MoreRequests.Count);
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.DataIncomplete));
        Assert.Empty(core.Zones);

        // Once more history can be loaded, the next retry completes; the signal of 08:45 is replayed, not executed.
        h.History.RequestMoreHelps = true;
        h.FeedTo(RecoveryData.Confirmation);
        h.Timer(A.AddMinutes(46.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.True(core.LastReport!.DataComplete);
        Assert.Equal(1, core.LastReport.ReplaySignals);
        Assert.Equal(0, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_RetryBeforeFifteenMinutes_DoesNotRun()
    {
        var h = new BotHarness();
        h.History.Available[TimeFrame.H4] = 100;
        BotCore core = h.Start(A);

        h.Timer(A.AddMinutes(14.0));

        Assert.Single(h.History.MoreRequests);
        Assert.Equal(BotState.Recovering, core.State);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_MissingBarOnAWeekday_StaysRecovering()
    {
        var h = new BotHarness();
        h.History.Missing.Add(A.AddHours(-2.0));

        BotCore core = h.Start(A);

        Assert.Equal(BotState.Recovering, core.State);
        Assert.Equal(new[] { "M15: gap between 2026-03-04 05:45 and 2026-03-04 06:15" }, core.LastReport!.DataProblems);
        Assert.Equal(new[] { (TimeFrame.M15, 3000) }, h.History.MoreRequests);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_MissingDayThatIsAMarketHoliday_IsNotAGap()
    {
        // Every M15 bar of Tuesday 2026-03-03 is missing. With 3 March as a holiday the day counts as closed.
        var h = new BotHarness();
        foreach (Bar bar in h.Bars.M15.Where(bar => bar.OpenTimeUtc.Date == new DateTime(2026, 3, 3)))
        {
            h.History.Missing.Add(bar.OpenTimeUtc);
        }

        BotCore withHoliday = h.Start(A, RecoveryData.Parameters() with { MarketHolidays = "12-25,01-01,03-03" });
        Assert.Equal(BotState.Running, withHoliday.State);

        BotCore withoutHoliday = h.Start(A.AddMinutes(1.0));
        Assert.Equal(BotState.Recovering, withoutHoliday.State);
        Assert.Equal(new[] { "M15: gap between 2026-03-02 23:45 and 2026-03-04 00:00" }, withoutHoliday.LastReport!.DataProblems);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void LiveGap_OverAMarketHoliday_IsNotAGap()
    {
        // 4 March is declared a holiday: m1 and m2 may be missing on it without blocking entries.
        var h = new BotHarness();
        BotCore core = h.Start(A, RecoveryData.Parameters() with { MarketHolidays = "03-04" });
        h.FeedTo(A.AddMinutes(15.0));
        Bar m3 = h.Bars.M15.Single(bar => bar.OpenTimeUtc == A.AddMinutes(45.0));
        h.Broker.AdvanceTo(A.AddMinutes(60.0));

        core.OnBarClosed(TimeFrame.M15, m3);

        Assert.False(core.DataGap);
        Assert.Equal(BotState.Running, core.State);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_BrokerNotConnected_StaysRecoveringWithoutRequests()
    {
        var h = new BotHarness();
        h.Broker.IsConnected = false;

        BotCore core = h.Start(A);

        Assert.Equal(BotState.Recovering, core.State);
        Assert.Equal(new[] { "broker not connected" }, core.LastReport!.DataProblems);
        Assert.Empty(h.History.MoreRequests);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Recovery_IncompleteData_StillRepairsAMissingStop()
    {
        var h = new BotHarness();
        h.History.Available[TimeFrame.D1] = 10;
        h.Broker.Tick(RecoveryData.Symbol, 100.66, 100.67);
        h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, null, 103.061, RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(A);

        Assert.Equal(BotState.Recovering, core.State);
        Assert.Equal(RecoveryData.Stop, h.ReferencePosition()!.StopLoss);
        Assert.Single(core.LastReport!.Protection);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void LiveGap_BlocksEntriesUntilRecoveryReloadsTheHistory()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.FeedTo(A.AddMinutes(15.0));

        // m1 (08:15) never arrives; m2 closes at 08:45.
        Bar m2 = h.Bars.M15.Single(bar => bar.OpenTimeUtc == A.AddMinutes(30.0));
        h.Broker.AdvanceTo(RecoveryData.Confirmation);
        core.OnBarClosed(TimeFrame.M15, m2);

        Assert.Equal(BotState.Blocked, core.State);
        Assert.True(core.DataGap);
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.DataGap));

        h.Timer(A.AddMinutes(46.0));

        Assert.Equal(BotState.Running, core.State);
        Assert.False(core.DataGap);
        Assert.Equal(1, core.LastReport!.ReplaySignals);
        Assert.Equal(0, h.Count(BrokerCall.Place));
    }

    // ------------------------------------------------------------------ REC-004

    [Fact]
    [Trait("Req", "REC-004")]
    public void Replay_SignalsAreRecordedAsNotExecutable()
    {
        var h = new BotHarness();

        BotCore core = h.Start(Nine);

        Assert.Equal(1, core.LastReport!.ReplaySignals);
        Assert.Equal(1, h.Journal.Count("skip", "zone=" + RecoveryData.ZoneId + " reason=SignalFromReplay"));
        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(BotState.Running, core.State);
    }

    [Theory]
    [Trait("Req", "REC-004")]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(30)]
    public void Replay_AfterARestart_ReachesTheSameZonesAndDecisionsAsABotThatNeverStopped(int restartMinutes)
    {
        DateTime restart = A.AddMinutes(restartMinutes);
        var live = new BotHarness(broker: BotHarness.NewBroker(A.AddHours(-8.0)));
        live.Start(A.AddHours(-8.0));
        live.FeedTo(restart);
        var restarted = new BotHarness();
        restarted.Start(restart);

        Assert.Equal(live.Core.Zones, restarted.Core.Zones);
        Assert.Equal(live.Core.Bias, restarted.Core.Bias);
        Assert.Equal(Bias.Long, restarted.Core.Bias);
        Assert.NotEmpty(restarted.Core.Zones);

        live.FeedTo(A.AddHours(2.0));
        restarted.FeedTo(A.AddHours(2.0));

        BrokerRequest expected = Assert.Single(live.Broker.Requests);
        BrokerRequest actual = Assert.Single(restarted.Broker.Requests);
        Assert.Equal(expected, actual);
        Assert.Equal(live.Core.Zones, restarted.Core.Zones);
    }

    // ------------------------------------------------------------------ REC-005

    [Fact]
    [Trait("Req", "REC-005")]
    public void Adopt_PositionWithoutStop_StopSetFromItsCommentWithAlerts()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine));
        h.Broker.Tick(RecoveryData.Symbol, 100.66, 100.67);
        long id = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, null, 103.061, RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(Nine);

        Assert.Equal(new[] { id }, core.LastReport!.PositionsAdopted);
        Assert.Equal(new[] { Id(id) + " StopRestored (stop 99.482)" }, core.LastReport.Protection);
        Assert.Equal(RecoveryData.Stop, h.ReferencePosition()!.StopLoss);
        Assert.Equal(1, h.Hooks.Count(AlertKind.StopLossMissing));
        Assert.Equal(1, h.Hooks.Count(AlertKind.StopLossRestored));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-005")]
    public void Adopt_StopThatCannotBeSet_PositionClosedWithAnAlert()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine));
        h.Broker.Tick(RecoveryData.Symbol, 100.66, 100.67);
        long id = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, null, 103.061, RecoveryData.Label, RecoveryData.Comment());
        h.Broker.Script(BrokerCall.Modify, BrokerFault.Rejected);
        h.Broker.Script(BrokerCall.Modify, BrokerFault.Rejected);

        BotCore core = h.Start(Nine);

        Assert.Null(h.ReferencePosition());
        Assert.Equal(2, h.Count(BrokerCall.Modify));
        Assert.Equal(1, h.Count(BrokerCall.Close));
        Assert.Equal(new[] { Id(id) + " Closed (stop loss could not be set in 2 attempts)" }, core.LastReport!.Protection);
        Assert.Equal(1, h.Hooks.Count(AlertKind.PositionClosedWithoutStop));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-005")]
    public void Adopt_PositionWithoutTarget_IsReportedAndKept()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine));
        long id = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, 99.482, null, RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(Nine);

        Assert.Equal(new[] { id }, core.LastReport!.PositionsAdopted);
        Assert.Equal(new[] { id }, core.LastReport.WithoutTakeProfit);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    // ------------------------------------------------------------------ REC-006

    [Theory]
    [Trait("Req", "REC-006")]
    [InlineData(true, true, true, RecoveryData.Label, null)]
    [InlineData(false, true, true, RecoveryData.Label, "no stop loss")]
    [InlineData(true, false, true, RecoveryData.Label, "no take profit")]
    [InlineData(true, true, false, RecoveryData.Label, "no expiry")]
    [InlineData(true, true, true, "ZFVG|v1|USDJPY|D-20260101-0000", "zone D-20260101-0000 unknown or ended")]
    [InlineData(true, true, true, "ZFVG|v2|USDJPY|D-20260303-1600", "label not readable")]
    public void PendingOrder_KeptOnlyWithStopTargetExpiryAndAKnownZone(bool stop, bool target, bool expiry, string label, string? reason)
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine));
        long id = h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.675, stop ? 99.482 : null, target ? 103.061 : null, expiry ? OrderExpiry : null, label, RecoveryData.Comment());

        BotCore core = h.Start(Nine);

        if (reason == null)
        {
            Assert.Equal(new[] { id }, core.LastReport!.OrdersKept);
            Assert.Empty(core.LastReport.OrdersCancelled);
            Assert.Equal(0, h.Broker.WriteRequestCount);
        }
        else
        {
            Assert.Empty(core.LastReport!.OrdersKept);
            Assert.Equal(new[] { Id(id) + " (" + reason + ")" }, core.LastReport.OrdersCancelled);
            Assert.Empty(h.Broker.GetPendingOrders());
            Assert.Equal(1, h.Count(BrokerCall.Cancel));
        }

        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-006")]
    public void PendingOrder_CancelThatFails_IsReportedAndAlerted()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine));
        long id = h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.675, 99.482, 103.061, null, RecoveryData.Label, RecoveryData.Comment());
        h.Broker.Script(BrokerCall.Cancel, BrokerFault.Rejected, "Busy");

        BotCore core = h.Start(Nine);

        Assert.Equal(new[] { Id(id) + " (no expiry, cancel Rejected)" }, core.LastReport!.OrdersCancelled);
        Assert.Equal(1, h.Hooks.Count(AlertKind.CancelFailed));
        Assert.Single(h.Broker.GetPendingOrders());
    }

    // ------------------------------------------------------------------ REC-007

    [Fact]
    [Trait("Req", "REC-007")]
    public void Stranger_KeptReportedCountedInRiskAndClosedOnFriday()
    {
        // A position of an unknown zone risks 1,600 at its stop (160,000 x 100 pips x 0.0001): M - W = 1,600 > 1,500.
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine));
        long id = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 160000.0, 100.0, 99.0, 102.0, "ZFVG|v1|USDJPY|D-20260101-0000", RecoveryData.Comment());

        BotCore core = h.Start(Nine);

        Assert.Equal(new[] { Id(id) + " (zone D-20260101-0000 unknown)" }, core.LastReport!.Strangers);
        Assert.Empty(core.LastReport.PositionsAdopted);
        Assert.Equal(1, h.Hooks.Count(BotAlertKind.Stranger));
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(BotState.Blocked, core.State);
        Assert.True(core.BudgetBlocked);

        h.Timer(RecoveryData.At(6, 20, 0));

        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(1, h.Count(BrokerCall.Close));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-007")]
    public void Stranger_WithAnUnreadableLabelOrComment_IsKept()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine));
        long first = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 1000.0, 100.0, 99.0, 102.0, "ZFVG|v9|USDJPY", "x");
        long second = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 1000.0, 100.0, 99.0, 102.0, RecoveryData.Label, "not a plan");

        BotCore core = h.Start(Nine);

        Assert.Equal(new[] { Id(first) + " (label not readable)", Id(second) + " (comment not readable)" }, core.LastReport!.Strangers);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    // ------------------------------------------------------------------ REC-008

    [Fact]
    [Trait("Req", "REC-008")]
    public void TradeJournal_TradeClosedWhileRunning_JournaledOnceOnTradeEvents()
    {
        var h = new BotHarness();
        h.Start(A);
        h.FeedTo(RecoveryData.Confirmation);
        h.FillReferenceOrder(A.AddMinutes(50.0));
        h.Core.OnTradeEvent();
        h.Broker.AdvanceTo(A.AddMinutes(70.0));
        h.Broker.Tick(RecoveryData.Symbol, 103.07, 103.08);
        h.Core.OnTradeEvent();
        h.Core.OnTradeEvent();
        h.Start(A.AddMinutes(80.0));

        Assert.Equal(1, h.Journal.Count(TradeJournal.Category, "event=opened"));
        Assert.Equal(1, h.Journal.Count(TradeJournal.Category, "event=closed"));
    }

    // ------------------------------------------------------------------ REC-009

    [Fact]
    [Trait("Req", "REC-009")]
    public void CatchUp_StartOnSaturday_ClosesAndCancelsAtTheFirstEventAfterTheReopenNotBefore()
    {
        DateTime saturday = RecoveryData.At(7, 10, 0);
        var h = new BotHarness(broker: BotHarness.NewBroker(saturday, marketOpen: false));
        long position = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());
        long order = h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.675, 99.482, 103.061, RecoveryData.At(9, 10, 0), RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(saturday);
        h.Timer(RecoveryData.At(7, 18, 0));
        h.Timer(RecoveryData.At(8, 21, 59));

        Assert.True(core.LastReport!.WeekendClosePending);
        Assert.Equal(new[] { position }, core.LastReport.PositionsAdopted);
        Assert.Equal(new[] { order }, core.LastReport.OrdersKept);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.True(core.WeekendClosePending);

        DateTime reopen = RecoveryData.At(8, 22, 0);
        h.Broker.SetSymbol(RecoveryData.UsdJpy(marketOpen: true));
        h.Timer(reopen);

        Assert.Empty(h.Broker.GetPositions());
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(new[] { BrokerCall.Cancel, BrokerCall.Close }, h.Broker.Requests.Select(r => r.Call));
        Assert.All(h.Broker.Requests, request => Assert.Equal(reopen, request.AtUtc));
        Assert.False(core.WeekendClosePending);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-009")]
    public void CatchUp_StartOnFridayAfterTheCloseWithTheMarketOpen_ClosesAndCancelsAtOnce()
    {
        DateTime friday = RecoveryData.At(6, 20, 30);
        var h = new BotHarness(broker: BotHarness.NewBroker(friday));
        h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());
        h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.675, 99.482, 103.061, RecoveryData.At(6, 22, 0), RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(friday);

        Assert.Empty(h.Broker.GetPositions());
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(2, core.LastReport!.CatchUp.Count);
        Assert.False(core.LastReport.WeekendClosePending);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-009")]
    public void CatchUp_StartOnAWeekday_ClosesNothing()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine));
        h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(Nine);

        Assert.Empty(core.LastReport!.CatchUp);
        Assert.False(core.WeekendClosePending);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "REC-009")]
    public void CatchUp_DailyBudgetBreached_PendingOrdersCancelled()
    {
        // Lost 1,600 today: M = 100,000, W = 98,400 - 4.9 (the order's 1,000 units x 49 pips x 0.0001 at its stop).
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine, ExecutionData.Account(98400.0, 98400.0, 98400.0)));
        h.Broker.AddClosedTrade(new ClosedTradeInfo(900, RecoveryData.Symbol, "ZFVG|v1|USDJPY|D-20260302-0800", A.AddHours(-3.0), A.AddHours(-1.0), -1600.0, 0.0, 0.0, 98400.0));
        long order = h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.675, 100.185, 103.061, OrderExpiry, RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(Nine);

        Assert.Equal(new[] { Id(order) + " Cancelled (daily budget exhausted)" }, core.LastReport!.CatchUp);
        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Equal(BotState.Blocked, core.State);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ REC-010

    [Fact]
    [Trait("Req", "REC-010")]
    public void Report_OnACleanStart_HasEveryFieldInAFixedForm()
    {
        var h = new BotHarness(broker: BotHarness.NewBroker(Nine));

        BotCore core = h.Start(Nine);

        string expected = "symbol=USDJPY at=2026-03-04T09:00:00Z downtime=unknown data=complete[] zones=[] replay-signals=1"
            + " adopted=[] strangers=[] without-target=[] protection=[] orders-kept=[] orders-cancelled=[] journaled=0"
            + " risk={emergency=ok equity 100000 daily floor 96000 overall floor 91000; kill-switch=ok peak 100000 drawdown 0 limit 8000;"
            + " daily-budget=ok used 0 limit 1500; data=ok} catch-up=[] weekend-close-pending=no state=Running news=enabled hash="
            + RecoveryData.Hash + " version=" + BuildInfo.Version;
        Assert.Equal(expected, core.LastReport!.Text);
        Assert.Equal(1, h.Journal.Count("recovery", "reason=start " + expected));
        BotAlert alert = Assert.Single(h.Hooks.Alerts, a => a.Kind == BotAlertKind.RecoveryReport);
        Assert.Equal(expected, alert.Message);
        Assert.Equal(Nine, alert.AtUtc);
    }

    [Fact]
    [Trait("Req", "REC-010")]
    public void Report_AfterAnOutage_ListsDowntimeAdoptedKeptCancelledStrangersZonesAndState()
    {
        var h = new BotHarness();
        h.Start(A);
        h.FeedTo(A.AddMinutes(30.0));
        h.Timer(A.AddMinutes(31.0));
        long kept = h.Broker.AddOrder(RecoveryData.Symbol, Side.Buy, 1000.0, 100.675, 99.482, 103.061, OrderExpiry, RecoveryData.Label, RecoveryData.Comment());
        long cancelled = h.Broker.AddOrder(RecoveryData.Symbol, Side.Sell, 1000.0, 101.5, 102.0, 100.0, OrderExpiry, "ZFVG|v1|USDJPY|S-20260101-0000", RecoveryData.Comment());
        long stranger = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 1000.0, 100.0, 99.0, 102.0, "ZFVG|v1|USDJPY|D-20260102-0000", RecoveryData.Comment());
        long adopted = h.Broker.AddPosition(RecoveryData.Symbol, Side.Buy, 1000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(A.AddMinutes(61.0));
        RecoveryReport report = core.LastReport!;

        Assert.Equal(TimeSpan.FromMinutes(30.0), report.Downtime);
        Assert.Equal(new[] { adopted }, report.PositionsAdopted);
        Assert.Equal(new[] { kept }, report.OrdersKept);
        Assert.Equal(new[] { Id(cancelled) + " (zone S-20260101-0000 unknown or ended)" }, report.OrdersCancelled);
        Assert.Equal(new[] { Id(stranger) + " (zone D-20260102-0000 unknown)" }, report.Strangers);
        Assert.Empty(report.ActiveZones);
        Assert.Equal(BotState.Running, report.State);
        Assert.True(report.NewsEnabled);
        Assert.Contains("downtime=30min", report.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "REC-010")]
    public void Report_BeforeTheTouch_ListsTheActiveZone()
    {
        var h = new BotHarness();

        BotCore core = h.Start(A);

        Assert.Equal(new[] { RecoveryData.ZoneId }, core.LastReport!.ActiveZones);
        Assert.Equal(0, core.LastReport.ReplaySignals);
        Assert.Equal(RecoveryData.ZoneId, Assert.Single(core.Zones).Id);
    }
}
