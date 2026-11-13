using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// The price levels of a trade that is to be sized (RSK-001, RSK-002). A data record; the
/// <see cref="PositionSizer"/> checks the values.
/// </summary>
/// <param name="Symbol">Symbol name; must equal the name of the symbol data used for sizing.</param>
/// <param name="Side">Direction of the trade.</param>
/// <param name="Entry">Entry (limit) price.</param>
/// <param name="StopLoss">Stop-loss price; on the loss side of entry.</param>
/// <param name="TakeProfit">Take-profit price; on the profit side of entry.</param>
public sealed record TradePlan(string Symbol, Side Side, double Entry, double StopLoss, double TakeProfit)
{
    /// <summary>Symbol name; must equal the name of the symbol data used for sizing.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Direction of the trade.</summary>
    public Side Side { get; init; } = Side;

    /// <summary>Entry (limit) price.</summary>
    public double Entry { get; init; } = Entry;

    /// <summary>Stop-loss price; on the loss side of entry (RSK-002).</summary>
    public double StopLoss { get; init; } = StopLoss;

    /// <summary>Take-profit price; on the profit side of entry (RSK-002).</summary>
    public double TakeProfit { get; init; } = TakeProfit;
}
