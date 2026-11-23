using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Execution;

/// <summary>What happened to one bot position or order in a <see cref="TradeManager"/> action.</summary>
public enum TradeActionOutcome
{
    /// <summary>A missing stop loss was set from the comment (EXE-011).</summary>
    StopRestored,

    /// <summary>The position was closed (EXE-011, EXE-013).</summary>
    Closed,

    /// <summary>The close request did not succeed; an alert was raised.</summary>
    CloseFailed,

    /// <summary>The pending order was cancelled (EXE-013, RSK-006).</summary>
    Cancelled,

    /// <summary>The cancel request did not succeed; an alert was raised.</summary>
    CancelFailed,
}

/// <summary>One action of <see cref="TradeManager"/> on a position or order.</summary>
/// <param name="Id">Broker id of the position or order.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Detail">Short invariant text.</param>
public sealed record TradeAction(long Id, TradeActionOutcome Outcome, string Detail);

/// <summary>
/// Looks after the bot's open trades on one symbol (EXE-011 to EXE-013): restores missing stops,
/// closes and cancels on Friday evening, and cancels entry orders when risk or news rules ask.
/// Reads the broker at every call and keeps nothing between calls.
/// </summary>
/// <remarks>
/// A partial fill is a normal position: it is protected and closed like any other, and the
/// unfilled remainder stays a pending order that the broker expires (EXE-012). Every request is
/// recorded in the <see cref="RequestCounter"/> (RSK-010) and always sent, because these requests
/// protect the account.
/// </remarks>
public sealed class TradeManager
{
    /// <summary>Attempts to set a missing stop loss before the position is closed (EXE-011).</summary>
    public const int StopAttempts = 2;

    private readonly string _symbol;
    private readonly ExecutionParameters _parameters;
    private readonly IBroker _broker;
    private readonly RequestCounter _counter;
    private readonly IExecutionEvents _events;

    /// <summary>Creates the trade manager of one symbol (EXE-011, EXE-013).</summary>
    /// <param name="symbol">Symbol of this instance; only bot trades on it are managed.</param>
    /// <param name="parameters">Execution parameters; validated here.</param>
    /// <param name="broker">Broker port.</param>
    /// <param name="counter">Request counter of this instance (RSK-010), shared with <see cref="OrderManager"/>.</param>
    /// <param name="events">Hook for alerts.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The symbol is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public TradeManager(string symbol, ExecutionParameters parameters, IBroker broker, RequestCounter counter, IExecutionEvents events)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            throw new ArgumentException("The symbol must not be empty.", nameof(symbol));
        }

        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        _symbol = symbol;
        _parameters = parameters;
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    /// <summary>
    /// Post-fill protection (EXE-011), called on every trade event and bar. For every bot position
    /// on the symbol without a stop loss: an alert is raised, the stop is set from the comment
    /// (the take profit is kept, or taken from the comment when the position has none), and the
    /// broker is read again to confirm it. After <see cref="StopAttempts"/> failed attempts in
    /// this call, or at once when the comment cannot be read, the position is closed and an alert
    /// raised.
    /// </summary>
    /// <returns>One action per position that had no stop loss, ordered by position id.</returns>
    public IReadOnlyList<TradeAction> EnsureProtection()
    {
        var actions = new List<TradeAction>();
        foreach (PositionInfo position in BotPositions().Where(p => !p.StopLoss.HasValue))
        {
            _events.Alert(new ExecutionAlert(AlertKind.StopLossMissing, _symbol, "position " + Text(position.Id) + " has no stop loss"));
            CommentPlan? plan = OrderComment.TryParse(position.Comment);
            if (plan != null && TrySetStop(position, plan))
            {
                _events.Alert(new ExecutionAlert(AlertKind.StopLossRestored, _symbol, "position " + Text(position.Id) + " stop loss set to " + Text(plan.Stop)));
                actions.Add(new TradeAction(position.Id, TradeActionOutcome.StopRestored, "stop " + Text(plan.Stop)));
                continue;
            }

            string why = plan == null ? "comment cannot be read" : "stop loss could not be set in " + Text(StopAttempts) + " attempts";
            actions.Add(Close(position, AlertKind.PositionClosedWithoutStop, why));
        }

        return actions.AsReadOnly();
    }

    /// <summary>
    /// Friday close (EXE-013): on a Friday at or after FridayCloseUtc every bot pending order on
    /// the symbol is cancelled and then every bot position on the symbol is closed. At any other
    /// time nothing is done. Safe to call repeatedly, also after a restart: it acts on whatever
    /// the broker still holds.
    /// </summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    /// <returns>One action per order and position, cancels first; empty when not due.</returns>
    /// <exception cref="ArgumentException">The time is not UTC.</exception>
    public IReadOnlyList<TradeAction> FridayClose(DateTime serverTimeUtc)
    {
        if (!FridayCloseDue(serverTimeUtc))
        {
            return Array.Empty<TradeAction>();
        }

        var actions = new List<TradeAction>(CancelEntryOrders("Friday close"));
        foreach (PositionInfo position in BotPositions())
        {
            actions.Add(Close(position, null, "Friday close"));
        }

        return actions.AsReadOnly();
    }

    /// <summary>
    /// True when the Friday close is due (EXE-013, EXE-016): a Friday at or after FridayCloseUtc.
    /// Sends nothing.
    /// </summary>
    /// <param name="serverTimeUtc">Server time, kind UTC.</param>
    /// <returns>True when <see cref="FridayClose"/> would act on the broker's bot trades.</returns>
    /// <exception cref="ArgumentException">The time is not UTC.</exception>
    public bool FridayCloseDue(DateTime serverTimeUtc)
    {
        if (serverTimeUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The server time must be a UTC time (kind UTC).", nameof(serverTimeUtc));
        }

        return serverTimeUtc.DayOfWeek == DayOfWeek.Friday && serverTimeUtc.TimeOfDay >= _parameters.FridayCloseUtc;
    }

    /// <summary>Cancels every bot pending order on the symbol, for the risk and news rules and the Friday close (EXE-013, RSK-006).</summary>
    /// <param name="reason">Why, for the journal and alerts.</param>
    /// <returns>One action per order, ordered by order id.</returns>
    public IReadOnlyList<TradeAction> CancelEntryOrders(string reason)
    {
        var actions = new List<TradeAction>();
        IEnumerable<PendingOrderInfo> orders = _broker.GetPendingOrders()
            .Where(o => RiskEngine.IsBotLabel(o.Label) && string.Equals(o.Symbol, _symbol, StringComparison.Ordinal))
            .OrderBy(o => o.Id);
        foreach (PendingOrderInfo order in orders)
        {
            OrderResult result = _broker.CancelOrder(order.Id);
            _counter.Record(_broker.ServerTimeUtc);
            if (result.Outcome == OrderOutcome.Success)
            {
                actions.Add(new TradeAction(order.Id, TradeActionOutcome.Cancelled, reason));
            }
            else
            {
                _events.Alert(new ExecutionAlert(AlertKind.CancelFailed, _symbol, "order " + Text(order.Id) + " (" + reason + ") cancel " + result.Outcome + " " + (result.ErrorCode ?? string.Empty)));
                actions.Add(new TradeAction(order.Id, TradeActionOutcome.CancelFailed, reason));
            }
        }

        return actions.AsReadOnly();
    }

    /// <summary>
    /// EXE-025 (spec 3.8, 5.5): true when a bot position of the symbol whose signal id is a session
    /// range id (RH- or RL-, <see cref="SessionRangeId"/>) has reached its timed exit: the server
    /// time is at or after <paramref name="exitNy"/> New York on the date in the id, which is the
    /// close time of the first M15 bar at or after that time, since the exit lies on a 15-minute
    /// boundary. Positions of other kinds are not looked at. The positions are read from the broker.
    /// </summary>
    /// <param name="serverTimeUtc">Broker server time, kind UTC.</param>
    /// <param name="exitNy">RangeExitNy, a New York time of day.</param>
    /// <returns>True when <see cref="RangeExit"/> would close a position.</returns>
    /// <exception cref="ArgumentException">The server time is not UTC.</exception>
    public bool RangeExitDue(DateTime serverTimeUtc, TimeSpan exitNy)
    {
        return RangeExitPositions(serverTimeUtc, exitNy).Count > 0;
    }

    /// <summary>
    /// EXE-025: closes at market, in id order, every position for which <see cref="RangeExitDue"/>
    /// holds, with the detail "range exit". A position that closed is gone from the broker, so it is
    /// closed once; a close that failed is alerted (CloseFailed) and tried again at the next call.
    /// Pending orders and positions of other kinds are not touched.
    /// </summary>
    /// <param name="serverTimeUtc">Broker server time, kind UTC.</param>
    /// <param name="exitNy">RangeExitNy, a New York time of day.</param>
    /// <returns>One action per position, Closed or CloseFailed.</returns>
    /// <exception cref="ArgumentException">The server time is not UTC.</exception>
    public IReadOnlyList<TradeAction> RangeExit(DateTime serverTimeUtc, TimeSpan exitNy)
    {
        var actions = new List<TradeAction>();
        foreach (PositionInfo position in RangeExitPositions(serverTimeUtc, exitNy))
        {
            actions.Add(Close(position, null, "range exit"));
        }

        return actions.AsReadOnly();
    }

    /// <summary>
    /// EXE-024: cancels, in id order, every pending bot order of the symbol whose label carries this
    /// signal id. When no such order is pending nothing is sent. The orders are read from the broker.
    /// </summary>
    /// <param name="signalId">The signal id (zone id) in the label.</param>
    /// <param name="reason">Why, for the actions and alerts.</param>
    /// <returns>One action per order, Cancelled or CancelFailed.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public IReadOnlyList<TradeAction> CancelOrdersOf(string signalId, string reason)
    {
        ArgumentNullException.ThrowIfNull(signalId);
        ArgumentNullException.ThrowIfNull(reason);
        var actions = new List<TradeAction>();
        foreach (PendingOrderInfo order in OrdersOf(signalId))
        {
            OrderResult result = _broker.CancelOrder(order.Id);
            _counter.Record(_broker.ServerTimeUtc);
            if (result.Outcome == OrderOutcome.Success)
            {
                actions.Add(new TradeAction(order.Id, TradeActionOutcome.Cancelled, reason));
            }
            else
            {
                _events.Alert(new ExecutionAlert(AlertKind.CancelFailed, _symbol, "order " + Text(order.Id) + " (" + reason + ") cancel " + result.Outcome + " " + (result.ErrorCode ?? string.Empty)));
                actions.Add(new TradeAction(order.Id, TradeActionOutcome.CancelFailed, reason));
            }
        }

        return actions.AsReadOnly();
    }

    /// <summary>
    /// SIG-033, EXE-024: true when a bot order of the symbol whose label carries this signal id is
    /// pending at the broker now.
    /// </summary>
    /// <param name="signalId">The signal id (zone id) in the label.</param>
    /// <returns>True when such an order is pending.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signalId"/> is null.</exception>
    public bool HasPendingOrderOf(string signalId)
    {
        ArgumentNullException.ThrowIfNull(signalId);
        return OrdersOf(signalId).Count > 0;
    }

    private List<PendingOrderInfo> OrdersOf(string signalId)
    {
        return _broker.GetPendingOrders()
            .Where(o => RiskEngine.IsBotLabel(o.Label) && string.Equals(o.Symbol, _symbol, StringComparison.Ordinal) && string.Equals(IdOf(o.Label), signalId, StringComparison.Ordinal))
            .OrderBy(o => o.Id)
            .ToList();
    }

    private List<PositionInfo> RangeExitPositions(DateTime serverTimeUtc, TimeSpan exitNy)
    {
        if (serverTimeUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The server time must be a UTC time (kind UTC).", nameof(serverTimeUtc));
        }

        return BotPositions()
            .Where(p => SessionRangeId.TryTradingDate(IdOf(p.Label), out DateTime tradingDate) && serverTimeUtc >= NewYorkClock.UtcInTradingDay(tradingDate, exitNy))
            .ToList();
    }

    private static string? IdOf(string label)
    {
        return OrderLabel.TryParse(label, out string _, out string zoneId) ? zoneId : null;
    }

    private bool TrySetStop(PositionInfo position, CommentPlan plan)
    {
        double? takeProfit = position.TakeProfit ?? plan.Target;
        for (int attempt = 1; attempt <= StopAttempts; attempt++)
        {
            _broker.ModifyPositionProtection(position.Id, plan.Stop, takeProfit);
            _counter.Record(_broker.ServerTimeUtc);

            // Whatever the result said, the broker's position decides (Unclear may have worked, Success may not have).
            PositionInfo? now = _broker.GetPositions().FirstOrDefault(p => p.Id == position.Id);
            if (now == null || now.StopLoss.HasValue)
            {
                return true;
            }
        }

        return false;
    }

    private TradeAction Close(PositionInfo position, AlertKind? alert, string reason)
    {
        OrderResult result = _broker.ClosePosition(position.Id);
        _counter.Record(_broker.ServerTimeUtc);
        if (result.Outcome != OrderOutcome.Success)
        {
            _events.Alert(new ExecutionAlert(AlertKind.CloseFailed, _symbol, "position " + Text(position.Id) + " (" + reason + ") close " + result.Outcome + " " + (result.ErrorCode ?? string.Empty)));
            return new TradeAction(position.Id, TradeActionOutcome.CloseFailed, reason);
        }

        if (alert.HasValue)
        {
            _events.Alert(new ExecutionAlert(alert.Value, _symbol, "position " + Text(position.Id) + " closed: " + reason));
        }

        return new TradeAction(position.Id, TradeActionOutcome.Closed, reason);
    }

    private IEnumerable<PositionInfo> BotPositions()
    {
        return _broker.GetPositions()
            .Where(p => RiskEngine.IsBotLabel(p.Label) && string.Equals(p.Symbol, _symbol, StringComparison.Ordinal))
            .OrderBy(p => p.Id)
            .ToList();
    }
}
