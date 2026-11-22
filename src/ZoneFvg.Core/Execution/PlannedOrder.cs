using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Execution;

/// <summary>
/// A signal that passed every pre-trade filter, with its checked levels, volume and expiry
/// (EXE-001 to EXE-008, EXE-015, RSK-001 to RSK-006). A data record built by
/// <see cref="PreTradeFilters"/>; <see cref="OrderManager"/> sends it.
/// </summary>
/// <param name="Signal">The signal the order comes from.</param>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Side">Direction.</param>
/// <param name="Entry">Entry (limit or stop) price on the symbol's price grid; for a breakout buy the spread is included (EXE-019).</param>
/// <param name="StopLoss">Stop-loss price on the grid; for a sell the spread is included (EXE-007).</param>
/// <param name="TakeProfit">Take-profit price on the grid.</param>
/// <param name="VolumeUnits">Volume in units (RSK-001).</param>
/// <param name="RiskMoney">Money lost at the stop with this volume, without commission (RSK-001).</param>
/// <param name="StopPips">Stop distance in pips.</param>
/// <param name="ExpiryUtc">Order expiry, kind UTC (EXE-015).</param>
/// <param name="PriceDigits">Price digits of the symbol from the broker (<see cref="SymbolInfo.Digits"/>, EXE-017).</param>
/// <param name="Label">Order label (EXE-008).</param>
/// <param name="Comment">Order comment (EXE-008).</param>
/// <param name="PlannedAtUtc">Server time at which the filters passed, kind UTC.</param>
public sealed record PlannedOrder(
    Signal Signal,
    string Symbol,
    Side Side,
    double Entry,
    double StopLoss,
    double TakeProfit,
    double VolumeUnits,
    double RiskMoney,
    double StopPips,
    DateTime ExpiryUtc,
    int PriceDigits,
    string Label,
    string Comment,
    DateTime PlannedAtUtc)
{
    /// <summary>
    /// Tick size of the symbol from the broker (<see cref="SymbolInfo.TickSize"/>, EXE-017). The
    /// read-back of EXE-010 accepts a stop and target within half of it. Default 0, which accepts
    /// no difference at all.
    /// </summary>
    public double TickSize { get; init; }

    /// <summary>
    /// How the order is placed (EXE-018): <see cref="EntryOrderType.Stop"/> for a breakout signal,
    /// <see cref="EntryOrderType.Limit"/> otherwise. Default Limit.
    /// </summary>
    public EntryOrderType EntryType { get; init; }

    /// <summary>The limit order request that places this order (EXE-008).</summary>
    /// <returns>A limit order request with stop loss, take profit, expiry, label and comment.</returns>
    public LimitOrderRequest ToRequest()
    {
        return new LimitOrderRequest(Symbol, Side, VolumeUnits, Entry, StopLoss, TakeProfit, ExpiryUtc, Label, Comment);
    }

    /// <summary>The stop order request that places this order (EXE-018), with the same values as <see cref="ToRequest"/>.</summary>
    /// <returns>A stop order request with stop loss, take profit, expiry, label and comment.</returns>
    public StopOrderRequest ToStopRequest()
    {
        return new StopOrderRequest(Symbol, Side, VolumeUnits, Entry, StopLoss, TakeProfit, ExpiryUtc, Label, Comment);
    }
}

/// <summary>
/// What <see cref="PreTradeFilters.Evaluate"/> needs besides the broker (EXE-002, EXE-006).
/// </summary>
/// <param name="Symbol">Symbol of this instance, for example EURUSD.</param>
/// <param name="State">Current bot state (EXE-002).</param>
/// <param name="Zones">Non-terminal zones of the signal engine, for the room check (EXE-006).</param>
/// <param name="UnresolvedPlacement">True when <see cref="OrderManager"/> holds an unresolved placement on the symbol (EXE-002, EXE-009).</param>
public sealed record PreTradeContext(string Symbol, BotState State, IReadOnlyList<Zone> Zones, bool UnresolvedPlacement);

/// <summary>Result of <see cref="PreTradeFilters.Evaluate"/>: an order to place, or a skip with one reason (EXE-014).</summary>
public sealed class PreTradeResult
{
    private PreTradeResult(PlannedOrder? order, SkipRecord? skip)
    {
        Order = order;
        Skip = skip;
    }

    /// <summary>True when the signal may be placed as <see cref="Order"/>.</summary>
    public bool IsAllowed => Order != null;

    /// <summary>The order to place; null when skipped.</summary>
    public PlannedOrder? Order { get; }

    /// <summary>The skip with its one reason and measured values; null when allowed (EXE-014).</summary>
    public SkipRecord? Skip { get; }

    internal static PreTradeResult Allowed(PlannedOrder order)
    {
        return new PreTradeResult(order, null);
    }

    internal static PreTradeResult Skipped(SkipRecord skip)
    {
        return new PreTradeResult(null, skip);
    }
}
