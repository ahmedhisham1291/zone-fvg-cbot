using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Execution;

/// <summary>What happened to one placement (EXE-009, EXE-010).</summary>
public enum PlaceOutcome
{
    /// <summary>The order is at the broker with the requested stop, target and expiry, or it has already filled.</summary>
    Placed,

    /// <summary>An order or position with the label already exists; nothing was sent (EXE-009 step 1).</summary>
    AlreadyExists,

    /// <summary>An earlier placement of the label has an unclear outcome; nothing was sent until it is resolved (EXE-009).</summary>
    AwaitingResolution,

    /// <summary>The broker clearly rejected the order; it is not retried (EXE-009).</summary>
    Rejected,

    /// <summary>The outcome is unclear; the placement is kept for <see cref="OrderManager.ResolvePending"/> (EXE-009).</summary>
    Unclear,

    /// <summary>The order read back differs from the request in stop, target or expiry; a cancel was sent and an alert raised (EXE-010).</summary>
    MismatchCancelled,

    /// <summary>The broker reported success but neither the order nor a position with the label can be found; an alert was raised and nothing is resent (EXE-010).</summary>
    NotFound,

    /// <summary>The request limit would be exceeded; nothing was sent (RSK-010).</summary>
    RequestLimitReached,

    /// <summary>An unclear placement found nothing at the broker and may not be resent (EXE-009).</summary>
    Abandoned,
}

/// <summary>Result of one placement or resolution (EXE-009, EXE-010).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Label">Label of the order.</param>
/// <param name="OrderId">Broker id of the order, when known.</param>
/// <param name="Detail">Short invariant text, for example the broker's error code.</param>
public sealed record PlaceResult(PlaceOutcome Outcome, string Label, long? OrderId, string Detail);

/// <summary>
/// Sends entry orders idempotently (EXE-009) and checks them after placement (EXE-010): limit
/// orders through <see cref="IBroker.PlaceLimitOrder"/>, stop orders through
/// <see cref="IBroker.PlaceStopOrder"/> (EXE-018).
/// </summary>
/// <remarks>
/// <para>The broker is the only source of truth: every placement and every resolution starts
/// with a query for the label. The only memory is a short in-memory list of placements whose
/// outcome was Unclear; losing it in a restart is harmless, because a restarted bot never
/// executes a replayed signal (EXE-001) and the broker query comes first.</para>
/// <para>Every request is recorded in the <see cref="RequestCounter"/> (RSK-010). An entry order
/// is not sent when it would exceed the hour or day limit. Cancels sent by EXE-010 are always
/// sent, because they remove an order with the wrong protection.</para>
/// <para>Read-back tolerance (EXE-010): prices within half a price step of the request, expiry
/// within one second.</para>
/// </remarks>
public sealed class OrderManager
{
    private static readonly TimeSpan BarLength = TimeSpan.FromMinutes(15.0);
    private static readonly TimeSpan ExpiryTolerance = TimeSpan.FromSeconds(1.0);

    private readonly IBroker _broker;
    private readonly RequestCounter _counter;
    private readonly IExecutionEvents _events;
    private readonly List<PlannedOrder> _unresolved = new List<PlannedOrder>();
    private readonly bool _requestLimitRecordOnly;

    /// <summary>Creates the order manager (EXE-009).</summary>
    /// <param name="broker">Broker port.</param>
    /// <param name="counter">Request counter of this instance (RSK-010), shared with <see cref="TradeManager"/>.</param>
    /// <param name="events">Hook for skip records and alerts.</param>
    /// <param name="requestLimitRecordOnly">
    /// REC-014, backtests only: when true an entry request is sent even when it exceeds the request
    /// limit; the request is counted and the bot records the would-be halt. Default false (live).
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public OrderManager(IBroker broker, RequestCounter counter, IExecutionEvents events, bool requestLimitRecordOnly = false)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _requestLimitRecordOnly = requestLimitRecordOnly;
    }

    /// <summary>Number of placements whose outcome is still unclear (EXE-009).</summary>
    public int UnresolvedCount => _unresolved.Count;

    /// <summary>True when a placement on the symbol has an unclear outcome; the symbol then counts as busy (EXE-002, EXE-009).</summary>
    /// <param name="symbol">Symbol name.</param>
    /// <returns>True when such a placement exists.</returns>
    public bool HasUnresolved(string symbol)
    {
        return _unresolved.Any(order => string.Equals(order.Symbol, symbol, StringComparison.Ordinal));
    }

    /// <summary>
    /// Places the order idempotently (EXE-009): if an order or position with its label exists at
    /// the broker, or an earlier placement of the label is unresolved, nothing is sent. Otherwise
    /// one request is sent and its result handled: success is read back and checked (EXE-010); a
    /// clear rejection is recorded and alerted and never retried; an unclear outcome is kept for
    /// <see cref="ResolvePending"/> and never resent here.
    /// </summary>
    /// <param name="order">The checked order from <see cref="PreTradeFilters"/>.</param>
    /// <returns>What happened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> is null.</exception>
    public PlaceResult TryPlace(PlannedOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (_unresolved.Any(pending => string.Equals(pending.Label, order.Label, StringComparison.Ordinal)))
        {
            return new PlaceResult(PlaceOutcome.AwaitingResolution, order.Label, null, "earlier placement unresolved");
        }

        return Send(order);
    }

    /// <summary>
    /// Resolves the placements with an unclear outcome; called at each M15 close (EXE-009). For each:
    /// the broker is queried for the label. If an order exists it is checked as after a successful
    /// placement (EXE-010); if a position exists the placement is done. If nothing exists the
    /// order is resent once only when the signal is at most one M15 bar old, at least one bar has
    /// closed since its confirmation and no such bar traded through the entry, and at least 15
    /// minutes of order life are left; otherwise it is abandoned and recorded. Traded through, for a
    /// limit order: a low at or below the order's entry for a buy, a high at or above it for a
    /// sell (EXE-009); for a stop order: a high at or above the signal's entry (before the spread)
    /// for a buy, a low at or below it for a sell (EXE-020). Until a bar has closed after the
    /// confirmation, a placement with nothing at the broker is kept.
    /// </summary>
    /// <param name="recentM15Bars">Closed M15 bars of the symbol, at least those since the oldest unresolved confirmation.</param>
    /// <returns>One result per placement that was resolved, in the order the placements were made.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="recentM15Bars"/> is null.</exception>
    public IReadOnlyList<PlaceResult> ResolvePending(IReadOnlyList<Bar> recentM15Bars)
    {
        ArgumentNullException.ThrowIfNull(recentM15Bars);
        var results = new List<PlaceResult>();
        DateTime now = _broker.ServerTimeUtc;
        foreach (PlannedOrder order in _unresolved.ToList())
        {
            PlaceResult? result = Resolve(order, recentM15Bars, now);
            if (result != null)
            {
                results.Add(result);
            }
        }

        return results.AsReadOnly();
    }

    private PlaceResult? Resolve(PlannedOrder order, IReadOnlyList<Bar> bars, DateTime now)
    {
        PendingOrderInfo? existing = FindOrder(order.Label, null);
        if (existing != null)
        {
            _unresolved.Remove(order);
            return Verify(order, existing);
        }

        PositionInfo? position = FindPosition(order.Label);
        if (position != null)
        {
            _unresolved.Remove(order);
            return new PlaceResult(PlaceOutcome.Placed, order.Label, null, "filled as position " + Text(position.Id));
        }

        TimeSpan age = now - order.Signal.ConfirmationCloseUtc;
        bool barClosed = age >= BarLength;
        if (!barClosed && age >= TimeSpan.Zero)
        {
            return null;
        }

        _unresolved.Remove(order);
        string? reason = ResendBlocker(order, bars, now, age);
        if (reason != null)
        {
            var skip = new SkipRecord(order.Symbol, order.Signal.ZoneId, SkipReason.UnclearOutcomeNotResent, age.TotalMinutes, ExecutionParameters.MaxSignalAgeMinutes, reason, now);
            _events.Skipped(skip);
            return new PlaceResult(PlaceOutcome.Abandoned, order.Label, null, reason);
        }

        return Send(order);
    }

    private static string? ResendBlocker(PlannedOrder order, IReadOnlyList<Bar> bars, DateTime now, TimeSpan age)
    {
        if (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(ExecutionParameters.MaxSignalAgeMinutes))
        {
            return "signal age " + Text(age.TotalMinutes) + " minutes, no longer fresh";
        }

        if (order.ExpiryUtc - now < TimeSpan.FromMinutes(ExecutionParameters.MinOrderLifeMinutes))
        {
            return "less than 15 minutes of order life left";
        }

        DateTime confirmation = order.Signal.ConfirmationCloseUtc;
        Bar[] since = bars.Where(bar => bar != null && bar.OpenTimeUtc >= confirmation && bar.OpenTimeUtc + BarLength <= now).ToArray();
        if (!since.Any(bar => bar.OpenTimeUtc == confirmation))
        {
            return "no closed bar since the confirmation, price path unknown";
        }

        if (order.EntryType == EntryOrderType.Stop)
        {
            return StopReached(order, since);
        }

        bool through = order.Side == Side.Buy
            ? since.Any(bar => !PriceMath.GreaterThan(bar.Low, order.Entry, PriceMath.DefaultTolerance))
            : since.Any(bar => !PriceMath.LessThan(bar.High, order.Entry, PriceMath.DefaultTolerance));
        return through ? "price traded through the entry " + Text(order.Entry) : null;
    }

    // EXE-020: a closed bar reached the signal's entry, the level before the spread was added.
    private static string? StopReached(PlannedOrder order, Bar[] since)
    {
        double level = order.Signal.Entry;
        bool reached = order.Side == Side.Buy
            ? since.Any(bar => !PriceMath.LessThan(bar.High, level, PriceMath.DefaultTolerance))
            : since.Any(bar => !PriceMath.GreaterThan(bar.Low, level, PriceMath.DefaultTolerance));
        return reached ? "price traded through the entry " + Text(level) : null;
    }

    private PlaceResult Send(PlannedOrder order)
    {
        if (FindOrder(order.Label, null) != null || FindPosition(order.Label) != null)
        {
            return new PlaceResult(PlaceOutcome.AlreadyExists, order.Label, null, "label exists at the broker");
        }

        DateTime now = _broker.ServerTimeUtc;
        if (!_requestLimitRecordOnly && !EntryRequestAllowed(now))
        {
            string detail = "request limit would be exceeded";
            _events.Skipped(new SkipRecord(order.Symbol, order.Signal.ZoneId, SkipReason.RequestLimitReached, 0.0, 0.0, detail, now));
            _events.Alert(new ExecutionAlert(AlertKind.RequestLimitReached, order.Symbol, order.Label + ": " + detail));
            return new PlaceResult(PlaceOutcome.RequestLimitReached, order.Label, null, detail);
        }

        OrderResult result = order.EntryType == EntryOrderType.Stop
            ? _broker.PlaceStopOrder(order.ToStopRequest())
            : _broker.PlaceLimitOrder(order.ToRequest());
        _counter.Record(now);
        if (result.Outcome == OrderOutcome.Success)
        {
            return AfterSuccess(order, result.Id);
        }

        if (result.Outcome == OrderOutcome.Rejected)
        {
            string code = result.ErrorCode ?? "no code";
            _events.Skipped(new SkipRecord(order.Symbol, order.Signal.ZoneId, SkipReason.OrderRejected, 0.0, 0.0, "rejected: " + code, now));
            _events.Alert(new ExecutionAlert(AlertKind.OrderRejected, order.Symbol, order.Label + " rejected: " + code));
            return new PlaceResult(PlaceOutcome.Rejected, order.Label, null, code);
        }

        // Unclear, or an undefined outcome: never resend blindly (EXE-009).
        _unresolved.Add(order);
        return new PlaceResult(PlaceOutcome.Unclear, order.Label, null, result.ErrorCode ?? "unclear");
    }

    private PlaceResult AfterSuccess(PlannedOrder order, long? id)
    {
        PendingOrderInfo? placed = FindOrder(order.Label, id);
        if (placed != null)
        {
            return Verify(order, placed);
        }

        PositionInfo? position = FindPosition(order.Label);
        if (position != null)
        {
            return new PlaceResult(PlaceOutcome.Placed, order.Label, id, "filled as position " + Text(position.Id));
        }

        _events.Alert(new ExecutionAlert(AlertKind.OrderNotFound, order.Symbol, order.Label + ": accepted but not found at the broker"));
        return new PlaceResult(PlaceOutcome.NotFound, order.Label, id, "accepted but not found");
    }

    private PlaceResult Verify(PlannedOrder order, PendingOrderInfo placed)
    {
        string? mismatch = Mismatch(order, placed);
        if (mismatch == null)
        {
            return new PlaceResult(PlaceOutcome.Placed, order.Label, placed.Id, "verified");
        }

        OrderResult cancel = _broker.CancelOrder(placed.Id);
        _counter.Record(_broker.ServerTimeUtc);
        _events.Alert(new ExecutionAlert(AlertKind.OrderMismatch, order.Symbol, order.Label + " order " + Text(placed.Id) + " cancelled: " + mismatch));
        if (cancel.Outcome != OrderOutcome.Success)
        {
            _events.Alert(new ExecutionAlert(AlertKind.CancelFailed, order.Symbol, order.Label + " order " + Text(placed.Id) + " cancel " + cancel.Outcome + " " + (cancel.ErrorCode ?? string.Empty)));
        }

        return new PlaceResult(PlaceOutcome.MismatchCancelled, order.Label, placed.Id, mismatch);
    }

    private static string? Mismatch(PlannedOrder order, PendingOrderInfo placed)
    {
        // EXE-017: half a tick, plus one billionth of a tick for floating-point noise.
        double tolerance = (0.5 * order.TickSize) + (1e-9 * order.TickSize);
        if (!placed.StopLoss.HasValue || Math.Abs(placed.StopLoss.Value - order.StopLoss) > tolerance)
        {
            return "stop loss " + Price(placed.StopLoss) + " instead of " + Text(order.StopLoss);
        }

        if (!placed.TakeProfit.HasValue || Math.Abs(placed.TakeProfit.Value - order.TakeProfit) > tolerance)
        {
            return "take profit " + Price(placed.TakeProfit) + " instead of " + Text(order.TakeProfit);
        }

        if (!placed.ExpiryUtc.HasValue || (placed.ExpiryUtc.Value - order.ExpiryUtc).Duration() > ExpiryTolerance)
        {
            string actual = placed.ExpiryUtc.HasValue ? placed.ExpiryUtc.Value.ToString("o", CultureInfo.InvariantCulture) : "none";
            return "expiry " + actual + " instead of " + order.ExpiryUtc.ToString("o", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private bool EntryRequestAllowed(DateTime now)
    {
        CircuitBreakerDecision decision = _counter.Evaluate(now);
        bool hour = decision.LastHourCount + 1 <= decision.HourLimit;
        bool day = !decision.DayLimit.HasValue || decision.TodayCount + 1 <= decision.DayLimit.Value;
        return !decision.Halt && hour && day;
    }

    private PendingOrderInfo? FindOrder(string label, long? id)
    {
        IReadOnlyList<PendingOrderInfo> orders = _broker.GetPendingOrders();
        PendingOrderInfo? byId = id.HasValue ? orders.FirstOrDefault(o => o.Id == id.Value && string.Equals(o.Label, label, StringComparison.Ordinal)) : null;
        return byId ?? orders.Where(o => string.Equals(o.Label, label, StringComparison.Ordinal)).OrderBy(o => o.Id).FirstOrDefault();
    }

    private PositionInfo? FindPosition(string label)
    {
        return _broker.GetPositions().Where(p => string.Equals(p.Label, label, StringComparison.Ordinal)).OrderBy(p => p.Id).FirstOrDefault();
    }

    private static string Price(double? value)
    {
        return value.HasValue ? Text(value.Value) : "none";
    }
}
