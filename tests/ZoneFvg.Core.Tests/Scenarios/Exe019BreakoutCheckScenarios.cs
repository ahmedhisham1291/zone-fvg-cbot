using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-019: the pre-trade checks of a breakout signal. (a) A breakout signal has no gap and a
/// level, a ZoneFvg signal has its gap. (b) The gap-versus-spread and room checks are not run.
/// (c) For a buy the spread is added to the entry, for a sell to the stop (EXE-007); the stop
/// bounds, the cost check, sizing and the comment use these levels. (d) Directly after the stop
/// bounds, a buy is skipped when the ask is at or above the order's entry and a sell when the bid
/// is at or below it; a bid or ask that is not usable is BrokerDataInvalid. Every other check and
/// their order are unchanged. Values: <see cref="ExecutionData"/> (breakout buy and sell).
/// </summary>
public class Exe019BreakoutCheckScenarios
{
    private static ExecutionHarness BuyHarness(double spread = 0.0001, double commission = 0.0, string news = ExecutionData.QuietNews)
    {
        return ExecutionData.BreakoutHarness(ExecutionData.BuyQuote(spread, commission), news);
    }

    private static ExecutionHarness SellHarness(double spread = 0.0001, double commission = 0.0)
    {
        return ExecutionData.BreakoutHarness(ExecutionData.SellQuote(spread, commission));
    }

    private static void SetQuote(ExecutionHarness h, double bid, double ask)
    {
        h.Broker.SetSymbol(h.Broker.Symbol("EURUSD") with { Bid = bid, Ask = ask });
    }

    // ------------------------------------------------------------------ (a) inputs

    public static IEnumerable<object?[]> BadLevels()
    {
        yield return new object?[] { null };
        yield return new object?[] { 0.0 };
        yield return new object?[] { -1.085 };
        yield return new object?[] { double.NaN };
        yield return new object?[] { double.PositiveInfinity };
    }

    [Theory]
    [Trait("Req", "EXE-019")]
    [MemberData(nameof(BadLevels))]
    public void BreakoutSignal_LevelMissingNotFiniteOrNotPositive_IsInvalidSignal(double? level)
    {
        ExecutionHarness h = BuyHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.BreakoutBuy(level: level));

        Assert.Equal(SkipReason.InvalidSignal, skip.Reason);
        Assert.Equal(ExecutionData.BreakoutBuyId, skip.ZoneId);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutSignal_WithAGap_IsInvalidSignal()
    {
        ExecutionHarness h = BuyHarness();
        Fvg gap = ExecutionData.Buy().Fvg!;

        Assert.Equal(SkipReason.InvalidSignal, h.SkipOf(ExecutionData.BreakoutBuy(fvg: gap)).Reason);
        Assert.Equal(SkipReason.InvalidSignal, h.SkipOf(ExecutionData.BreakoutSell(fvg: gap with { Side = Side.Sell })).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void ZoneFvgSignal_WithoutAGap_IsInvalidSignal()
    {
        ExecutionHarness h = BuyHarness();

        Assert.Equal(SkipReason.InvalidSignal, h.SkipOf(ExecutionData.Buy() with { Fvg = null }).Reason);
        Assert.Equal(SkipReason.InvalidSignal, h.SkipOf(ExecutionData.Sell() with { Fvg = null }).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void Signal_OfAnUndefinedKind_IsInvalidSignal()
    {
        ExecutionHarness h = BuyHarness();

        Assert.Equal(SkipReason.InvalidSignal, h.SkipOf(ExecutionData.BreakoutBuy() with { Kind = (SignalKind)7 }).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutSignals_WithLevelAndNoGap_AreAllowedAsStopOrders()
    {
        PlannedOrder buy = BuyHarness().OrderOf(ExecutionData.BreakoutBuy());
        PlannedOrder sell = SellHarness().OrderOf(ExecutionData.BreakoutSell());

        Assert.Equal(EntryOrderType.Stop, buy.EntryType);
        Assert.Equal(EntryOrderType.Stop, sell.EntryType);
        Assert.Equal(EntryOrderType.Limit, BuyHarness().OrderOf(ExecutionData.Buy()).EntryType);
    }

    // ------------------------------------------------------------------ (b) no gap or room check

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_SupplyZoneOneRAboveTheEntry_IsAllowed()
    {
        // Order entry 1.08640, 41 pips: 1.0 R above is 1.09050, inside the 1.5 R of EXE-006.
        ExecutionHarness h = BuyHarness();
        Zone[] zones = { ExecutionData.Zone("S-20260113-0800", ZoneType.Supply, 1.0905) };

        PlannedOrder order = h.OrderOf(ExecutionData.BreakoutBuy(), zones: zones);

        Assert.Equal(1.0864, order.Entry, 9);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutSell_DemandZoneOneRBelowTheEntry_IsAllowed()
    {
        // Order entry 1.07670, 41 pips: 1.0 R below is 1.07260.
        ExecutionHarness h = SellHarness();
        Zone[] zones = { ExecutionData.Zone("D-20260113-0800", ZoneType.Demand, 1.0726) };

        Assert.Equal(1.0767, h.OrderOf(ExecutionData.BreakoutSell(), zones: zones).Entry, 9);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void ZoneFvgBuy_SupplyZoneOneRAboveTheEntry_IsStillNoRoom()
    {
        // Entry 1.10000, 20 pips: 1.0 R above is 1.10200.
        ExecutionHarness h = BuyHarness();
        Zone[] zones = { ExecutionData.Zone("S-20260113-0800", ZoneType.Supply, 1.102) };

        Assert.Equal(SkipReason.NoRoom, h.SkipOf(ExecutionData.Buy(), zones: zones).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_SpreadAtTheMaximum_IsNotCheckedAgainstAGap()
    {
        // Spread 1.5 pips: a ZoneFvg gap would need 4.5 pips; a breakout has none and is allowed.
        PlannedOrder order = BuyHarness(spread: 0.00015).OrderOf(ExecutionData.BreakoutBuy());

        Assert.Equal(1.08645, order.Entry, 9);
    }

    // ------------------------------------------------------------------ (c) spread on the entry

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_Spread0_0001_EntryRaisedStopAndTargetKept()
    {
        PlannedOrder order = BuyHarness().OrderOf(ExecutionData.BreakoutBuy());

        Assert.Equal(1.0864, order.Entry, 9);
        Assert.Equal(1.0823, order.StopLoss, 9);
        Assert.Equal(1.0943, order.TakeProfit, 9);
        Assert.Equal(41.0, order.StopPips, 6);
        Assert.Equal(121000.0, order.VolumeUnits);
        Assert.Equal(496.1, order.RiskMoney, 6);
        Assert.Equal("e=1.08640;s=1.08230;t=1.09430;r=496.10;h=119a3864", order.Comment);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_Spread0_00013_EntryIsTheSignalEntryPlusTheSpreadOnTheGrid()
    {
        PlannedOrder order = BuyHarness(spread: 0.00013).OrderOf(ExecutionData.BreakoutBuy());

        Assert.Equal(1.08643, order.Entry, 9);
        Assert.Equal(1.0823, order.StopLoss, 9);
        Assert.Equal(41.3, order.StopPips, 6);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_EntryOffTheGrid_IsRoundedAfterTheSpreadIsAdded()
    {
        // 1.086304 + 0.0001 = 1.086404, rounded to the tick 0.00001: 1.08640.
        PlannedOrder order = BuyHarness().OrderOf(ExecutionData.BreakoutBuy(entry: 1.086304));

        Assert.Equal(1.0864, order.Entry, 9);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutSell_Spread0_0001_SpreadOnTheStopEntryAndTargetKept()
    {
        PlannedOrder order = SellHarness().OrderOf(ExecutionData.BreakoutSell());

        Assert.Equal(1.0767, order.Entry, 9);
        Assert.Equal(1.0808, order.StopLoss, 9);
        Assert.Equal(1.0687, order.TakeProfit, 9);
        Assert.Equal(41.0, order.StopPips, 6);
        Assert.Equal(121000.0, order.VolumeUnits);
        Assert.Equal("e=1.07670;s=1.08080;t=1.06870;r=496.10;h=119a3864", order.Comment);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutSell_Spread0_00013_OnlyTheStopMoves()
    {
        PlannedOrder order = SellHarness(spread: 0.00013).OrderOf(ExecutionData.BreakoutSell());

        Assert.Equal(1.0767, order.Entry, 9);
        Assert.Equal(1.08083, order.StopLoss, 9);
        Assert.Equal(1.0687, order.TakeProfit, 9);
        Assert.Equal(41.3, order.StopPips, 6);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_StopBoundsUseTheRaisedEntry()
    {
        // Signal distance 11.5 pips (entry 1.08630, stop 1.08515): with spread 0.0001 the order has
        // 12.5 pips and passes; with spread 0.00004 it has 11.9 pips, below MinStopPips 12.
        Signal signal = ExecutionData.BreakoutBuy(stop: 1.08515);

        Assert.Equal(12.5, BuyHarness().OrderOf(signal).StopPips, 6);
        SkipRecord skip = BuyHarness(spread: 0.00004).SkipOf(signal);
        Assert.Equal(SkipReason.StopTooSmall, skip.Reason);
        Assert.Equal(11.9, skip.Measured, 6);
        Assert.Equal(12.0, skip.Limit, 6);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_StopExactly12PipsAfterTheSpread_IsAllowed()
    {
        // Signal distance 11 pips plus the 1-pip spread: exactly MinStopPips.
        Assert.Equal(12.0, BuyHarness().OrderOf(ExecutionData.BreakoutBuy(stop: 1.0852)).StopPips, 6);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_StopTooLargeOnlyBecauseOfTheSpread_IsSkipped()
    {
        // ATR(H4) 0.002: at most 40 pips. Signal distance 39.5 pips plus the spread: 40.5 pips.
        SkipRecord skip = BuyHarness().SkipOf(ExecutionData.BreakoutBuy(stop: 1.08235, atrH4: 0.002));

        Assert.Equal(SkipReason.StopTooLarge, skip.Reason);
        Assert.Equal(40.5, skip.Measured, 6);
        Assert.Equal(40.0, skip.Limit, 6);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutSell_StopBoundsUseTheWidenedStop()
    {
        // Signal distance 11.5 pips (entry 1.07670, stop 1.07785): 12.5 pips with spread 0.0001, 11.9 with 0.00004.
        Signal signal = ExecutionData.BreakoutSell(stop: 1.07785);

        Assert.Equal(12.5, SellHarness().OrderOf(signal).StopPips, 6);
        Assert.Equal(SkipReason.StopTooSmall, SellHarness(spread: 0.00004).SkipOf(signal).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_CostCheckUsesTheStopPipsAfterTheSpread()
    {
        // Spread 1.0 + commission 3.05 pips = 4.05 pips: within 10% of 41 pips (4.1), above 10% of the signal's 40 (4.0).
        PlannedOrder order = BuyHarness(commission: 0.000305).OrderOf(ExecutionData.BreakoutBuy());

        Assert.Equal(41.0, order.StopPips, 6);

        // Commission 3.15 pips: 4.15 pips is above 4.1.
        SkipRecord skip = BuyHarness(commission: 0.000315).SkipOf(ExecutionData.BreakoutBuy());
        Assert.Equal(SkipReason.CostTooHigh, skip.Reason);
        Assert.Equal(4.15, skip.Measured, 6);
        Assert.Equal(4.1, skip.Limit, 6);
    }

    // ------------------------------------------------------------------ (d) entry already reached

    [Theory]
    [Trait("Req", "EXE-019")]
    [InlineData(1.0864)]
    [InlineData(1.0865)]
    [InlineData(1.08639999995)]
    [InlineData(1.08640000005)]
    public void BreakoutBuy_AskAtOrAboveTheOrderEntry_IsEntryAlreadyReached(double ask)
    {
        ExecutionHarness h = BuyHarness();
        SetQuote(h, ask - 0.0001, ask);

        SkipRecord skip = h.SkipOf(ExecutionData.BreakoutBuy());

        Assert.Equal(SkipReason.EntryAlreadyReached, skip.Reason);
        Assert.Equal(ask, skip.Measured, 9);
        Assert.Equal(1.0864, skip.Limit, 9);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Empty(h.Events.Alerts);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_AskOneTickBelowTheOrderEntry_IsAllowed()
    {
        ExecutionHarness h = BuyHarness();
        SetQuote(h, 1.08629, 1.08639);

        Assert.Equal(1.0864, h.OrderOf(ExecutionData.BreakoutBuy()).Entry, 9);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_OtherSpread_ComparesTheAskWithTheRaisedEntry()
    {
        // Spread 0.00013: order entry 1.08643. Ask 1.08643 is reached, 1.08642 is not.
        ExecutionHarness reached = BuyHarness(spread: 0.00013);
        SetQuote(reached, 1.0863, 1.08643);
        ExecutionHarness below = BuyHarness(spread: 0.00013);
        SetQuote(below, 1.08629, 1.08642);

        Assert.Equal(SkipReason.EntryAlreadyReached, reached.SkipOf(ExecutionData.BreakoutBuy()).Reason);
        Assert.Equal(1.08643, below.OrderOf(ExecutionData.BreakoutBuy()).Entry, 9);
    }

    [Theory]
    [Trait("Req", "EXE-019")]
    [InlineData(1.0767)]
    [InlineData(1.0766)]
    [InlineData(1.07670000005)]
    [InlineData(1.07669999995)]
    public void BreakoutSell_BidAtOrBelowTheOrderEntry_IsEntryAlreadyReached(double bid)
    {
        ExecutionHarness h = SellHarness();
        SetQuote(h, bid, bid + 0.0001);

        SkipRecord skip = h.SkipOf(ExecutionData.BreakoutSell());

        Assert.Equal(SkipReason.EntryAlreadyReached, skip.Reason);
        Assert.Equal(bid, skip.Measured, 9);
        Assert.Equal(1.0767, skip.Limit, 9);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutSell_BidOneTickAboveTheOrderEntry_IsAllowed()
    {
        ExecutionHarness h = SellHarness();
        SetQuote(h, 1.07671, 1.07681);

        Assert.Equal(1.0767, h.OrderOf(ExecutionData.BreakoutSell()).Entry, 9);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutSell_AskAboveTheEntry_IsAllowed()
    {
        // A sell reads the bid only: the buy rule (ask at or above the entry) would skip this.
        ExecutionHarness h = SellHarness();
        SetQuote(h, 1.0772, 1.0773);

        Assert.True(h.Evaluate(ExecutionData.BreakoutSell()).IsAllowed);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_BidBelowTheEntry_IsAllowed()
    {
        // A buy reads the ask only: the sell rule (bid at or below the entry) would skip this.
        ExecutionHarness h = BuyHarness();
        SetQuote(h, 1.0858, 1.0859);

        Assert.True(h.Evaluate(ExecutionData.BreakoutBuy()).IsAllowed);
    }

    public static IEnumerable<object[]> BadQuotes()
    {
        yield return new object[] { "both zero", 0.0, 0.0 };
        yield return new object[] { "bid zero", 0.0, 1.0859 };
        yield return new object[] { "ask zero", 1.0858, 0.0 };
        yield return new object[] { "bid negative", -1.0858, 1.0859 };
        yield return new object[] { "ask negative", 1.0858, -1.0859 };
        yield return new object[] { "bid NaN", double.NaN, 1.0859 };
        yield return new object[] { "ask NaN", 1.0858, double.NaN };
        yield return new object[] { "bid infinite", double.PositiveInfinity, 1.0859 };
        yield return new object[] { "ask infinite", 1.0858, double.PositiveInfinity };
        yield return new object[] { "ask below bid", 1.0859, 1.0858 };
    }

    [Theory]
    [Trait("Req", "EXE-019")]
    [MemberData(nameof(BadQuotes))]
    public void BreakoutSignal_BidOrAskNotUsable_IsBrokerDataInvalid(string name, double bid, double ask)
    {
        ExecutionHarness buy = BuyHarness();
        SetQuote(buy, bid, ask);
        ExecutionHarness sell = SellHarness();
        SetQuote(sell, bid, ask);

        Assert.True(buy.SkipOf(ExecutionData.BreakoutBuy()).Reason == SkipReason.BrokerDataInvalid, name + " (buy)");
        Assert.True(sell.SkipOf(ExecutionData.BreakoutSell()).Reason == SkipReason.BrokerDataInvalid, name + " (sell)");
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutSignal_SymbolWithoutQuote_IsBrokerDataInvalid()
    {
        // The default SymbolInfo has bid and ask 0.
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.EurUsd());

        SkipRecord skip = h.SkipOf(ExecutionData.BreakoutBuy());

        Assert.Equal(SkipReason.BrokerDataInvalid, skip.Reason);
        Assert.Contains("ask", skip.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "EXE-019")]
    [InlineData(0.0, 0.0)]
    [InlineData(1.2, 1.2001)]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(1.0859, 1.0858)]
    public void ZoneFvgSignal_IsNotAffectedByBidAndAsk(double bid, double ask)
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.Quoted(bid, ask));

        PlannedOrder buy = h.OrderOf(ExecutionData.Buy());
        PlannedOrder sell = h.OrderOf(ExecutionData.Sell());

        Assert.Equal(1.1, buy.Entry, 9);
        Assert.Equal(1.098, buy.StopLoss, 9);
        Assert.Equal(1.1021, sell.StopLoss, 9);
    }

    // ------------------------------------------------------------------ order of the checks

    public static IEnumerable<object[]> EarlierChecks()
    {
        yield return new object[] { "invalid", SkipReason.InvalidSignal };
        yield return new object[] { "replay", SkipReason.SignalFromReplay };
        yield return new object[] { "stale", SkipReason.SignalStale };
        yield return new object[] { "halted", SkipReason.BotNotRunning };
        yield return new object[] { "position without stop", SkipReason.PositionWithoutStop };
        yield return new object[] { "outside session", SkipReason.OutsideSession };
        yield return new object[] { "Friday", SkipReason.FridayNoEntry };
        yield return new object[] { "spread", SkipReason.SpreadTooWide };
        yield return new object[] { "market closed", SkipReason.MarketClosed };
        yield return new object[] { "busy", SkipReason.SymbolBusy };
        yield return new object[] { "calendar unavailable", SkipReason.NewsCalendarUnavailable };
        yield return new object[] { "inside news window", SkipReason.NewsWindow };
        yield return new object[] { "breakout bar in news window", SkipReason.NewsBreakoutBar };
        yield return new object[] { "stop on wrong side", SkipReason.StopOnWrongSide };
        yield return new object[] { "target on wrong side", SkipReason.TargetOnWrongSide };
        yield return new object[] { "stop too small", SkipReason.StopTooSmall };
        yield return new object[] { "stop too large", SkipReason.StopTooLarge };
    }

    [Theory]
    [Trait("Req", "EXE-019")]
    [MemberData(nameof(EarlierChecks))]
    public void BreakoutBuy_AlsoAlreadyReached_ReportsTheEarlierCheckFirst(string name, SkipReason expected)
    {
        // Every case also has the ask 1.08700, above the order entry 1.08640, and the spread 1.6
        // pips only in the spread case.
        (ExecutionHarness h, Signal signal, BotState state) = Earlier(name);
        SetQuote(h, 1.0869, 1.087);

        Assert.Equal(expected, h.SkipOf(signal, state).Reason);
    }

    private static (ExecutionHarness Harness, Signal Signal, BotState State) Earlier(string name)
    {
        Signal buy = ExecutionData.BreakoutBuy();
        switch (name)
        {
            case "invalid":
                return (BuyHarness(), ExecutionData.BreakoutBuy(level: double.NaN), BotState.Running);
            case "replay":
                return (BuyHarness(), ExecutionData.BreakoutBuy(fromReplay: true), BotState.Running);
            case "stale":
                return (BuyHarness(), ExecutionData.BreakoutBuy(closeUtc: ExecutionData.At(14, 9, 30)), BotState.Running);
            case "halted":
                return (BuyHarness(), buy, BotState.Halted);
            case "position without stop":
                ExecutionHarness unprotected = BuyHarness();
                unprotected.Broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.08, null, 1.09, ExecutionData.Label("BH-20260109-2200"), "c");
                return (unprotected, buy, BotState.Running);
            case "outside session":
                DateTime late = ExecutionData.At(14, 17, 0);
                return (ExecutionData.BreakoutHarness(ExecutionData.BuyQuote(), now: late), ExecutionData.BreakoutBuy(closeUtc: late), BotState.Running);
            case "Friday":
                DateTime friday = ExecutionData.At(16, 12, 0);
                return (ExecutionData.BreakoutHarness(ExecutionData.BuyQuote(), now: friday), ExecutionData.BreakoutBuy(closeUtc: friday), BotState.Running);
            case "spread":
                return (BuyHarness(spread: 0.00016), buy, BotState.Running);
            case "market closed":
                return (ExecutionData.BreakoutHarness(ExecutionData.BuyQuote() with { IsMarketOpen = false }), buy, BotState.Running);
            case "busy":
                ExecutionHarness busy = BuyHarness();
                busy.Broker.AddOrder("EURUSD", Side.Buy, 1000.0, 1.09, 1.08, 1.1, ExecutionData.At(14, 12, 0), ExecutionData.Label("BH-20260109-2200"), "c");
                return (busy, buy, BotState.Running);
            case "calendar unavailable":
                return (BuyHarness(news: "2026-02-18 18:00,USD,High,Event\n"), buy, BotState.Running);
            case "inside news window":
                return (BuyHarness(news: "2026-01-14 10:15,USD,High,Event\n"), buy, BotState.Running);
            case "breakout bar in news window":
                return (BuyHarness(news: "2026-01-14 09:20,USD,High,Event\n"), buy, BotState.Running);
            case "stop on wrong side":
                return (BuyHarness(), ExecutionData.BreakoutBuy(stop: 1.0875), BotState.Running);
            case "target on wrong side":
                return (BuyHarness(), ExecutionData.BreakoutBuy(target: 1.085), BotState.Running);
            case "stop too small":
                return (BuyHarness(), ExecutionData.BreakoutBuy(stop: 1.0853), BotState.Running);
            default:
                return (BuyHarness(), ExecutionData.BreakoutBuy(atrH4: 0.001), BotState.Running);
        }
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_AlreadyReachedAndCostTooHigh_ReportsEntryAlreadyReached()
    {
        // Commission 5 pips: cost 6 pips against 4.1 allowed. The ask 1.08700 is above the entry.
        ExecutionHarness reached = BuyHarness(commission: 0.0005);
        SetQuote(reached, 1.0869, 1.087);
        ExecutionHarness notReached = BuyHarness(commission: 0.0005);

        Assert.Equal(SkipReason.EntryAlreadyReached, reached.SkipOf(ExecutionData.BreakoutBuy()).Reason);
        Assert.Equal(SkipReason.CostTooHigh, notReached.SkipOf(ExecutionData.BreakoutBuy()).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_AlreadyReachedAndExpiryTooShort_ReportsEntryAlreadyReached()
    {
        // Event at 10:44: the window starts at 10:14, 14 minutes of order life.
        ExecutionHarness reached = BuyHarness(news: "2026-01-14 10:44,USD,High,Event\n");
        SetQuote(reached, 1.0869, 1.087);
        ExecutionHarness notReached = BuyHarness(news: "2026-01-14 10:44,USD,High,Event\n");

        Assert.Equal(SkipReason.EntryAlreadyReached, reached.SkipOf(ExecutionData.BreakoutBuy()).Reason);
        Assert.Equal(SkipReason.ExpiryTooShort, notReached.SkipOf(ExecutionData.BreakoutBuy()).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    [Trait("Req", "NWS-014")]
    public void BreakoutBuy_ConfirmingBarInANewsWindow_IsNewsBreakoutBarWithTheBarInTheDetail()
    {
        // Event at 09:20: the window 08:50 to 09:50 overlaps the confirming bar 09:45 to 10:00.
        SkipRecord skip = BuyHarness(news: "2026-01-14 09:20,USD,High,Event\n").SkipOf(ExecutionData.BreakoutBuy());

        Assert.Equal(SkipReason.NewsBreakoutBar, skip.Reason);
        Assert.Contains("2026-01-14 09:45:00", skip.Detail, StringComparison.Ordinal);
        Assert.Equal(0.0, skip.Measured);
        Assert.Equal(0.0, skip.Limit);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void BreakoutSell_EurEventOverlappingTheConfirmingBar_IsNewsBreakoutBar_GbpEventIsNot()
    {
        ExecutionHarness eur = ExecutionData.BreakoutHarness(ExecutionData.SellQuote(), "2026-01-14 09:20,EUR,High,Event\n");
        ExecutionHarness gbp = ExecutionData.BreakoutHarness(ExecutionData.SellQuote(), "2026-01-14 09:20,GBP,High,Event\n");

        Assert.Equal(SkipReason.NewsBreakoutBar, eur.SkipOf(ExecutionData.BreakoutSell()).Reason);
        Assert.True(gbp.Evaluate(ExecutionData.BreakoutSell()).IsAllowed);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void BreakoutBuy_NewsOnlyOnTheEarlierBarsOfAGapSpan_IsAllowed_ZoneFvgIsNewsFvgBar()
    {
        // Event at 09:00: the window 08:30 to 09:30 overlaps 09:15 to 09:30, the first bar of the
        // reference gap, but not the confirming bar 09:45 to 10:00.
        ExecutionHarness h = BuyHarness(news: "2026-01-14 09:00,USD,High,Event\n");

        Assert.True(h.Evaluate(ExecutionData.BreakoutBuy()).IsAllowed);
        Assert.Equal(SkipReason.NewsFvgBar, h.SkipOf(ExecutionData.Buy()).Reason);
    }

    [Fact]
    [Trait("Req", "NWS-014")]
    public void BreakoutBuy_CalendarUnavailableOrInsideAWindow_SkippedAsForAZoneFvgSignal()
    {
        ExecutionHarness unavailable = BuyHarness(news: "2026-02-18 18:00,USD,High,Event\n");
        ExecutionHarness inside = BuyHarness(news: "2026-01-14 10:15,USD,High,Event\n");

        Assert.Equal(SkipReason.NewsCalendarUnavailable, unavailable.SkipOf(ExecutionData.BreakoutBuy()).Reason);
        Assert.Equal(SkipReason.NewsCalendarUnavailable, unavailable.SkipOf(ExecutionData.Buy()).Reason);
        Assert.Equal(SkipReason.NewsWindow, inside.SkipOf(ExecutionData.BreakoutBuy()).Reason);
        Assert.Equal(SkipReason.NewsWindow, inside.SkipOf(ExecutionData.Buy()).Reason);
    }

    // ------------------------------------------------------------------ expiry (EXE-015)

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_Expiry_EightBarsAfterNow()
    {
        Assert.Equal(ExecutionData.At(14, 12, 0), BuyHarness().OrderOf(ExecutionData.BreakoutBuy()).ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_Expiry_ShortenedToTheNextNewsWindow()
    {
        // Event at 11:00: the window starts at 10:30.
        Assert.Equal(ExecutionData.At(14, 10, 30), BuyHarness(news: "2026-01-14 11:00,USD,High,Event\n").OrderOf(ExecutionData.BreakoutBuy()).ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "EXE-019")]
    public void BreakoutBuy_Expiry_FifteenMinutesIsEnough_FourteenIsExpiryTooShort()
    {
        PlannedOrder order = BuyHarness(news: "2026-01-14 10:45,USD,High,Event\n").OrderOf(ExecutionData.BreakoutBuy());
        SkipRecord skip = BuyHarness(news: "2026-01-14 10:44,USD,High,Event\n").SkipOf(ExecutionData.BreakoutBuy());

        Assert.Equal(ExecutionData.At(14, 10, 15), order.ExpiryUtc);
        Assert.Equal(SkipReason.ExpiryTooShort, skip.Reason);
        Assert.Equal(14.0, skip.Measured, 6);
        Assert.Equal(15.0, skip.Limit, 6);
    }
}
