using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// The explicit input of every risk decision (RSK-011): account, all positions, all pending
/// orders, the closed-trade history since <see cref="HistorySinceUtc"/>, symbol data and the
/// server time. Built by the caller from the broker port; the risk engine never calls the
/// broker itself.
/// </summary>
/// <remarks>
/// The constructor checks the data and throws <see cref="ArgumentException"/> for anything
/// missing or not finite, so a decision is never made on broken data (fail closed). Every list
/// is copied and put into a fixed order (positions and orders by id, history by close time and
/// then by every other field, symbols by name), so the order in which the broker returned the
/// data cannot change a sum or a decision. Symbol data values are checked where they are used.
/// </remarks>
public sealed class RiskSnapshot
{
    /// <summary>Creates a snapshot and checks its data (RSK-011).</summary>
    /// <param name="serverTimeUtc">Server time from the broker port; kind UTC.</param>
    /// <param name="account">Account values.</param>
    /// <param name="positions">All open positions of the account, bot or not.</param>
    /// <param name="pendingOrders">All pending orders of the account, bot or not.</param>
    /// <param name="history">Closed trades of the account since <paramref name="historySinceUtc"/>; may contain older ones.</param>
    /// <param name="historySinceUtc">Earliest close time the history is complete from; kind UTC.</param>
    /// <param name="symbols">Symbol data, at least for every symbol with a bot position or order and for the new trade.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A value is missing, not finite, not UTC, or an id or symbol name is duplicated.</exception>
    public RiskSnapshot(
        DateTime serverTimeUtc,
        AccountSnapshot account,
        IEnumerable<PositionInfo> positions,
        IEnumerable<PendingOrderInfo> pendingOrders,
        IEnumerable<ClosedTradeInfo> history,
        DateTime historySinceUtc,
        IEnumerable<SymbolInfo> symbols)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(pendingOrders);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(symbols);
        RequireUtc(serverTimeUtc, nameof(serverTimeUtc));
        RequireUtc(historySinceUtc, nameof(historySinceUtc));
        CheckAccount(account);

        PositionInfo[] positionArray = positions.ToArray();
        PendingOrderInfo[] orderArray = pendingOrders.ToArray();
        ClosedTradeInfo[] historyArray = history.ToArray();
        SymbolInfo[] symbolArray = symbols.ToArray();
        Array.ForEach(positionArray, CheckPosition);
        Array.ForEach(orderArray, CheckOrder);
        Array.ForEach(historyArray, CheckClosedTrade);
        Array.ForEach(symbolArray, CheckSymbol);
        RequireUnique(positionArray.Select(p => p.Id.ToString(CultureInfo.InvariantCulture)), nameof(positions));
        RequireUnique(orderArray.Select(o => o.Id.ToString(CultureInfo.InvariantCulture)), nameof(pendingOrders));
        RequireUnique(symbolArray.Select(s => s.Name), nameof(symbols));

        ServerTimeUtc = serverTimeUtc;
        HistorySinceUtc = historySinceUtc;
        Account = account;
        Positions = ReadOnly(positionArray.OrderBy(p => p.Id));
        PendingOrders = ReadOnly(orderArray.OrderBy(o => o.Id));
        History = ReadOnly(historyArray
            .OrderBy(h => h.CloseTimeUtc)
            .ThenBy(h => h.PositionId)
            .ThenBy(h => h.NetProfit)
            .ThenBy(h => h.BalanceAfterClose)
            .ThenBy(h => h.Commission)
            .ThenBy(h => h.Swap)
            .ThenBy(h => h.EntryTimeUtc)
            .ThenBy(h => h.Symbol, StringComparer.Ordinal)
            .ThenBy(h => h.Label, StringComparer.Ordinal));
        Symbols = ReadOnly(symbolArray.OrderBy(s => s.Name, StringComparer.Ordinal));
    }

    /// <summary>Server time of the snapshot, kind UTC. "Now" for every decision (RSK-011, RSK-012).</summary>
    public DateTime ServerTimeUtc { get; }

    /// <summary>Earliest close time from which <see cref="History"/> is complete, kind UTC (RSK-007, RSK-008).</summary>
    public DateTime HistorySinceUtc { get; }

    /// <summary>Account values.</summary>
    public AccountSnapshot Account { get; }

    /// <summary>All open positions, ordered by id.</summary>
    public IReadOnlyList<PositionInfo> Positions { get; }

    /// <summary>All pending orders, ordered by id.</summary>
    public IReadOnlyList<PendingOrderInfo> PendingOrders { get; }

    /// <summary>Closed trades, ordered by close time, then position id, then the remaining fields.</summary>
    public IReadOnlyList<ClosedTradeInfo> History { get; }

    /// <summary>Symbol data, ordered by name (ordinal).</summary>
    public IReadOnlyList<SymbolInfo> Symbols { get; }

    /// <summary>The symbol data with this exact name (ordinal comparison), or null when there is none.</summary>
    /// <param name="name">Symbol name.</param>
    /// <returns>The symbol data, or null.</returns>
    public SymbolInfo? FindSymbol(string name)
    {
        foreach (SymbolInfo symbol in Symbols)
        {
            if (string.Equals(symbol.Name, name, StringComparison.Ordinal))
            {
                return symbol;
            }
        }

        return null;
    }

    private static ReadOnlyCollection<T> ReadOnly<T>(IEnumerable<T> items)
    {
        return Array.AsReadOnly(items.ToArray());
    }

    private static void CheckAccount(AccountSnapshot account)
    {
        RequireFinite(account.Balance, "account balance");
        RequireFinite(account.Equity, "account equity");
        RequireFinite(account.FreeMargin, "account free margin");
        RequireText(account.Currency, "account currency");
    }

    private static void CheckPosition(PositionInfo position)
    {
        Require(position != null, "a position is null");
        CheckTrade(position!.Symbol, position.Side, position.VolumeUnits, position.EntryPrice, position.StopLoss, position.TakeProfit, position.Label, position.Comment, "position");
        RequireUtc(position.OpenTimeUtc, "position open time");
    }

    private static void CheckOrder(PendingOrderInfo order)
    {
        Require(order != null, "a pending order is null");
        CheckTrade(order!.Symbol, order.Side, order.VolumeUnits, order.EntryPrice, order.StopLoss, order.TakeProfit, order.Label, order.Comment, "pending order");
        RequireUtc(order.CreatedTimeUtc, "pending order creation time");
    }

    private static void CheckTrade(string symbol, Side side, double volume, double entry, double? stop, double? target, string label, string comment, string what)
    {
        RequireText(symbol, what + " symbol");
        RequireText(label, what + " label");
        RequireText(comment, what + " comment");
        Require(RiskMath.IsDefined(side), "a " + what + " side is not Buy or Sell");
        Require(RiskMath.IsPositive(volume), "a " + what + " volume must be a finite number greater than zero");
        Require(RiskMath.IsPositive(entry), "a " + what + " entry price must be a finite number greater than zero");
        Require(!stop.HasValue || RiskMath.IsPositive(stop.Value), "a " + what + " stop loss must be a finite price greater than zero");
        Require(!target.HasValue || RiskMath.IsPositive(target.Value), "a " + what + " take profit must be a finite price greater than zero");
    }

    private static void CheckClosedTrade(ClosedTradeInfo trade)
    {
        Require(trade != null, "a closed trade is null");
        RequireText(trade!.Symbol, "closed trade symbol");
        RequireText(trade.Label, "closed trade label");
        RequireUtc(trade.EntryTimeUtc, "closed trade entry time");
        RequireUtc(trade.CloseTimeUtc, "closed trade close time");
        RequireFinite(trade.NetProfit, "closed trade net profit");
        RequireFinite(trade.Commission, "closed trade commission");
        RequireFinite(trade.Swap, "closed trade swap");
        RequireFinite(trade.BalanceAfterClose, "closed trade balance after close");
    }

    private static void CheckSymbol(SymbolInfo symbol)
    {
        Require(symbol != null, "a symbol is null");
        RequireText(symbol!.Name, "symbol name");
    }

    private static void RequireUnique(IEnumerable<string> keys, string name)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (!seen.Add(key))
            {
                throw new ArgumentException("Duplicate entry " + key + " in " + name + ".", name);
            }
        }
    }

    private static void RequireUtc(DateTime value, string name)
    {
        Require(value.Kind == DateTimeKind.Utc, "the " + name + " must be a UTC time (kind UTC)");
    }

    private static void RequireFinite(double value, string name)
    {
        Require(double.IsFinite(value), "the " + name + " must be a finite number");
    }

    private static void RequireText(string? value, string name)
    {
        Require(value != null, "the " + name + " is missing");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException("Invalid risk snapshot: " + message + ".");
        }
    }
}
