using System;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// A pending order as the broker port reports it (docs/architecture.md section 3). A data
/// record without behaviour; the risk module checks the values before it uses them (RSK-011).
/// </summary>
/// <param name="Id">Broker order id.</param>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Side">Direction of the order.</param>
/// <param name="VolumeUnits">Volume in units.</param>
/// <param name="EntryPrice">Limit (entry) price.</param>
/// <param name="StopLoss">Stop-loss price, or null when the order has no stop loss.</param>
/// <param name="TakeProfit">Take-profit price, or null when the order has no take profit.</param>
/// <param name="Label">Order label; a bot order's label starts with <c>ZFVG|</c>.</param>
/// <param name="Comment">Order comment.</param>
/// <param name="CreatedTimeUtc">Time the order was created, in UTC (RSK-010).</param>
public sealed record PendingOrderInfo(
    long Id,
    string Symbol,
    Side Side,
    double VolumeUnits,
    double EntryPrice,
    double? StopLoss,
    double? TakeProfit,
    string Label,
    string Comment,
    DateTime CreatedTimeUtc)
{
    /// <summary>Broker order id.</summary>
    public long Id { get; init; } = Id;

    /// <summary>Symbol name.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Direction of the order.</summary>
    public Side Side { get; init; } = Side;

    /// <summary>Volume in units.</summary>
    public double VolumeUnits { get; init; } = VolumeUnits;

    /// <summary>Limit (entry) price.</summary>
    public double EntryPrice { get; init; } = EntryPrice;

    /// <summary>Stop-loss price, or null when the order has no stop loss.</summary>
    public double? StopLoss { get; init; } = StopLoss;

    /// <summary>Take-profit price, or null when the order has no take profit.</summary>
    public double? TakeProfit { get; init; } = TakeProfit;

    /// <summary>Order label; a bot order's label starts with <c>ZFVG|</c>.</summary>
    public string Label { get; init; } = Label;

    /// <summary>Order comment.</summary>
    public string Comment { get; init; } = Comment;

    /// <summary>Time the order was created, in UTC (RSK-010).</summary>
    public DateTime CreatedTimeUtc { get; init; } = CreatedTimeUtc;

    /// <summary>
    /// Time at which the broker cancels the order if unfilled, in UTC, or null when the order has
    /// no expiry. Read back after placement and compared with the request (EXE-010). Not a
    /// constructor parameter, so records built without it keep compiling; null means "no expiry".
    /// </summary>
    public DateTime? ExpiryUtc { get; init; }
}
