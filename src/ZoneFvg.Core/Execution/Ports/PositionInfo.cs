using System;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// An open position as the broker port reports it (docs/architecture.md section 3). A data
/// record without behaviour; the risk module checks the values before it uses them (RSK-011).
/// </summary>
/// <param name="Id">Broker position id.</param>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Side">Direction of the position.</param>
/// <param name="VolumeUnits">Volume in units.</param>
/// <param name="EntryPrice">Entry (fill) price.</param>
/// <param name="StopLoss">Stop-loss price, or null when the position has no stop loss.</param>
/// <param name="TakeProfit">Take-profit price, or null when the position has no take profit.</param>
/// <param name="Label">Position label; a bot position's label starts with <c>ZFVG|</c>.</param>
/// <param name="Comment">Position comment.</param>
/// <param name="OpenTimeUtc">Time the position was opened, in UTC (RSK-010).</param>
public sealed record PositionInfo(
    long Id,
    string Symbol,
    Side Side,
    double VolumeUnits,
    double EntryPrice,
    double? StopLoss,
    double? TakeProfit,
    string Label,
    string Comment,
    DateTime OpenTimeUtc)
{
    /// <summary>Broker position id.</summary>
    public long Id { get; } = Id;

    /// <summary>Symbol name.</summary>
    public string Symbol { get; } = Symbol;

    /// <summary>Direction of the position.</summary>
    public Side Side { get; } = Side;

    /// <summary>Volume in units.</summary>
    public double VolumeUnits { get; } = VolumeUnits;

    /// <summary>Entry (fill) price.</summary>
    public double EntryPrice { get; } = EntryPrice;

    /// <summary>Stop-loss price, or null when the position has no stop loss.</summary>
    public double? StopLoss { get; } = StopLoss;

    /// <summary>Take-profit price, or null when the position has no take profit.</summary>
    public double? TakeProfit { get; } = TakeProfit;

    /// <summary>Position label; a bot position's label starts with <c>ZFVG|</c>.</summary>
    public string Label { get; } = Label;

    /// <summary>Position comment.</summary>
    public string Comment { get; } = Comment;

    /// <summary>Time the position was opened, in UTC (RSK-010).</summary>
    public DateTime OpenTimeUtc { get; } = OpenTimeUtc;
}
