using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Recovery;

/// <summary>
/// The closing actions that WP06's <see cref="TradeManager"/> does not offer: the emergency close
/// of every bot trade of the account (RSK-009) and the weekend close of the symbol's bot trades
/// (REC-009, owner decision of 2026-11-16). Reads the broker at every call and keeps nothing.
/// </summary>
/// <remarks>
/// Orders are cancelled first, then positions closed, as in the Friday close (EXE-013). Every
/// request is recorded in the <see cref="RequestCounter"/> (RSK-010) and always sent, because these
/// requests protect the account (owner decision of 2026-11-16). A request that does not succeed
/// raises an alert.
/// </remarks>
public sealed class SafetyActions
{
    private readonly string _symbol;
    private readonly ExecutionParameters _parameters;
    private readonly IBroker _broker;
    private readonly RequestCounter _counter;
    private readonly IExecutionEvents _events;

    // EXE-033, row BA: true in an Overnight instance; the symbol's actions are limited to its own trades.
    private readonly bool _overnight;

    /// <summary>Creates the actions of one instance (RSK-009, REC-009) that is not in Overnight mode.</summary>
    /// <param name="symbol">Symbol of this instance.</param>
    /// <param name="parameters">Execution parameters (FridayCloseUtc); validated here.</param>
    /// <param name="broker">Broker port.</param>
    /// <param name="counter">Request counter of this instance (RSK-010).</param>
    /// <param name="events">Hook for alerts.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The symbol is empty.</exception>
    public SafetyActions(string symbol, ExecutionParameters parameters, IBroker broker, RequestCounter counter, IExecutionEvents events)
        : this(symbol, parameters, broker, counter, events, false)
    {
    }

    /// <summary>
    /// Creates the actions of one instance (RSK-009, REC-009, EXE-033). The weekend close and the
    /// exposure check look only at the trades of the symbol the instance owns
    /// (<see cref="InstanceScope.Owns"/>); the emergency close of the account is not limited.
    /// </summary>
    /// <param name="symbol">Symbol of this instance.</param>
    /// <param name="parameters">Execution parameters (FridayCloseUtc); validated here.</param>
    /// <param name="broker">Broker port.</param>
    /// <param name="counter">Request counter of this instance (RSK-010).</param>
    /// <param name="events">Hook for alerts.</param>
    /// <param name="overnightInstance">True for an instance in Overnight mode (EXE-033).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The symbol is empty.</exception>
    public SafetyActions(string symbol, ExecutionParameters parameters, IBroker broker, RequestCounter counter, IExecutionEvents events, bool overnightInstance)
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
        _overnight = overnightInstance;
    }

    /// <summary>
    /// True inside the weekend window of the owner decision of 2026-11-16 (REC-009): Friday at or
    /// after FridayCloseUtc, Saturday, or Sunday (UTC).
    /// </summary>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <returns>True inside the window.</returns>
    public bool InWeekendWindow(DateTime nowUtc)
    {
        return (nowUtc.DayOfWeek == DayOfWeek.Friday && nowUtc.TimeOfDay >= _parameters.FridayCloseUtc)
            || nowUtc.DayOfWeek == DayOfWeek.Saturday
            || nowUtc.DayOfWeek == DayOfWeek.Sunday;
    }

    /// <summary>True when the broker holds a bot position or bot pending order on the symbol that the instance owns (REC-009, EXE-033).</summary>
    /// <returns>True when there is something to close or cancel.</returns>
    public bool HasSymbolExposure()
    {
        return _broker.GetPositions().Any(p => IsBotOn(p.Label, p.Symbol, _symbol) && InstanceScope.Owns(_overnight, p.Label))
            || _broker.GetPendingOrders().Any(o => IsBotOn(o.Label, o.Symbol, _symbol) && InstanceScope.Owns(_overnight, o.Label));
    }

    /// <summary>True when the broker reports the symbol's market open, so requests are accepted again (owner decision of 2026-11-16).</summary>
    /// <returns>True when the market is open.</returns>
    public bool MarketOpen()
    {
        return _broker.GetSymbol(_symbol).IsMarketOpen;
    }

    /// <summary>
    /// Emergency close (RSK-009, spec 4.2 "close all positions, cancel orders"): cancels every bot
    /// pending order of the account, then closes every bot position of the account, whatever its
    /// symbol.
    /// </summary>
    /// <param name="reason">Why, for alerts.</param>
    /// <returns>One action per order and position, cancels first, each group ordered by id.</returns>
    public IReadOnlyList<TradeAction> CloseAllBotTrades(string reason)
    {
        return CloseWhere((symbol, label) => true, reason);
    }

    /// <summary>
    /// Weekend close (REC-009, owner decision of 2026-11-16): cancels the bot pending orders of the
    /// symbol, then closes its bot positions, of the trades the instance owns (EXE-033). The caller
    /// checks that the market is open.
    /// </summary>
    /// <param name="reason">Why, for alerts.</param>
    /// <returns>One action per order and position, cancels first, each group ordered by id.</returns>
    public IReadOnlyList<TradeAction> CloseSymbolTrades(string reason)
    {
        return CloseWhere((symbol, label) => string.Equals(symbol, _symbol, StringComparison.Ordinal) && InstanceScope.Owns(_overnight, label), reason);
    }

    private IReadOnlyList<TradeAction> CloseWhere(Func<string, string, bool> filter, string reason)
    {
        var actions = new List<TradeAction>();
        List<PendingOrderInfo> orders = _broker.GetPendingOrders()
            .Where(o => RiskEngine.IsBotLabel(o.Label) && filter(o.Symbol, o.Label))
            .OrderBy(o => o.Id)
            .ToList();
        foreach (PendingOrderInfo order in orders)
        {
            OrderResult result = _broker.CancelOrder(order.Id);
            _counter.Record(_broker.ServerTimeUtc);
            if (result.Outcome == OrderOutcome.Success)
            {
                actions.Add(new TradeAction(order.Id, TradeActionOutcome.Cancelled, reason));
                continue;
            }

            _events.Alert(new ExecutionAlert(AlertKind.CancelFailed, order.Symbol, "order " + Text(order.Id) + " (" + reason + ") cancel " + result.Outcome + " " + (result.ErrorCode ?? string.Empty)));
            actions.Add(new TradeAction(order.Id, TradeActionOutcome.CancelFailed, reason));
        }

        List<PositionInfo> positions = _broker.GetPositions()
            .Where(p => RiskEngine.IsBotLabel(p.Label) && filter(p.Symbol, p.Label))
            .OrderBy(p => p.Id)
            .ToList();
        foreach (PositionInfo position in positions)
        {
            OrderResult result = _broker.ClosePosition(position.Id);
            _counter.Record(_broker.ServerTimeUtc);
            if (result.Outcome == OrderOutcome.Success)
            {
                actions.Add(new TradeAction(position.Id, TradeActionOutcome.Closed, reason));
                continue;
            }

            _events.Alert(new ExecutionAlert(AlertKind.CloseFailed, position.Symbol, "position " + Text(position.Id) + " (" + reason + ") close " + result.Outcome + " " + (result.ErrorCode ?? string.Empty)));
            actions.Add(new TradeAction(position.Id, TradeActionOutcome.CloseFailed, reason));
        }

        return actions.AsReadOnly();
    }

    private static bool IsBotOn(string label, string symbol, string wanted)
    {
        return RiskEngine.IsBotLabel(label) && string.Equals(symbol, wanted, StringComparison.Ordinal);
    }
}
