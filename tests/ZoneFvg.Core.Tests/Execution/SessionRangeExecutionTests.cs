using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-024 and EXE-025 in the execution layer, with the EURUSD values of
/// <see cref="ExecutionData"/>: server time Wednesday 2026-01-14 10:00 UTC (winter, so 12:00 New
/// York is 17:00 UTC and 16:30 New York is 21:30 UTC), bid 1.0858, ask 1.0859, spread 0.0001.
/// The session range buy RH-20260114 has the entry 1.0863 (the range high), the stop 1.0823 and
/// the target 1.0943; its order is a buy stop at 1.0863 + 0.0001 = 1.0864. The sell RL-20260114
/// has the entry 1.0767, the stop 1.0807 (1.0808 with the spread) and the target 1.0687.
/// </summary>
public class SessionRangeExecutionTests
{
    private const string BuyId = "RH-20260114";
    private const string SellId = "RL-20260114";

    private static readonly DateTime Expiry = ExecutionData.At(14, 17, 0);

    private static Signal RangeBuy(DateTime? expiryUtc = null, double? level = 1.0863, bool fromReplay = false, DateTime? closeUtc = null)
    {
        return new Signal(BuyId, Side.Buy, 1.0863, 1.0823, 1.0943, null, closeUtc ?? ExecutionData.Now, fromReplay, 0.004)
        {
            Kind = SignalKind.SessionRange,
            Level = level,
            ExpiryUtc = expiryUtc ?? Expiry,
        };
    }

    private static Signal RangeSell()
    {
        return new Signal(SellId, Side.Sell, 1.0767, 1.0807, 1.0687, null, ExecutionData.Now, false, 0.004)
        {
            Kind = SignalKind.SessionRange,
            Level = 1.0767,
            ExpiryUtc = Expiry,
        };
    }

    private static ExecutionHarness Harness(string news = ExecutionData.QuietNews, DateTime? now = null, bool sell = false)
    {
        SymbolInfo quote = sell ? ExecutionData.SellQuote() : ExecutionData.BuyQuote();
        return new ExecutionHarness(ExecutionHarness.DefaultBroker(quote, now), news);
    }

    private static void SetQuote(ExecutionHarness h, double bid, double ask)
    {
        h.Broker.SetSymbol(h.Broker.Symbol(ExecutionData.Symbol) with { Bid = bid, Ask = ask });
    }

    private static string RangeLabel(string id, string symbol = ExecutionData.Symbol)
    {
        return "ZFVG|v1|" + symbol + "|" + id;
    }

    // ------------------------------------------------------------------ pre-trade checks and order

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Buy_Allowed_StopOrderWithSpreadOnTheEntryAndTheSignalsExpiry()
    {
        ExecutionHarness h = Harness();

        PlannedOrder order = h.OrderOf(RangeBuy());

        Assert.Equal(EntryOrderType.Stop, order.EntryType);
        Assert.Equal(1.0864, order.Entry, 9);
        Assert.Equal(1.0823, order.StopLoss, 9);
        Assert.Equal(1.0943, order.TakeProfit, 9);
        Assert.Equal(41.0, order.StopPips, 6);
        Assert.Equal(Expiry, order.ExpiryUtc);
        Assert.Equal(RangeLabel(BuyId), order.Label);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Sell_Allowed_StopOrderWithSpreadOnTheStop()
    {
        ExecutionHarness h = Harness(sell: true);

        PlannedOrder order = h.OrderOf(RangeSell());

        Assert.Equal(EntryOrderType.Stop, order.EntryType);
        Assert.Equal(1.0767, order.Entry, 9);
        Assert.Equal(1.0808, order.StopLoss, 9);
        Assert.Equal(1.0687, order.TakeProfit, 9);
        Assert.Equal(Expiry, order.ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Buy_NewsWindowBeforeTheSignalsExpiry_TheOrderEndsAtTheWindowStart()
    {
        // The window of the 18:00 event starts at 17:30, before the expiry 20:00.
        ExecutionHarness h = Harness();

        PlannedOrder order = h.OrderOf(RangeBuy(ExecutionData.At(14, 20, 0)));

        Assert.Equal(ExecutionData.At(14, 17, 30), order.ExpiryUtc);
    }

    [Theory]
    [Trait("Req", "EXE-024")]
    [InlineData(15, true)]
    [InlineData(14, false)]
    [InlineData(-1, false)]
    public void Buy_OrderLifeToTheSignalsExpiry_AtLeast15Minutes(int minutes, bool allowed)
    {
        ExecutionHarness h = Harness();
        Signal signal = RangeBuy(ExecutionData.Now.AddMinutes(minutes));

        if (allowed)
        {
            Assert.Equal(ExecutionData.Now.AddMinutes(minutes), h.OrderOf(signal).ExpiryUtc);
        }
        else
        {
            Assert.Equal(SkipReason.ExpiryTooShort, h.SkipOf(signal).Reason);
        }
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Friday_CapAlreadyPast_SkippedByTheFridayFilter()
    {
        // Friday 2026-01-16 12:00 UTC: the setup's expiry is the Friday cap 12:00, which is now.
        DateTime friday = ExecutionData.At(16, 12, 0);
        ExecutionHarness h = Harness(now: friday);

        SkipRecord skip = h.SkipOf(RangeBuy(friday, closeUtc: friday));

        Assert.Equal(SkipReason.FridayNoEntry, skip.Reason);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    public static IEnumerable<object?[]> BadInputs()
    {
        yield return new object?[] { "no expiry", RangeBuy() with { ExpiryUtc = null } };
        yield return new object?[] { "local expiry", RangeBuy(DateTime.SpecifyKind(Expiry, DateTimeKind.Local)) };
        yield return new object?[] { "unspecified expiry", RangeBuy(DateTime.SpecifyKind(Expiry, DateTimeKind.Unspecified)) };
        yield return new object?[] { "no level", RangeBuy(level: null) };
        yield return new object?[] { "zero level", RangeBuy(level: 0.0) };
        yield return new object?[] { "NaN level", RangeBuy(level: double.NaN) };
        yield return new object?[] { "gap", RangeBuy() with { Fvg = ExecutionData.Buy().Fvg } };
    }

    [Theory]
    [Trait("Req", "EXE-024")]
    [MemberData(nameof(BadInputs))]
    public void Signal_MissingOrInconsistentValue_IsInvalidSignal(string what, Signal signal)
    {
        ExecutionHarness h = Harness();

        SkipRecord skip = h.SkipOf(signal);

        Assert.True(skip.Reason == SkipReason.InvalidSignal, what);
        Assert.Equal(BuyId, skip.ZoneId);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Signal_FromReplay_NotExecutable()
    {
        ExecutionHarness h = Harness();

        Assert.Equal(SkipReason.SignalFromReplay, h.SkipOf(RangeBuy(fromReplay: true)).Reason);
    }

    [Theory]
    [Trait("Req", "EXE-024")]
    [InlineData(1.0863, 1.0864, true)]
    [InlineData(1.0864, 1.0865, true)]
    [InlineData(1.08629, 1.08639, false)]
    public void Buy_AskAtOrAboveTheOrderEntry_EntryAlreadyReached(double bid, double ask, bool reached)
    {
        ExecutionHarness h = Harness();
        SetQuote(h, bid, ask);

        if (reached)
        {
            SkipRecord skip = h.SkipOf(RangeBuy());
            Assert.Equal(SkipReason.EntryAlreadyReached, skip.Reason);
            Assert.Equal(1.0864, skip.Limit, 9);
        }
        else
        {
            Assert.Equal(1.0864, h.OrderOf(RangeBuy()).Entry, 9);
        }
    }

    [Theory]
    [Trait("Req", "EXE-024")]
    [InlineData(1.0767, 1.0768, true)]
    [InlineData(1.07671, 1.07681, false)]
    public void Sell_BidAtOrBelowTheEntry_EntryAlreadyReached(double bid, double ask, bool reached)
    {
        ExecutionHarness h = Harness(sell: true);
        SetQuote(h, bid, ask);

        if (reached)
        {
            Assert.Equal(SkipReason.EntryAlreadyReached, h.SkipOf(RangeSell()).Reason);
        }
        else
        {
            Assert.Equal(1.0767, h.OrderOf(RangeSell()).Entry, 9);
        }
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void News_ConfirmingBarOverlapsAWindowThatHasEnded_NotASkipForASessionRangeSignal()
    {
        // The 09:20 event makes a window from 08:50 to 09:50: it overlaps the bar 09:45 to 10:00 but not 10:00.
        ExecutionHarness h = Harness("2026-01-14 09:20,USD,High,Event\n");

        Assert.Equal(EntryOrderType.Stop, h.OrderOf(RangeBuy()).EntryType);
        Assert.Equal(SkipReason.NewsBreakoutBar, h.SkipOf(ExecutionData.BreakoutBuy()).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void News_InsideAWindow_NewsWindow()
    {
        ExecutionHarness h = Harness("2026-01-14 10:15,USD,High,Event\n");

        Assert.Equal(SkipReason.NewsWindow, h.SkipOf(RangeBuy()).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void News_CalendarDoesNotCoverNow_NewsCalendarUnavailable()
    {
        ExecutionHarness h = Harness("2026-03-04 13:30,USD,High,Event\n");

        Assert.Equal(SkipReason.NewsCalendarUnavailable, h.SkipOf(RangeBuy()).Reason);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void Filters_WithSessionRangeParameters_CommentCarriesTheSessionRangeHash()
    {
        ExecutionHarness h = Harness();
        var filters = new PreTradeFilters(ExecutionData.ExecutionDefaults, ExecutionData.SignalDefaults, ExecutionData.RiskDefaults, h.Broker, h.News, h.Events, sessionRange: new SessionRangeParameters());
        var context = new PreTradeContext(ExecutionData.Symbol, BotState.Running, Array.Empty<Zone>(), false);

        PreTradeResult result = filters.Evaluate(RangeBuy(), context);

        Assert.True(result.IsAllowed);
        Assert.Equal(SessionRangeBotData.Hash, OrderComment.TryParse(result.Order!.Comment)!.ParameterHash);
    }

    // ------------------------------------------------------------------ EXE-022 for the new kind

    [Fact]
    [Trait("Req", "EXE-024")]
    public void TryPlace_SessionRangeWithStop_SendsOneStopOrder()
    {
        ExecutionHarness h = Harness();
        PlannedOrder order = h.OrderOf(RangeBuy());

        PlaceResult result = h.Orders.TryPlace(order);

        Assert.Equal(PlaceOutcome.Placed, result.Outcome);
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Null(request.Order);
        Assert.Equal(order.ToStopRequest(), request.StopOrder);
        Assert.Equal(Expiry, Assert.Single(h.Broker.GetPendingOrders()).ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void TryPlace_SessionRangeWithLimit_NothingSentInvalidSignalAbandoned()
    {
        ExecutionHarness h = Harness();
        PlannedOrder order = h.OrderOf(RangeBuy()) with { EntryType = EntryOrderType.Limit };

        PlaceResult result = h.Orders.TryPlace(order);

        Assert.Equal(PlaceOutcome.Abandoned, result.Outcome);
        Assert.Equal("entry type Limit does not fit signal kind SessionRange", result.Detail);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        SkipRecord skip = h.Events.Skips[^1];
        Assert.Equal(SkipReason.InvalidSignal, skip.Reason);
        Assert.Equal(BuyId, skip.ZoneId);
    }

    // ------------------------------------------------------------------ void: unresolved placements

    [Fact]
    [Trait("Req", "EXE-024")]
    public void AbandonUnresolved_UnclearPlacementOfTheVoidSetup_DroppedRecordedAndNeverResent()
    {
        ExecutionHarness h = Harness();
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied);
        Assert.Equal(PlaceOutcome.Unclear, h.Orders.TryPlace(h.OrderOf(RangeBuy())).Outcome);
        Assert.Equal(1, h.Orders.UnresolvedCount);

        Assert.Empty(h.Orders.AbandonUnresolved(SellId, "setup void"));
        Assert.Equal(1, h.Orders.UnresolvedCount);
        h.Broker.AdvanceTo(ExecutionData.Now.AddMinutes(20.0));
        PlaceResult result = Assert.Single(h.Orders.AbandonUnresolved(BuyId, "setup void"));

        Assert.Equal(PlaceOutcome.Abandoned, result.Outcome);
        Assert.Equal(RangeLabel(BuyId), result.Label);
        Assert.Equal("setup void", result.Detail);
        Assert.Equal(0, h.Orders.UnresolvedCount);
        Assert.False(h.Orders.HasUnresolved(ExecutionData.Symbol));
        SkipRecord skip = h.Events.Skips[^1];
        Assert.Equal(SkipReason.UnclearOutcomeNotResent, skip.Reason);
        Assert.Equal(BuyId, skip.ZoneId);
        Assert.Equal("setup void", skip.Detail);
        Assert.Equal(20.0, skip.Measured, 9);
        Assert.Empty(h.Orders.ResolvePending(Array.Empty<Bar>()));
        Assert.Equal(1, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void AbandonUnresolved_NullArgument_Throws()
    {
        ExecutionHarness h = Harness();

        Assert.Throws<ArgumentNullException>(() => h.Orders.AbandonUnresolved(null!, "setup void"));
        Assert.Throws<ArgumentNullException>(() => h.Orders.AbandonUnresolved(BuyId, null!));
    }

    // ------------------------------------------------------------------ void: cancel by id

    [Fact]
    [Trait("Req", "EXE-024")]
    public void CancelOrdersOf_CancelsOnlyTheBotOrderOfThatIdOnTheSymbol()
    {
        ExecutionHarness h = Harness();
        long mine = h.Broker.AddOrder(ExecutionData.Symbol, Side.Buy, 1000.0, 1.0864, 1.0823, 1.0943, Expiry, RangeLabel(BuyId), "c");
        long other = h.Broker.AddOrder(ExecutionData.Symbol, Side.Sell, 1000.0, 1.0767, 1.0808, 1.0687, Expiry, RangeLabel(SellId), "c");
        long otherSymbol = h.Broker.AddOrder("GBPUSD", Side.Buy, 1000.0, 1.3, 1.29, 1.32, Expiry, RangeLabel(BuyId, "GBPUSD"), "c");
        long manual = h.Broker.AddOrder(ExecutionData.Symbol, Side.Buy, 1000.0, 1.0864, 1.0823, 1.0943, Expiry, "manual " + BuyId, "c");

        Assert.True(h.Trades.HasPendingOrderOf(BuyId));
        IReadOnlyList<TradeAction> actions = h.Trades.CancelOrdersOf(BuyId, "range void");

        TradeAction action = Assert.Single(actions);
        Assert.Equal(new TradeAction(mine, TradeActionOutcome.Cancelled, "range void"), action);
        Assert.Equal(new[] { other, otherSymbol, manual }, h.Broker.GetPendingOrders().Select(o => o.Id).OrderBy(id => id).ToArray());
        Assert.False(h.Trades.HasPendingOrderOf(BuyId));
        Assert.True(h.Trades.HasPendingOrderOf(SellId));
        Assert.Equal(1, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void CancelOrdersOf_NothingPending_NoRequest()
    {
        ExecutionHarness h = Harness();

        Assert.Empty(h.Trades.CancelOrdersOf(BuyId, "range void"));
        Assert.False(h.Trades.HasPendingOrderOf(BuyId));
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void CancelOrdersOf_CancelRejected_CancelFailedAndAlert()
    {
        ExecutionHarness h = Harness();
        long mine = h.Broker.AddOrder(ExecutionData.Symbol, Side.Buy, 1000.0, 1.0864, 1.0823, 1.0943, Expiry, RangeLabel(BuyId), "c");
        h.Broker.Script(BrokerCall.Cancel, BrokerFault.Rejected, "Busy");

        TradeAction action = Assert.Single(h.Trades.CancelOrdersOf(BuyId, "range void"));

        Assert.Equal(new TradeAction(mine, TradeActionOutcome.CancelFailed, "range void"), action);
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.CancelFailed));
        Assert.True(h.Trades.HasPendingOrderOf(BuyId));
    }

    [Fact]
    [Trait("Req", "EXE-024")]
    public void CancelOrdersOf_NullArgument_Throws()
    {
        ExecutionHarness h = Harness();

        Assert.Throws<ArgumentNullException>(() => h.Trades.CancelOrdersOf(null!, "range void"));
        Assert.Throws<ArgumentNullException>(() => h.Trades.CancelOrdersOf(BuyId, null!));
        Assert.Throws<ArgumentNullException>(() => h.Trades.HasPendingOrderOf(null!));
    }

    // ------------------------------------------------------------------ EXE-025 timed exit

    [Fact]
    [Trait("Req", "EXE-025")]
    public void RangeExit_At1630NewYorkOfTheIdsDate_ClosesOnlyThatSessionRangePosition()
    {
        ExecutionHarness h = Harness();
        TimeSpan exit = new SessionRangeParameters().ExitNy;
        long mine = h.Broker.AddPosition(ExecutionData.Symbol, Side.Buy, 1000.0, 1.0864, 1.0823, 1.0943, RangeLabel(BuyId), "c");
        long tomorrow = h.Broker.AddPosition(ExecutionData.Symbol, Side.Sell, 1000.0, 1.0767, 1.0808, 1.0687, RangeLabel("RL-20260115"), "c");
        long breakout = h.Broker.AddPosition(ExecutionData.Symbol, Side.Buy, 1000.0, 1.0864, 1.0823, 1.0943, RangeLabel(ExecutionData.BreakoutBuyId), "c");
        long zone = h.Broker.AddPosition(ExecutionData.Symbol, Side.Buy, 1000.0, 1.0864, 1.0823, 1.0943, RangeLabel(ExecutionData.BuyZone), "c");
        long otherSymbol = h.Broker.AddPosition("GBPUSD", Side.Buy, 1000.0, 1.3, 1.29, 1.32, RangeLabel(BuyId, "GBPUSD"), "c");
        long manual = h.Broker.AddPosition(ExecutionData.Symbol, Side.Buy, 1000.0, 1.0864, 1.0823, 1.0943, "manual", "c");
        DateTime before = ExecutionData.At(14, 21, 29, 59);
        DateTime at = ExecutionData.At(14, 21, 30);

        Assert.False(h.Trades.RangeExitDue(before, exit));
        Assert.Empty(h.Trades.RangeExit(before, exit));
        Assert.True(h.Trades.RangeExitDue(at, exit));
        h.Broker.AdvanceTo(at);
        TradeAction action = Assert.Single(h.Trades.RangeExit(at, exit));

        Assert.Equal(new TradeAction(mine, TradeActionOutcome.Closed, "range exit"), action);
        Assert.Equal(new[] { tomorrow, breakout, zone, otherSymbol, manual }, h.Broker.GetPositions().Select(p => p.Id).OrderBy(id => id).ToArray());
        Assert.Equal(1, h.Broker.WriteRequestCount);
        Assert.Empty(h.Trades.RangeExit(at.AddMinutes(1.0), exit));
        Assert.False(h.Trades.RangeExitDue(at.AddMinutes(1.0), exit));
        Assert.Equal(1, h.Broker.WriteRequestCount);
    }

    [Theory]
    [Trait("Req", "EXE-025")]
    [InlineData("RH-20260312", 2026, 3, 12, 20, 30)]
    [InlineData("RL-20260306", 2026, 3, 6, 21, 30)]
    [InlineData("RH-20261102", 2026, 11, 2, 21, 30)]
    [InlineData("RH-20261030", 2026, 10, 30, 20, 30)]
    public void RangeExitDue_SummerAndWinterDates_ExactlyFromTheExitInstant(string id, int year, int month, int day, int hour, int minute)
    {
        ExecutionHarness h = Harness();
        h.Broker.AddPosition(ExecutionData.Symbol, Side.Buy, 1000.0, 1.0864, 1.0823, 1.0943, RangeLabel(id), "c");
        var exitUtc = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

        Assert.False(h.Trades.RangeExitDue(exitUtc.AddSeconds(-1.0), new SessionRangeParameters().ExitNy));
        Assert.True(h.Trades.RangeExitDue(exitUtc, new SessionRangeParameters().ExitNy));
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void RangeExit_CloseRejected_CloseFailedAlertedAndTriedAgainNextTime()
    {
        ExecutionHarness h = Harness();
        TimeSpan exit = new SessionRangeParameters().ExitNy;
        long mine = h.Broker.AddPosition(ExecutionData.Symbol, Side.Buy, 1000.0, 1.0864, 1.0823, 1.0943, RangeLabel(BuyId), "c");
        DateTime at = ExecutionData.At(14, 21, 30);
        h.Broker.AdvanceTo(at);
        h.Broker.Script(BrokerCall.Close, BrokerFault.Rejected, "Busy");

        Assert.Equal(new TradeAction(mine, TradeActionOutcome.CloseFailed, "range exit"), Assert.Single(h.Trades.RangeExit(at, exit)));
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.CloseFailed));
        Assert.Equal(new TradeAction(mine, TradeActionOutcome.Closed, "range exit"), Assert.Single(h.Trades.RangeExit(at.AddMinutes(1.0), exit)));
        Assert.Empty(h.Broker.GetPositions());
    }

    [Fact]
    [Trait("Req", "EXE-025")]
    public void RangeExit_ServerTimeNotUtc_Throws()
    {
        ExecutionHarness h = Harness();
        TimeSpan exit = new SessionRangeParameters().ExitNy;
        DateTime local = DateTime.SpecifyKind(ExecutionData.At(14, 21, 30), DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => h.Trades.RangeExitDue(local, exit));
        Assert.Throws<ArgumentException>(() => h.Trades.RangeExit(local, exit));
    }
}
