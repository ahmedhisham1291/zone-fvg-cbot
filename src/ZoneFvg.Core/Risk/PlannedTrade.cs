using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// A sized trade that is checked against the account limits by
/// <see cref="RiskEngine.CheckNewTrade"/> (RSK-003 to RSK-006, RSK-010). A data record; the
/// engine checks the values.
/// </summary>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Side">Direction of the trade.</param>
/// <param name="Entry">Entry (limit) price.</param>
/// <param name="StopLoss">Stop-loss price; must be on the loss side of entry.</param>
/// <param name="VolumeUnits">Volume in units, as sized by <see cref="PositionSizer"/>.</param>
/// <param name="EstimatedMargin">Margin the trade would use, in the account currency, as estimated by the broker port (RSK-003).</param>
public sealed record PlannedTrade(string Symbol, Side Side, double Entry, double StopLoss, double VolumeUnits, double EstimatedMargin)
{
    /// <summary>Symbol name.</summary>
    public string Symbol { get; init; } = Symbol;

    /// <summary>Direction of the trade.</summary>
    public Side Side { get; init; } = Side;

    /// <summary>Entry (limit) price.</summary>
    public double Entry { get; init; } = Entry;

    /// <summary>Stop-loss price; must be on the loss side of entry.</summary>
    public double StopLoss { get; init; } = StopLoss;

    /// <summary>Volume in units, as sized by <see cref="PositionSizer"/>.</summary>
    public double VolumeUnits { get; init; } = VolumeUnits;

    /// <summary>Margin the trade would use, in the account currency, as estimated by the broker port (RSK-003).</summary>
    public double EstimatedMargin { get; init; } = EstimatedMargin;
}
