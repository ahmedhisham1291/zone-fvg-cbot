using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// The broker port (docs/architecture.md section 3). The adapter implements it with cTrader; tests
/// use a fake. It is the only source of truth for orders, positions and history: the execution
/// layer reads them here at every event and never keeps them as a record of its own (spec 8.1).
/// </summary>
/// <remarks>
/// Every request (<see cref="PlaceLimitOrder"/>, <see cref="CancelOrder"/>,
/// <see cref="ModifyPositionProtection"/>, <see cref="ClosePosition"/>) is synchronous and returns
/// an <see cref="OrderResult"/>. A timeout, a disconnect or a technical error is reported as
/// <see cref="OrderOutcome.Unclear"/>, never thrown (EXE-009).
/// </remarks>
public interface IBroker
{
    /// <summary>Server time, kind UTC. "Now" for every decision; the core never reads a clock (REC-012).</summary>
    DateTime ServerTimeUtc { get; }

    /// <summary>Whether the platform reports a connection. Not relied on before trading (spec 8.4).</summary>
    bool IsConnected { get; }

    /// <summary>Balance, equity, free margin and currency of the account (RSK-001, RSK-003).</summary>
    /// <returns>The account values now.</returns>
    AccountSnapshot GetAccount();

    /// <summary>Pip size, pip value per unit, volume minimum, step and maximum, spread and whether the market is open (EXE-002).</summary>
    /// <param name="symbol">Symbol name.</param>
    /// <returns>The symbol data now.</returns>
    SymbolInfo GetSymbol(string symbol);

    /// <summary>All open positions of the account, bot or not (EXE-002, EXE-011).</summary>
    /// <returns>The positions now.</returns>
    IReadOnlyList<PositionInfo> GetPositions();

    /// <summary>All pending orders of the account, bot or not (EXE-002, EXE-009).</summary>
    /// <returns>The pending orders now.</returns>
    IReadOnlyList<PendingOrderInfo> GetPendingOrders();

    /// <summary>Closed trades with the balance after each close, closed at or after <paramref name="sinceUtc"/> (RSK-007).</summary>
    /// <param name="sinceUtc">Earliest close time wanted, kind UTC.</param>
    /// <returns>The closed trades.</returns>
    IReadOnlyList<ClosedTradeInfo> GetHistory(DateTime sinceUtc);

    /// <summary>Margin the trade would use, in the account currency (RSK-003).</summary>
    /// <param name="symbol">Symbol name.</param>
    /// <param name="side">Direction.</param>
    /// <param name="volumeUnits">Volume in units.</param>
    /// <returns>The estimated margin.</returns>
    double EstimateMargin(string symbol, Side side, double volumeUnits);

    /// <summary>Places a limit order with stop loss, take profit and expiry attached (EXE-008). Synchronous.</summary>
    /// <param name="request">The order.</param>
    /// <returns>Success with the order id, a clear rejection with its code, or Unclear.</returns>
    OrderResult PlaceLimitOrder(LimitOrderRequest request);

    /// <summary>Cancels a pending order (EXE-010, EXE-013).</summary>
    /// <param name="orderId">Broker order id.</param>
    /// <returns>The outcome.</returns>
    OrderResult CancelOrder(long orderId);

    /// <summary>Sets the stop loss and take profit of a position (EXE-011). A null value means "none".</summary>
    /// <param name="positionId">Broker position id.</param>
    /// <param name="stopLoss">Stop-loss price, or null for none.</param>
    /// <param name="takeProfit">Take-profit price, or null for none.</param>
    /// <returns>The outcome.</returns>
    OrderResult ModifyPositionProtection(long positionId, double? stopLoss, double? takeProfit);

    /// <summary>Closes a position at market (EXE-011, EXE-013).</summary>
    /// <param name="positionId">Broker position id.</param>
    /// <returns>The outcome.</returns>
    OrderResult ClosePosition(long positionId);
}

/// <summary>Outcome of a broker request (docs/architecture.md section 3, EXE-009).</summary>
public enum OrderOutcome
{
    /// <summary>The broker accepted the request.</summary>
    Success,

    /// <summary>The broker clearly refused the request, for example bad volume, market closed or no money. Never retried.</summary>
    Rejected,

    /// <summary>Timeout, disconnect or technical error: the request may or may not have taken effect. Never resent blindly.</summary>
    Unclear,
}

/// <summary>Result of a broker request (docs/architecture.md section 3).</summary>
/// <param name="Outcome">Success, Rejected or Unclear.</param>
/// <param name="Id">Id of the order or position the request created or changed, when the broker returned one.</param>
/// <param name="ErrorCode">The broker's error code for a rejection or an unclear outcome; null on success.</param>
public sealed record OrderResult(OrderOutcome Outcome, long? Id, string? ErrorCode);

/// <summary>
/// A limit order with its protection, as sent to the broker (EXE-008). A data record; the
/// execution layer builds it from a checked plan.
/// </summary>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Side">Direction.</param>
/// <param name="VolumeUnits">Volume in units.</param>
/// <param name="EntryPrice">Limit (entry) price.</param>
/// <param name="StopLossPrice">Stop-loss price, attached at placement.</param>
/// <param name="TakeProfitPrice">Take-profit price, attached at placement.</param>
/// <param name="ExpiryUtc">Time at which the broker cancels the unfilled order, kind UTC (EXE-015).</param>
/// <param name="Label">Label <c>ZFVG|v1|&lt;symbol&gt;|&lt;zoneId&gt;</c> (EXE-008).</param>
/// <param name="Comment">Comment with the plan and the parameter hash (EXE-008).</param>
public sealed record LimitOrderRequest(
    string Symbol,
    Side Side,
    double VolumeUnits,
    double EntryPrice,
    double StopLossPrice,
    double TakeProfitPrice,
    DateTime ExpiryUtc,
    string Label,
    string Comment);
