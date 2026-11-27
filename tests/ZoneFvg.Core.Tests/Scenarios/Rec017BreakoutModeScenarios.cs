using System.Globalization;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Signals;
using Data = ZoneFvg.Core.Tests.BreakoutRecoveryData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-017: Breakout mode end to end through <see cref="BotCore"/> and a <see cref="FakeBroker"/>,
/// live and across a restart. Data and every expected value: <see cref="BreakoutRecoveryData"/>.
/// The harness parameters are the spec defaults (default signal, execution and breakout
/// parameters), so the parameter hash is 119a3864 in Breakout mode and f681dff2 in ZoneFvg mode.
/// </summary>
public class Rec017BreakoutModeScenarios
{
    private const string Symbol = RecoveryData.Symbol;

    private static readonly DateTime Thursday = Data.Wednesday.AddDays(1.0);
    private static readonly DateTime Friday = Data.Wednesday.AddDays(2.0);

    private static BotHarness Started(DateTime atUtc, BotParameters? parameters = null, bool sell = false, BarSet? bars = null, AccountSnapshot? account = null)
    {
        BotHarness h = Data.Harness(atUtc, sell, bars, account);
        h.Start(atUtc, parameters ?? Data.Parameters());
        return h;
    }

    private static string Id(long id)
    {
        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static StopOrderRequest BuyRequest()
    {
        return new StopOrderRequest(Symbol, Side.Buy, Data.Volume, Data.Entry, Data.Stop, Data.Target, Data.Expiry, Data.BuyLabel, Data.BuyComment());
    }

    // The Wednesday buy fills: the ask reaches the entry 100.37.
    private static void FillBuy(BotHarness h, DateTime atUtc)
    {
        h.Broker.AdvanceTo(atUtc);
        h.Broker.Tick(Symbol, 100.36, 100.37);
        h.Core.OnTradeEvent();
    }

    // The bid reaches the target 101.16 of the Wednesday buy.
    private static void HitBuyTarget(BotHarness h, DateTime atUtc)
    {
        h.Broker.AdvanceTo(atUtc);
        h.Broker.Tick(Symbol, 101.16, 101.17);
        h.Core.OnTradeEvent();
    }

    private static void AssertBuyPosition(BotHarness h)
    {
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(Data.BuyLabel, position.Label);
        Assert.Equal(Side.Buy, position.Side);
        Assert.Equal(Data.Entry, position.EntryPrice);
        Assert.Equal(Data.Stop, position.StopLoss);
        Assert.Equal(Data.Target, position.TakeProfit);
    }

    private static string TradeLine(BotHarness h)
    {
        return Assert.Single(h.Journal.Lines("trade"), line => line.Contains(" trade symbol=", StringComparison.Ordinal));
    }

    // A stranger's reason (REC-007): its zone id is unknown or its parameter hash differs; which one comes first is not fixed by the spec.
    private static void AssertStranger(string reported, long id, string breakoutId, string hash)
    {
        Assert.StartsWith(Id(id) + " (", reported, StringComparison.Ordinal);
        Assert.True(
            reported.Contains("zone " + breakoutId + " unknown", StringComparison.Ordinal) || reported.Contains("parameter hash " + hash + " differs", StringComparison.Ordinal),
            "stranger reason: " + reported);
    }

    // ------------------------------------------------------------------ live

    [Fact]
    [Trait("Req", "REC-017")]
    public void LiveBuy_CloseAboveTheReferenceHigh_OneStopOrderThatFillsKeepsItsProtectionAndClosesAtTheTarget()
    {
        BotHarness h = Started(Data.Start, Data.Parameters() with { WriteTradeLog = true });
        h.Broker.ReportExitReasons = true;

        h.FeedTo(Data.Confirmation.AddMinutes(-15.0));
        Assert.Empty(h.Broker.Requests);
        Assert.Empty(h.Journal.Lines("signal"));

        h.FeedTo(Data.Confirmation);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Equal(BrokerCall.Place, request.Call);
        Assert.Null(request.Order);
        Assert.Equal(BuyRequest(), request.StopOrder);
        string signal = Assert.Single(h.Journal.Lines("signal"));
        Assert.Contains(
            " signal symbol=USDJPY result=taken bias=Long zone=BH-20260303-0000 zoneProximal=NA zoneDistal=NA fvgUpper=NA fvgLower=NA"
            + " entry=100.37 stop=99.96 target=101.16 stopPips=41.0 volume=121000 ",
            signal,
            StringComparison.Ordinal);
        Assert.EndsWith(" skip=none kind=breakout level=100.3", signal, StringComparison.Ordinal);

        FillBuy(h, Data.Confirmation.AddMinutes(5.0));
        AssertBuyPosition(h);
        Assert.Empty(h.Broker.GetPendingOrders());
        h.FeedTo(Data.Confirmation.AddMinutes(30.0));
        AssertBuyPosition(h);

        HitBuyTarget(h, Data.Confirmation.AddMinutes(35.0));
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(Data.Target, Assert.Single(h.Broker.History).ClosingPrice);
        string trade = TradeLine(h);
        Assert.Contains(" label=ZFVG|v1|USDJPY|BH-20260303-0000 side=Buy requested=100.37 ", trade, StringComparison.Ordinal);
        Assert.Contains(" exit=TakeProfit ", trade, StringComparison.Ordinal);
        string tradeLog = Assert.Single(h.Journal.Lines("tradelog"));
        Assert.Contains(" row=USDJPY,BH-20260303-0000,", tradeLog, StringComparison.Ordinal);
        Assert.EndsWith(" problem=none", tradeLog, StringComparison.Ordinal);

        Assert.Single(h.Broker.Requests);
        Assert.Equal(BotState.Running, h.Core.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void LiveBuy_LaterBarsOfTheSameDay_NoSecondOrder_TheNextReferenceGivesANewOrderWithTheNewId()
    {
        BotHarness h = Started(Data.Start);

        // Every Wednesday bar after 09:15 closes at 100.35, above the reference high 100.3; the order expires at 11:15.
        h.FeedTo(Thursday);
        Assert.Single(h.Broker.Requests);
        Assert.Single(h.Journal.Lines("signal"));
        Assert.Empty(h.Broker.GetPendingOrders());

        h.FeedTo(Data.NextConfirmation.AddMinutes(-15.0));
        Assert.Single(h.Broker.Requests);

        h.FeedTo(Data.NextConfirmation);
        Assert.Equal(2, h.Count(BrokerCall.Place));
        Assert.Null(h.Broker.Requests[1].Order);
        Assert.Equal(
            new StopOrderRequest(Symbol, Side.Buy, Data.Volume, Data.NextEntry, Data.NextStop, Data.NextTarget, Data.NextConfirmation.AddHours(2.0), Data.NextBuyLabel, Data.Comment(Data.NextEntry, Data.NextStop, Data.NextTarget)),
            h.Broker.Requests[1].StopOrder);
        IReadOnlyList<string> signals = h.Journal.Lines("signal");
        Assert.Equal(2, signals.Count);
        Assert.Contains(" zone=BH-20260304-0000 ", signals[1], StringComparison.Ordinal);
        Assert.EndsWith(" skip=none kind=breakout level=100.4", signals[1], StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void LiveSell_CloseBelowTheReferenceLow_OneSellStopWithTheBreakoutIdAndTheSpreadOnItsStop()
    {
        BotHarness h = Started(Data.Start, sell: true);

        h.FeedTo(Data.Confirmation);

        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Null(request.Order);
        Assert.Equal(
            new StopOrderRequest(Symbol, Side.Sell, Data.Volume, Data.SellEntry, Data.SellStop, Data.SellTarget, Data.Expiry, Data.SellLabel, Data.Comment(Data.SellEntry, Data.SellStop, Data.SellTarget)),
            request.StopOrder);
        string signal = Assert.Single(h.Journal.Lines("signal"));
        Assert.Contains(" result=taken bias=Short zone=BL-20260303-0000 zoneProximal=NA zoneDistal=NA fvgUpper=NA fvgLower=NA entry=99.64 stop=100.05 target=98.84 ", signal, StringComparison.Ordinal);
        Assert.EndsWith(" skip=none kind=breakout level=99.7", signal, StringComparison.Ordinal);

        // The bid reaches the entry: a sell stop fills.
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(5.0));
        h.Broker.Tick(Symbol, 99.64, 99.65);
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal((Side.Sell, Data.SellEntry, Data.SellStop, Data.SellTarget), (position.Side, position.EntryPrice, position.StopLoss!.Value, position.TakeProfit!.Value));
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "REC-017")]
    [InlineData(1.5, 0.1, 100.37, 99.76, 101.56, 81000.0)]
    [InlineData(1.0, 0.2, 100.38, 99.97, 101.17, 121000.0)]
    public void LiveBuy_OtherBreakoutValues_TheEngineAndTheCommentUseThem(double stopAtrMult, double entryBufferAtr, double entry, double stop, double target, double volume)
    {
        // StopAtrMult 1.5: stop 100.36 - 0.6 = 99.76, target 100.36 + 1.2 = 101.56, 61 pips, 500 / 0.0061 = 81,967 -> 81,000.
        // EntryBufferAtr 0.2: signal entry 100.35 + 0.02 = 100.37, order entry 100.38, stop 99.97, target 101.17, 41 pips.
        var breakout = new BreakoutParameters { StopAtrMult = stopAtrMult, EntryBufferAtr = entryBufferAtr };
        string hash = ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), breakout);
        BotHarness h = Started(Data.Start, Data.Parameters() with { Breakout = breakout });

        h.FeedTo(Data.Confirmation);

        StopOrderRequest request = Assert.Single(h.Broker.Requests).StopOrder!;
        Assert.Equal((entry, stop, target, volume), (request.EntryPrice, request.StopLossPrice, request.TakeProfitPrice, request.VolumeUnits));
        Assert.Equal(Data.BuyLabel, request.Label);
        Assert.NotEqual(Data.Hash, hash);
        Assert.Equal(hash, OrderComment.TryParse(request.Comment)!.ParameterHash);
        Assert.Equal(hash, h.Core.LastReport!.ParameterHash);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void LiveBuy_CloseAboveAt0630OutsideTheWindow_NoSignal_TheFirstCloseAboveFrom0700Confirms()
    {
        BotHarness h = Started(Data.Start, bars: Data.Bars(wednesday: TimeSpan.FromHours(6.25)));

        // The bars closing at 06:30 and 06:45 close above 100.3 but before 07:00.
        h.FeedTo(Data.Wednesday.AddHours(6.75));
        Assert.Empty(h.Broker.Requests);
        Assert.Empty(h.Journal.Lines("signal"));

        // The bar 06:45 to 07:00 (high 100.4): entry 100.41 + 0.01 spread = 100.42, stop 100.01, target 101.21.
        h.FeedTo(Data.Wednesday.AddHours(7.0));
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Equal(
            new StopOrderRequest(Symbol, Side.Buy, Data.Volume, 100.42, 100.01, 101.21, Data.Wednesday.AddHours(9.0), Data.BuyLabel, Data.Comment(100.42, 100.01, 101.21)),
            request.StopOrder);
        Assert.Single(h.Journal.Lines("signal"));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void LiveBreakout_EngineHasNoZonesAndNoGaps_SnapshotsAreEmpty()
    {
        BotHarness h = Started(Data.Start);

        h.FeedTo(Data.Confirmation);

        Assert.Single(h.Broker.Requests);
        Assert.Equal(Bias.Long, h.Core.Bias);
        Assert.Empty(h.Core.Zones);
        Assert.Empty(h.Core.ZoneSnapshot);
        Assert.Empty(h.Core.FvgSnapshot);
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ replay and restart

    [Fact]
    [Trait("Req", "REC-017")]
    public void Replay_RestartAfterTwoBreakouts_BothJournaledAsSkippedFromReplayAndNoRequest()
    {
        BotHarness h = Started(Thursday.AddHours(12.0));

        Assert.Equal(2, h.Core.LastReport!.ReplaySignals);
        Assert.Equal(1, h.Journal.Count("skip", "zone=" + Data.BuyId + " reason=SignalFromReplay"));
        Assert.Equal(1, h.Journal.Count("skip", "zone=" + Data.NextBuyId + " reason=SignalFromReplay"));
        IReadOnlyList<string> signals = h.Journal.Lines("signal");
        Assert.Equal(2, signals.Count);
        Assert.Contains(" result=skipped bias=Long zone=BH-20260303-0000 zoneProximal=NA zoneDistal=NA ", signals[0], StringComparison.Ordinal);
        Assert.EndsWith(" skip=SignalFromReplay kind=breakout level=100.3", signals[0], StringComparison.Ordinal);
        Assert.EndsWith(" skip=SignalFromReplay kind=breakout level=100.4", signals[1], StringComparison.Ordinal);
        Assert.Equal(BotState.Running, h.Core.State);
        Assert.Equal(0, h.Broker.WriteRequestCount);

        h.FeedTo(Thursday.AddHours(13.0));
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Restart_StopOrderPending_KeptNoCancelNoNewOrderRunning_ThenFillsAndIsManaged()
    {
        BotHarness h = Started(Data.Start);
        h.FeedTo(Data.Confirmation.AddMinutes(15.0));
        long order = Assert.Single(h.Broker.GetPendingOrders()).Id;

        BotCore restarted = h.Start(Data.Confirmation.AddMinutes(20.0), Data.Parameters());

        Assert.Equal(new[] { order }, restarted.LastReport!.OrdersKept);
        Assert.Empty(restarted.LastReport.OrdersCancelled);
        Assert.Empty(restarted.LastReport.Strangers);
        Assert.Equal(1, restarted.LastReport.ReplaySignals);
        Assert.Equal(BotState.Running, restarted.State);
        Assert.Single(h.Broker.Requests);
        Assert.Equal(0, h.Count(BrokerCall.Cancel));

        h.FeedTo(Data.Wednesday.AddHours(10.0));
        Assert.Single(h.Broker.Requests);
        FillBuy(h, Data.Wednesday.AddHours(10.0).AddMinutes(5.0));
        AssertBuyPosition(h);
        h.FeedTo(Data.Wednesday.AddHours(10.5));
        AssertBuyPosition(h);
        HitBuyTarget(h, Data.Wednesday.AddHours(10.5).AddMinutes(5.0));

        Assert.Empty(h.Broker.GetPositions());
        Assert.Contains(" label=ZFVG|v1|USDJPY|BH-20260303-0000 ", TradeLine(h), StringComparison.Ordinal);
        Assert.Single(h.Broker.Requests);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Restart_PositionOpen_AdoptedNotAStrangerProtectionCheckedNoNewOrder()
    {
        BotHarness h = Started(Data.Start);
        h.FeedTo(Data.Confirmation);
        FillBuy(h, Data.Confirmation.AddMinutes(5.0));
        long position = Assert.Single(h.Broker.GetPositions()).Id;
        h.FeedTo(Data.Confirmation.AddMinutes(15.0));
        int writes = h.Broker.WriteRequestCount;

        BotCore restarted = h.Start(Data.Confirmation.AddMinutes(20.0), Data.Parameters());

        Assert.Equal(new[] { position }, restarted.LastReport!.PositionsAdopted);
        Assert.Empty(restarted.LastReport.Strangers);
        Assert.Empty(restarted.LastReport.Protection);
        Assert.Empty(restarted.LastReport.WithoutTakeProfit);
        Assert.Equal(BotState.Running, restarted.State);
        Assert.Equal(writes, h.Broker.WriteRequestCount);

        h.FeedTo(Data.Wednesday.AddHours(12.0));
        Assert.Equal(1, h.Count(BrokerCall.Place));
        AssertBuyPosition(h);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Restart_PositionWithoutStop_AdoptedAndTheStopSetFromItsComment()
    {
        BotHarness h = Started(Data.Start);
        h.FeedTo(Data.Confirmation);
        FillBuy(h, Data.Confirmation.AddMinutes(5.0));
        long position = Assert.Single(h.Broker.GetPositions()).Id;
        h.Broker.ModifyPositionProtection(position, null, Data.Target);

        BotCore restarted = h.Start(Data.Confirmation.AddMinutes(20.0), Data.Parameters());

        Assert.Equal(new[] { position }, restarted.LastReport!.PositionsAdopted);
        Assert.Equal(new[] { Id(position) + " StopRestored (stop 99.96)" }, restarted.LastReport.Protection);
        AssertBuyPosition(h);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "REC-017")]
    [InlineData(true)]
    [InlineData(false)]
    public void Restart_BreakoutPositionAndOrderAtTheBroker_KnownInBreakoutMode_StrangerAndCancelledInZoneFvgMode(bool breakoutMode)
    {
        // Thursday 10:30: the Wednesday buy is open and the Thursday buy stop is pending, both with the breakout hash.
        DateTime at = Thursday.AddHours(10.5);
        BotHarness h = Data.Harness(at);
        long position = h.Broker.AddPosition(Symbol, Side.Buy, Data.Volume, Data.Entry, Data.Stop, Data.Target, Data.BuyLabel, Data.BuyComment());
        long order = h.Broker.AddOrder(Symbol, Side.Buy, Data.Volume, Data.NextEntry, Data.NextStop, Data.NextTarget, Data.NextConfirmation.AddHours(2.0), Data.NextBuyLabel, Data.Comment(Data.NextEntry, Data.NextStop, Data.NextTarget));

        BotCore core = h.Start(at, breakoutMode ? Data.Parameters() : RecoveryData.Parameters());

        if (breakoutMode)
        {
            Assert.Equal(new[] { position }, core.LastReport!.PositionsAdopted);
            Assert.Empty(core.LastReport.Strangers);
            Assert.Equal(new[] { order }, core.LastReport.OrdersKept);
            Assert.Empty(core.LastReport.OrdersCancelled);
            Assert.Equal(0, h.Broker.WriteRequestCount);
            Assert.Equal(0, h.Alerts(AlertKind.Stranger));
        }
        else
        {
            Assert.Equal(Data.ZoneFvgHash, core.LastReport!.ParameterHash);
            Assert.Empty(core.LastReport.PositionsAdopted);
            AssertStranger(Assert.Single(core.LastReport.Strangers), position, Data.BuyId, Data.Hash);
            Assert.Equal(1, h.Alerts(AlertKind.Stranger));
            Assert.Empty(core.LastReport.OrdersKept);
            Assert.Equal(new[] { Id(order) + " (zone BH-20260304-0000 unknown or ended)" }, core.LastReport.OrdersCancelled);
            Assert.Empty(h.Broker.GetPendingOrders());
            Assert.Equal(1, h.Count(BrokerCall.Cancel));
            Assert.Equal(0, h.Count(BrokerCall.Modify));
            Assert.Equal(0, h.Count(BrokerCall.Close));
        }

        AssertBuyPosition(h);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Restart_InBreakoutMode_PositionWithAZoneIdIsAStranger()
    {
        DateTime at = Data.Confirmation.AddMinutes(15.0);
        BotHarness h = Data.Harness(at);
        long position = h.Broker.AddPosition(Symbol, Side.Buy, 1000.0, RecoveryData.Entry, RecoveryData.Stop, RecoveryData.Target, RecoveryData.Label, RecoveryData.Comment());

        BotCore core = h.Start(at, Data.Parameters());

        Assert.Empty(core.LastReport!.PositionsAdopted);
        AssertStranger(Assert.Single(core.LastReport.Strangers), position, RecoveryData.ZoneId, Data.ZoneFvgHash);
        PositionInfo kept = Assert.Single(h.Broker.GetPositions());
        Assert.Equal((RecoveryData.Stop, RecoveryData.Target), (kept.StopLoss!.Value, kept.TakeProfit!.Value));
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Restart_InBreakoutMode_KnownIdWithTheZoneFvgHashIsAStrangerBecauseTheHashCoversTheMode()
    {
        DateTime at = Data.Confirmation.AddMinutes(15.0);
        BotHarness h = Data.Harness(at);
        long position = h.Broker.AddPosition(Symbol, Side.Buy, Data.Volume, Data.Entry, Data.Stop, Data.Target, Data.BuyLabel, Data.BuyComment(Data.ZoneFvgHash));

        BotCore core = h.Start(at, Data.Parameters());

        Assert.Equal(1, core.LastReport!.ReplaySignals);
        Assert.Equal(new[] { Id(position) + " (parameter hash f681dff2 differs)" }, core.LastReport.Strangers);
        Assert.Empty(core.LastReport.PositionsAdopted);
        AssertBuyPosition(h);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "REC-017")]
    [InlineData(true, true, true, Data.BuyLabel, null)]
    [InlineData(false, true, true, Data.BuyLabel, "no stop loss")]
    [InlineData(true, false, true, Data.BuyLabel, "no take profit")]
    [InlineData(true, true, false, Data.BuyLabel, "no expiry")]
    [InlineData(true, true, true, "ZFVG|v1|USDJPY|BH-20260302-0000", "zone BH-20260302-0000 unknown or ended")]
    [InlineData(true, true, true, "ZFVG|v1|USDJPY|BL-20260303-0000", "zone BL-20260303-0000 unknown or ended")]
    public void Restart_PendingStopOrder_KeptOnlyWithStopTargetExpiryAndAnIdTheReplayProduced(bool stop, bool target, bool expiry, string label, string? reason)
    {
        DateTime at = Data.Confirmation.AddMinutes(15.0);
        BotHarness h = Data.Harness(at);
        long id = h.Broker.AddOrder(Symbol, Side.Buy, Data.Volume, Data.Entry, stop ? Data.Stop : null, target ? Data.Target : null, expiry ? Data.Expiry : null, label, Data.BuyComment());

        BotCore core = h.Start(at, Data.Parameters());

        Assert.Equal(1, core.LastReport!.ReplaySignals);
        if (reason == null)
        {
            Assert.Equal(new[] { id }, core.LastReport.OrdersKept);
            Assert.Empty(core.LastReport.OrdersCancelled);
            Assert.Single(h.Broker.GetPendingOrders());
            Assert.Equal(0, h.Broker.WriteRequestCount);
        }
        else
        {
            Assert.Empty(core.LastReport.OrdersKept);
            Assert.Equal(new[] { Id(id) + " (" + reason + ")" }, core.LastReport.OrdersCancelled);
            Assert.Empty(h.Broker.GetPendingOrders());
            Assert.Equal(1, h.Count(BrokerCall.Cancel));
        }

        Assert.Equal(0, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "REC-017")]
    [InlineData(1.5, 0.1)]
    [InlineData(1.0, 0.2)]
    public void Restart_OtherBreakoutValues_EarlierPositionIsAStrangerBecauseTheHashDiffers(double stopAtrMult, double entryBufferAtr)
    {
        BotHarness h = Started(Data.Start);
        h.FeedTo(Data.Confirmation);
        FillBuy(h, Data.Confirmation.AddMinutes(5.0));
        long position = Assert.Single(h.Broker.GetPositions()).Id;
        h.FeedTo(Data.Confirmation.AddMinutes(15.0));
        int writes = h.Broker.WriteRequestCount;
        var breakout = new BreakoutParameters { StopAtrMult = stopAtrMult, EntryBufferAtr = entryBufferAtr };
        string hash = ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), breakout);

        BotCore restarted = h.Start(Data.Confirmation.AddMinutes(20.0), Data.Parameters() with { Breakout = breakout });

        Assert.NotEqual(Data.Hash, hash);
        Assert.Equal(hash, restarted.LastReport!.ParameterHash);
        Assert.Equal(1, restarted.LastReport.ReplaySignals);
        Assert.Empty(restarted.LastReport.PositionsAdopted);
        Assert.Equal(new[] { Id(position) + " (parameter hash 119a3864 differs)" }, restarted.LastReport.Strangers);
        AssertBuyPosition(h);
        Assert.Equal(writes, h.Broker.WriteRequestCount);

        h.FeedTo(Data.Wednesday.AddHours(11.0));
        Assert.Equal(1, h.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    // ------------------------------------------------------------------ parameter hash and start line

    [Fact]
    [Trait("Req", "REC-017")]
    public void Hash_BreakoutModeWithAllDefaults_ReportStartLineAndOrderCommentCarry119a3864()
    {
        BotParameters parameters = Data.Parameters();
        Assert.Equal(new SignalParameters(), parameters.Signal);
        Assert.Equal(new ExecutionParameters(), parameters.Execution);
        Assert.Equal(new BreakoutParameters(), parameters.Breakout);
        BotHarness h = Started(Data.Start, parameters);

        Assert.Equal("119a3864", h.Core.LastReport!.ParameterHash);
        Assert.Contains(" hash=119a3864", h.Core.LastReport.Text, StringComparison.Ordinal);
        Assert.Contains(" hash=119a3864 ", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);

        h.FeedTo(Data.Confirmation);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Equal("119a3864", OrderComment.TryParse(request.StopOrder!.Comment)!.ParameterHash);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Hash_ZoneFvgMode_ReportAndOrderCommentCarryTheValueOfV09()
    {
        var h = new BotHarness();
        h.Start(RecoveryData.Activation);

        Assert.Null(RecoveryData.Parameters().Breakout);
        Assert.Equal("f681dff2", h.Core.LastReport!.ParameterHash);
        Assert.Contains(" hash=f681dff2 ", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);

        h.FeedTo(RecoveryData.Confirmation);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Null(request.StopOrder);
        Assert.Equal("f681dff2", OrderComment.TryParse(request.Order!.Comment)!.ParameterHash);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void StartLine_BreakoutMode_EndsWithModeBreakout_ZoneFvgLineHasNoMode()
    {
        BotHarness breakout = Started(Data.Start);
        BotHarness zoneFvg = Started(Data.Start, RecoveryData.Parameters());

        string withMode = Assert.Single(breakout.Journal.Lines("start"));
        string without = Assert.Single(zoneFvg.Journal.Lines("start"));

        Assert.EndsWith(" mode=Breakout brake=off", withMode, StringComparison.Ordinal);
        Assert.DoesNotContain("mode=", without, StringComparison.Ordinal);
        Assert.EndsWith(" brake=off", without, StringComparison.Ordinal);
        Assert.Equal(without[..^" brake=off".Length].Replace(" hash=f681dff2 ", " hash=119a3864 ", StringComparison.Ordinal) + " mode=Breakout brake=off", withMode);
        ScenarioAssert.Global(breakout.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void StartLine_Reconnect_AlsoEndsWithModeBreakout()
    {
        BotHarness h = Started(Data.Start);

        h.Core.OnDisconnected();
        h.Core.OnConnected(h.History);

        IReadOnlyList<string> lines = h.Journal.Lines("start");
        Assert.Equal(2, lines.Count);
        Assert.Contains(" start event=reconnect ", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(" mode=Breakout brake=off", lines[1], StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "REC-017")]
    [InlineData("th-TH")]
    [InlineData("comma")]
    public void StartLine_UnderAnotherCulture_IsTheInvariantLine(string culture)
    {
        string expected = Assert.Single(Started(Data.Start).Journal.Lines("start"));
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo other = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        other.NumberFormat.NumberDecimalSeparator = ",";
        other.DateTimeFormat.TimeSeparator = ".";
        try
        {
            CultureInfo.CurrentCulture = culture == "comma" ? other : new CultureInfo(culture);

            string line = Assert.Single(Started(Data.Start).Journal.Lines("start"));

            Assert.Equal(expected, line);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        ScenarioAssert.Global(Started(Data.Start).Broker);
    }

    // ------------------------------------------------------------------ the rules that stay the same

    [Fact]
    [Trait("Req", "REC-017")]
    public void Friday_ConfirmationAt1215_SkippedWithFridayNoEntry()
    {
        BotHarness h = Started(Friday.AddHours(6.0));

        h.FeedTo(Friday.AddHours(12.25));

        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(1, h.Journal.Count("skip", "zone=" + Data.FridayBuyId + " reason=FridayNoEntry"));
        Assert.EndsWith(" skip=FridayNoEntry kind=breakout level=100.5", h.Journal.Lines("signal").Last(), StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Friday_PendingStopOrder_CancelledByTheFridayClose()
    {
        // FridayCloseUtc 11:00 so that the order (09:15 to 11:15) is still pending at the close.
        BotParameters parameters = Data.Parameters() with { Execution = new ExecutionParameters { FridayCloseUtc = TimeSpan.FromHours(11.0) } };
        BotHarness h = Started(Friday.AddHours(6.0), parameters, bars: Data.Bars(friday: TimeSpan.FromHours(9.0)));
        h.FeedTo(Friday.AddHours(9.25));
        BrokerRequest place = Assert.Single(h.Broker.Requests);
        Assert.NotNull(place.StopOrder);
        PendingOrderInfo order = Assert.Single(h.Broker.GetPendingOrders());
        Assert.Equal("ZFVG|v1|USDJPY|" + Data.FridayBuyId, order.Label);
        h.FeedTo(Friday.AddHours(10.75));
        Assert.Single(h.Broker.GetPendingOrders());

        h.Timer(Friday.AddHours(11.0));

        Assert.Empty(h.Broker.GetPendingOrders());
        BrokerRequest cancel = Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Cancel);
        Assert.Equal(order.Id, cancel.TargetId);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "REC-017")]
    [InlineData("08:30", true)]
    [InlineData("08:40", true)]
    [InlineData("08:29", false)]
    public void News_ConfirmingBarOverlapsANewsWindow_SkippedWithNewsBreakoutBarAndNothingSent(string eventTime, bool skipped)
    {
        // Windows of 30 minutes before and after: an event at 08:30 ends at 09:00, the open of the bar 09:00 to 09:15.
        string news = "2026-03-04 " + eventTime + ",USD,High,Test event\n" + RecoveryData.News;
        BotHarness h = Started(Data.Start, Data.Parameters(true, news));

        h.FeedTo(Data.Confirmation);

        string signal = Assert.Single(h.Journal.Lines("signal"));
        if (skipped)
        {
            Assert.Equal(0, h.Broker.WriteRequestCount);
            Assert.Equal(1, h.Journal.Count("skip", "zone=" + Data.BuyId + " reason=NewsBreakoutBar"));
            Assert.EndsWith(" skip=NewsBreakoutBar kind=breakout level=100.3", signal, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(BuyRequest(), Assert.Single(h.Broker.Requests).StopOrder);
            Assert.EndsWith(" skip=none kind=breakout level=100.3", signal, StringComparison.Ordinal);
        }

        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Halted_Confirmation_SkippedWithBotNotRunning()
    {
        // Lost 8,000 on Monday: drawdown 8%, the kill switch halts the bot at the start.
        BotHarness h = Data.Harness(Data.Start, account: ExecutionData.Account(92000.0, 92000.0, 92000.0));
        h.Broker.AddClosedTrade(new ClosedTradeInfo(901, Symbol, "ZFVG|v1|USDJPY|BH-20260301-0000", RecoveryData.At(2, 14, 0), RecoveryData.At(2, 15, 0), -8000.0, 0.0, 0.0, 92000.0));
        BotCore core = h.Start(Data.Start, Data.Parameters());
        Assert.Equal(BotState.Halted, core.State);

        h.FeedTo(Data.Confirmation);

        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Journal.Count("skip", "zone=" + Data.BuyId + " reason=BotNotRunning"));
        Assert.EndsWith(" skip=BotNotRunning kind=breakout level=100.3", Assert.Single(h.Journal.Lines("signal")), StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Blocked_Confirmation_SkippedWithBotNotRunning()
    {
        // An embedded history that does not cover March 2026: the calendar is unavailable and the bot Blocked.
        BotHarness h = Started(Data.Start, Data.Parameters(true, "2025-01-08 13:30,USD,High,CPI m/m\n"));
        Assert.Equal(BotState.Blocked, h.Core.State);

        h.FeedTo(Data.Confirmation);

        Assert.Equal(BotState.Blocked, h.Core.State);
        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Journal.Count("skip", "zone=" + Data.BuyId + " reason=BotNotRunning"));
        Assert.EndsWith(" skip=BotNotRunning kind=breakout level=100.3", Assert.Single(h.Journal.Lines("signal")), StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void BacktestHaltsLogOnly_DailyBudgetExhaustedByTheBreakoutOrder_PlacedWithOneWouldBlockLine()
    {
        // Lost 1,100 today (the CE(S)T day starts Tuesday 23:00 UTC). Risk 0.5% of 98,900 gives 120,000 units,
        // 492.00 at the stop: M - W = 1,100 + 492 = 1,592 > 1,500.
        BotHarness h = Data.Harness(Data.Start, account: ExecutionData.Account(98900.0, 98900.0, 98900.0));
        h.Broker.AddClosedTrade(new ClosedTradeInfo(901, Symbol, "ZFVG|v1|USDJPY|D-20260302-0800", Data.Start.AddHours(-2.0), Data.Start.AddHours(-1.0), -1100.0, 0.0, 0.0, 98900.0));
        BotCore core = h.Start(Data.Start, Data.Parameters() with { BacktestHaltsLogOnly = true });

        h.FeedTo(Data.Confirmation);

        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Equal(Data.BuyLabel, request.StopOrder!.Label);
        Assert.Equal(120000.0, request.StopOrder.VolumeUnits);
        Assert.Equal(0, h.Journal.Count("skip", "reason=DailyBudgetExceeded"));
        Assert.Equal(1, h.Journal.Count("risk", " risk event=would-block cause=DailyBudget detail=daily\\srisk\\sbudget:\\sworst-case\\sdaily\\sloss"));

        h.FeedTo(Data.Wednesday.AddHours(10.75));
        Assert.Equal(1, h.Journal.Count("risk", " risk event=would-block cause=DailyBudget detail="));
        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Equal(BotState.Running, core.State);
        ScenarioAssert.Global(h.Broker);
    }
}
