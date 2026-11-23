using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-027 to EXE-029 and NWS-015 through the execution layer of the Choch model and a
/// <see cref="FakeBroker"/>. Data and every expected value: <see cref="ChochExecutionData"/>.
/// </summary>
public class Exe027ChochOrderScenarios
{
    private static readonly DateTime Now = ExecutionData.Now;

    // ------------------------------------------------------------------ EXE-027: the order

    [Fact]
    [Trait("Req", "EXE-027")]
    public void Buy_PlacedAsABuyLimitWithStopTargetAndAnExpiryOfEightBars()
    {
        ChochExecutionHarness h = ChochExecutionData.Harness();

        PlannedOrder order = h.OrderOf(ChochExecutionData.Buy());
        PlaceResult placed = h.Orders.TryPlace(order);

        Assert.Equal(EntryOrderType.Limit, order.EntryType);
        Assert.Equal(PlaceOutcome.Placed, placed.Outcome);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Null(request.StopOrder);
        LimitOrderRequest limit = request.Order!;
        Assert.Equal(Side.Buy, limit.Side);
        Assert.Equal(1.1, limit.EntryPrice, 10);
        Assert.Equal(1.099, limit.StopLossPrice, 10);
        Assert.Equal(1.102, limit.TakeProfitPrice, 10);
        Assert.Equal(Now.AddHours(2.0), limit.ExpiryUtc);
        Assert.Equal(ExecutionData.Label(ExecutionData.BuyZone), limit.Label);
        Assert.Equal(200000.0, limit.VolumeUnits);
        Assert.Empty(h.Events.Skips);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-027")]
    public void Buy_FilledOnTheRetest_ThenTheTarget()
    {
        ChochExecutionHarness h = ChochExecutionData.Harness();
        h.Orders.TryPlace(h.OrderOf(ChochExecutionData.Buy()));

        h.Broker.AdvanceTo(Now.AddMinutes(20.0));
        h.Broker.Tick(ExecutionData.Symbol, 1.09998, 1.1);
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(1.099, position.StopLoss!.Value, 10);
        Assert.Equal(1.102, position.TakeProfit!.Value, 10);
        h.Broker.AdvanceTo(Now.AddMinutes(50.0));
        h.Broker.Tick(ExecutionData.Symbol, 1.102, 1.10202);

        Assert.Empty(h.Broker.GetPositions());
        Assert.True(Assert.Single(h.Broker.History).NetProfit > 0.0);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-027")]
    public void Buy_FilledOnTheRetest_ThenTheStop()
    {
        ChochExecutionHarness h = ChochExecutionData.Harness();
        h.Orders.TryPlace(h.OrderOf(ChochExecutionData.Buy()));

        h.Broker.AdvanceTo(Now.AddMinutes(20.0));
        h.Broker.Tick(ExecutionData.Symbol, 1.09998, 1.1);
        Assert.Single(h.Broker.GetPositions());
        h.Broker.AdvanceTo(Now.AddMinutes(40.0));
        h.Broker.Tick(ExecutionData.Symbol, 1.099, 1.09902);

        Assert.Empty(h.Broker.GetPositions());
        Assert.True(Assert.Single(h.Broker.History).NetProfit < 0.0);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-027")]
    public void Buy_NoRetestWithinEightBars_Expires()
    {
        ChochExecutionHarness h = ChochExecutionData.Harness();
        h.Orders.TryPlace(h.OrderOf(ChochExecutionData.Buy()));

        h.Broker.AdvanceTo(Now.AddHours(2.0).AddMinutes(-1.0));
        Assert.Single(h.Broker.GetPendingOrders());
        h.Broker.AdvanceTo(Now.AddHours(2.0));

        Assert.Empty(h.Broker.GetPendingOrders());
        Assert.Empty(h.Broker.GetPositions());
        Assert.Empty(h.Broker.History);
    }

    [Fact]
    [Trait("Req", "EXE-027")]
    public void Sell_PlacedAsASellLimit_TheSpreadAddedToTheStop()
    {
        ChochExecutionHarness h = ChochExecutionData.Harness(ChochExecutionData.SellQuote());

        PlannedOrder order = h.OrderOf(ChochExecutionData.Sell());
        h.Orders.TryPlace(order);

        LimitOrderRequest limit = Assert.Single(h.Broker.Requests).Order!;
        Assert.Equal(EntryOrderType.Limit, order.EntryType);
        Assert.Equal(Side.Sell, limit.Side);
        Assert.Equal(1.1, limit.EntryPrice, 10);
        Assert.Equal(1.101 + ChochExecutionData.Spread, limit.StopLossPrice, 10);
        Assert.Equal(1.098, limit.TakeProfitPrice, 10);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-027")]
    [InlineData(1.1, true)]
    [InlineData(1.0999, true)]
    [InlineData(1.1001, false)]
    public void Buy_AskAtOrBelowTheEntry_EntryAlreadyReached(double ask, bool skipped)
    {
        ChochExecutionHarness h = ChochExecutionData.Harness(ChochExecutionData.BuyQuote(ask, ask - ChochExecutionData.Spread));

        if (!skipped)
        {
            h.OrderOf(ChochExecutionData.Buy());
            return;
        }

        SkipRecord skip = h.SkipOf(ChochExecutionData.Buy());
        Assert.Equal(SkipReason.EntryAlreadyReached, skip.Reason);
        Assert.Equal(ask, skip.Measured);
        Assert.Equal(1.1, skip.Limit, 10);
        Assert.StartsWith("ask " + Invariant.Text(ask) + " at or below the entry 1.1", skip.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "EXE-027")]
    [InlineData(1.1, true)]
    [InlineData(1.1001, true)]
    [InlineData(1.0999, false)]
    public void Sell_BidAtOrAboveTheEntry_EntryAlreadyReached(double bid, bool skipped)
    {
        ChochExecutionHarness h = ChochExecutionData.Harness(ChochExecutionData.SellQuote(bid, bid + ChochExecutionData.Spread));

        if (!skipped)
        {
            h.OrderOf(ChochExecutionData.Sell());
            return;
        }

        SkipRecord skip = h.SkipOf(ChochExecutionData.Sell());
        Assert.Equal(SkipReason.EntryAlreadyReached, skip.Reason);
        Assert.Equal(bid, skip.Measured);
        Assert.StartsWith("bid " + Invariant.Text(bid) + " at or above the entry 1.1", skip.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-027")]
    public void Buy_QuoteNotUsable_BrokerDataInvalid()
    {
        ChochExecutionHarness h = ChochExecutionData.Harness(ChochExecutionData.BuyQuote(0.0, 0.0));

        Assert.Equal(SkipReason.BrokerDataInvalid, h.SkipOf(ChochExecutionData.Buy()).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-027")]
    public void Inputs_GapLevelOrModel_InvalidSignal()
    {
        var fvg = new Fvg(Side.Buy, 1.1005, 1.0995, Now.AddMinutes(-45.0), Now.AddMinutes(-30.0), Now.AddMinutes(-15.0));

        Assert.Equal(SkipReason.InvalidSignal, ChochExecutionData.Harness().SkipOf(ChochExecutionData.Buy(fvg: fvg)).Reason);
        Assert.Equal(SkipReason.InvalidSignal, ChochExecutionData.Harness().SkipOf(ChochExecutionData.Buy(level: 1.1001)).Reason);
        Assert.Equal(SkipReason.InvalidSignal, ChochExecutionData.Harness().SkipOf(ChochExecutionData.Buy() with { Level = null }).Reason);
        Assert.Equal(SkipReason.InvalidSignal, ChochExecutionData.Harness().SkipOf(ChochExecutionData.Buy() with { Level = double.NaN }).Reason);

        // A Choch signal reaching filters that were not created for the Choch model fails closed.
        Assert.Equal(SkipReason.InvalidSignal, ChochExecutionData.Harness(chochModel: false).SkipOf(ChochExecutionData.Buy()).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-027")]
    public void OrderManager_ChochWithAStopEntryType_RefusedChochWithLimitSent()
    {
        ChochExecutionHarness h = ChochExecutionData.Harness();
        PlannedOrder order = h.OrderOf(ChochExecutionData.Buy());

        PlaceResult refused = h.Orders.TryPlace(order with { EntryType = EntryOrderType.Stop });

        Assert.Equal(PlaceOutcome.Abandoned, refused.Outcome);
        Assert.Equal("entry type Stop does not fit signal kind Choch", refused.Detail);
        Assert.Empty(h.Broker.Requests);
        Assert.Equal(PlaceOutcome.Placed, h.Orders.TryPlace(order).Outcome);
    }

    // ------------------------------------------------------------------ EXE-028: stop bounds, reward, no room or gap check

    [Fact]
    [Trait("Req", "EXE-028")]
    public void StopOfExactlyChochMinStopPips_Passes_OneTickLess_StopTooSmall()
    {
        // 5 pips = 50 ticks of 0.00001; one tick less is 4.9 pips. Targets at 2R.
        PlannedOrder order = ChochExecutionData.Harness().OrderOf(ChochExecutionData.Buy(stop: 1.0995, target: 1.101));
        SkipRecord skip = ChochExecutionData.Harness().SkipOf(ChochExecutionData.Buy(stop: 1.09951, target: 1.10098));

        Assert.Equal(5.0, order.StopPips, 6);
        Assert.Equal(400000.0, order.VolumeUnits);
        Assert.Equal(SkipReason.StopTooSmall, skip.Reason);
        Assert.Equal(4.9, skip.Measured, 6);
        Assert.Equal(5.0, skip.Limit);
        Assert.EndsWith(" pips below 5", skip.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-028")]
    public void TenPipStop_PassesForChoch_TheSameStopOfAZoneFvgSignalIsBelowMinStopPips()
    {
        ChochExecutionHarness h = ChochExecutionData.Harness();

        h.OrderOf(ChochExecutionData.Buy());
        SkipRecord zoneFvg = h.SkipOf(ExecutionData.Buy(stop: 1.099, target: 1.102));

        Assert.Equal(SkipReason.StopTooSmall, zoneFvg.Reason);
        Assert.Equal(12.0, zoneFvg.Limit);
    }

    [Fact]
    [Trait("Req", "EXE-028")]
    public void StopAboveMaxStopAtr_StopTooLarge()
    {
        // ATR(H4) 0.0004: at most 2 x 4 = 8 pips; the stop is 10.
        SkipRecord skip = ChochExecutionData.Harness().SkipOf(ChochExecutionData.Buy(atrH4: 0.0004));

        Assert.Equal(SkipReason.StopTooLarge, skip.Reason);
    }

    [Fact]
    [Trait("Req", "EXE-028")]
    public void RewardOfExactlyMinRewardRisk_Passes_OneTickLess_RewardRiskTooLow()
    {
        ChochExecutionData.Harness().OrderOf(ChochExecutionData.Buy(target: 1.102));
        SkipRecord skip = ChochExecutionData.Harness().SkipOf(ChochExecutionData.Buy(target: 1.10199));

        Assert.Equal(SkipReason.RewardRiskTooLow, skip.Reason);
        Assert.Equal(1.99, skip.Measured, 6);
        Assert.Equal(2.0, skip.Limit);
        Assert.EndsWith("R below 2R, target default", skip.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-028")]
    public void Reward_MeasuredOnTheSignalsOwnLevels_ForASellBeforeTheSpread()
    {
        // The sell's order stop is 1.10102 (spread added); on the signal's levels the reward is exactly 2.
        ChochExecutionData.Harness(ChochExecutionData.SellQuote()).OrderOf(ChochExecutionData.Sell());
        SkipRecord skip = ChochExecutionData.Harness(ChochExecutionData.SellQuote()).SkipOf(ChochExecutionData.Sell(target: 1.09801));

        Assert.Equal(SkipReason.RewardRiskTooLow, skip.Reason);
        Assert.Equal(1.99, skip.Measured, 6);
        Assert.EndsWith("target zone:D-20260113-1200", skip.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-028")]
    public void OtherMinRewardRisk_IsTheLimit()
    {
        var choch = new ChochParameters { MinRewardRisk = 1.5 };

        ChochExecutionData.Harness(choch: choch).OrderOf(ChochExecutionData.Buy(target: 1.1015));
        Assert.Equal(1.5, ChochExecutionData.Harness(choch: choch).SkipOf(ChochExecutionData.Buy(target: 1.10149)).Limit);
    }

    [Fact]
    [Trait("Req", "EXE-028")]
    public void SupplyZoneOneRAboveTheEntry_DoesNotSkipAChochSignal_ButSkipsAZoneFvgSignal()
    {
        Zone supply = ExecutionData.Zone("S-20260114-0000", ZoneType.Supply, 1.101);
        var zones = new[] { supply };

        ChochExecutionData.Harness().OrderOf(ChochExecutionData.Buy(), zones);
        SkipRecord zoneFvg = ChochExecutionData.Harness().SkipOf(ExecutionData.Buy(stop: 1.0985, target: 1.103), zones);

        Assert.Equal(SkipReason.NoRoom, zoneFvg.Reason);
    }

    [Fact]
    [Trait("Req", "EXE-028")]
    public void ZoneFvgSignalWithOnePointFiveR_NotAffectedByTheRewardCheck()
    {
        PlannedOrder order = ChochExecutionData.Harness().OrderOf(ExecutionData.Buy(stop: 1.0985, target: 1.10225));

        Assert.Equal(SignalKind.ZoneFvg, order.Signal.Kind);
    }

    [Fact]
    [Trait("Req", "EXE-028")]
    public void CostCheck_StillApplies()
    {
        // Spread 1.5 pips on a 10-pip stop: 15%, above CostToRiskMax 10%.
        SkipRecord skip = ChochExecutionData.Harness(ExecutionData.Quoted(1.1004, 1.10055, 0.00015)).SkipOf(ChochExecutionData.Buy());

        Assert.Equal(SkipReason.CostTooHigh, skip.Reason);
    }

    // ------------------------------------------------------------------ NWS-015: the confirming bar

    [Fact]
    [Trait("Req", "NWS-015")]
    public void ConfirmingBarInANewsWindow_NewsChochBar()
    {
        // Event at 09:20: the window 08:50 to 09:50 overlaps the bar 09:45 to 10:00; now 10:00 is outside it.
        SkipRecord skip = ChochExecutionData.Harness(news: "2026-01-14 09:20,USD,High,Event\n").SkipOf(ChochExecutionData.Buy());

        Assert.Equal(SkipReason.NewsChochBar, skip.Reason);
        Assert.Equal("CHoCH bar opening 2026-01-14 09:45:00 overlaps a news window", skip.Detail);
    }

    [Fact]
    [Trait("Req", "NWS-015")]
    public void BarEndingExactlyAtTheWindowStart_IsNotNewsChochBar()
    {
        // Event at 10:30: the window starts at 10:00, the close of the bar, which the bar does not include.
        // Now (10:00) is inside the window, so NWS-009 skips it as NewsWindow, not as NewsChochBar.
        SkipRecord skip = ChochExecutionData.Harness(news: "2026-01-14 10:30,USD,High,Event\n").SkipOf(ChochExecutionData.Buy());

        Assert.Equal(SkipReason.NewsWindow, skip.Reason);
    }

    [Fact]
    [Trait("Req", "NWS-015")]
    public void WindowEndingOneMinuteBeforeTheBar_Passes_CalendarUnavailable_Skipped()
    {
        ChochExecutionData.Harness(news: "2026-01-14 09:14,USD,High,Event\n").OrderOf(ChochExecutionData.Buy());

        Assert.Equal(SkipReason.NewsCalendarUnavailable, ChochExecutionData.Harness(news: "2026-02-18 18:00,USD,High,Event\n").SkipOf(ChochExecutionData.Buy()).Reason);
    }

    // ------------------------------------------------------------------ EXE-029: the all-day session

    [Theory]
    [Trait("Req", "EXE-029")]
    [InlineData(23, 45)]
    [InlineData(0, 0)]
    public void AllDaySession_SignalAt2345AndAtMidnight_Passes(int hour, int minute)
    {
        DateTime at = hour == 0 ? ExecutionData.At(15, 0, 0) : ExecutionData.At(14, hour, minute);
        var allDay = new ExecutionParameters { SessionStartUtc = TimeSpan.Zero, SessionEndUtc = TimeSpan.FromHours(24.0) };

        PlannedOrder order = ChochExecutionData.Harness(execution: allDay, now: at).OrderOf(ChochExecutionData.Buy(closeUtc: at));

        Assert.Equal(at.AddHours(2.0), order.ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "EXE-029")]
    public void DefaultSession_SignalAt1700_StillOutsideSession()
    {
        DateTime at = ExecutionData.At(14, 17, 0);

        SkipRecord skip = ChochExecutionData.Harness(now: at).SkipOf(ChochExecutionData.Buy(closeUtc: at));

        Assert.Equal(SkipReason.OutsideSession, skip.Reason);
    }

    [Fact]
    [Trait("Req", "EXE-029")]
    public void Validate_SessionEnd2400Accepted_StartAt2400Refused_EndAboveRefused()
    {
        new ExecutionParameters { SessionStartUtc = TimeSpan.Zero, SessionEndUtc = TimeSpan.FromHours(24.0) }.Validate();
        new ExecutionParameters { SessionEndUtc = TimeSpan.FromHours(24.0) }.Validate();

        var start = Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionParameters { SessionStartUtc = TimeSpan.FromHours(24.0), SessionEndUtc = TimeSpan.FromHours(24.0) }.Validate());
        var above = Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutionParameters { SessionEndUtc = TimeSpan.FromHours(24.0) + TimeSpan.FromSeconds(1.0) }.Validate());

        Assert.Equal("SessionStartUtc", start.ParamName);
        Assert.Equal("SessionEndUtc", above.ParamName);
        Assert.StartsWith("SessionEndUtc must be a UTC time of day from 00:00 up to and including 24:00 but was 1.00:00:01.", above.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-029")]
    public void BreakoutMode_WindowEnd2400_StillRefused()
    {
        var allDay = new ExecutionParameters { SessionStartUtc = TimeSpan.Zero, SessionEndUtc = TimeSpan.FromHours(24.0) };
        BotParameters breakout = RecoveryData.Parameters() with
        {
            Execution = allDay,
            Breakout = new BreakoutParameters { WindowStartUtc = TimeSpan.Zero, WindowEndUtc = TimeSpan.FromHours(24.0), LastConfirmUtc = TimeSpan.FromHours(8.0) },
        };

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => breakout.Validate(isBacktest: true));

        Assert.Equal("WindowEndUtc", error.ParamName);
        (RecoveryData.Parameters() with { Execution = allDay, Choch = new ChochParameters() }).Validate(isBacktest: true);
    }

    [Fact]
    [Trait("Req", "EXE-029")]
    public void Hash_SessionEnd2400_WrittenInTheConstantFormat()
    {
        var allDay = new ExecutionParameters { SessionStartUtc = TimeSpan.Zero, SessionEndUtc = TimeSpan.FromHours(24.0) };

        string text = ParameterHash.Canonical(new SignalParameters(), allDay);

        Assert.Contains(";SessionStartUtc=00:00:00;SessionEndUtc=1.00:00:00;", text, StringComparison.Ordinal);
        Assert.Equal("976d9100", ParameterHash.Compute(new SignalParameters(), allDay));
    }
}
