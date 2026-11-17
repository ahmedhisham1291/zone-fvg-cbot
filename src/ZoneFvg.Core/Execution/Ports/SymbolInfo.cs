using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// The symbol data read from the broker port (docs/architecture.md section 3). A data record
/// without behaviour; the risk module checks the values before it uses them (RSK-011).
/// </summary>
/// <param name="Name">Symbol name as the broker reports it, for example EURUSD.</param>
/// <param name="BaseCurrency">Base currency code, for example EUR for EURUSD (RSK-005).</param>
/// <param name="QuoteCurrency">Quote currency code, for example USD for EURUSD (RSK-005).</param>
/// <param name="PipSize">Size of one pip in price units, for example 0.0001 for EURUSD.</param>
/// <param name="PipValuePerUnit">Value of one pip for one unit of volume, in the account currency (RSK-001).</param>
/// <param name="VolumeMinUnits">Smallest volume the broker accepts, in units (RSK-001).</param>
/// <param name="VolumeStepUnits">Volume step in units (RSK-001).</param>
/// <param name="VolumeMaxUnits">Largest volume the broker accepts, in units.</param>
/// <param name="Spread">Current spread in price units.</param>
/// <param name="IsMarketOpen">Whether the symbol's market is open.</param>
/// <param name="RoundTripCommissionPerUnit">Commission for opening and closing one unit of volume, in the account currency (RSK-006).</param>
public sealed record SymbolInfo(
    string Name,
    string BaseCurrency,
    string QuoteCurrency,
    double PipSize,
    double PipValuePerUnit,
    double VolumeMinUnits,
    double VolumeStepUnits,
    double VolumeMaxUnits,
    double Spread,
    bool IsMarketOpen,
    double RoundTripCommissionPerUnit)
{
    /// <summary>Symbol name as the broker reports it, for example EURUSD.</summary>
    public string Name { get; init; } = Name;

    /// <summary>Base currency code, for example EUR for EURUSD (RSK-005).</summary>
    public string BaseCurrency { get; init; } = BaseCurrency;

    /// <summary>Quote currency code, for example USD for EURUSD (RSK-005).</summary>
    public string QuoteCurrency { get; init; } = QuoteCurrency;

    /// <summary>Size of one pip in price units, for example 0.0001 for EURUSD.</summary>
    public double PipSize { get; init; } = PipSize;

    /// <summary>Value of one pip for one unit of volume, in the account currency (RSK-001).</summary>
    public double PipValuePerUnit { get; init; } = PipValuePerUnit;

    /// <summary>Smallest volume the broker accepts, in units (RSK-001).</summary>
    public double VolumeMinUnits { get; init; } = VolumeMinUnits;

    /// <summary>Volume step in units (RSK-001).</summary>
    public double VolumeStepUnits { get; init; } = VolumeStepUnits;

    /// <summary>Largest volume the broker accepts, in units.</summary>
    public double VolumeMaxUnits { get; init; } = VolumeMaxUnits;

    /// <summary>Current spread in price units.</summary>
    public double Spread { get; init; } = Spread;

    /// <summary>Whether the symbol's market is open.</summary>
    public bool IsMarketOpen { get; init; } = IsMarketOpen;

    /// <summary>Commission for opening and closing one unit of volume, in the account currency (RSK-006).</summary>
    public double RoundTripCommissionPerUnit { get; init; } = RoundTripCommissionPerUnit;

    /// <summary>
    /// Number of decimals of the symbol's prices, cTrader <c>Symbol.Digits</c> (EXE-017). Used to
    /// write prices in the order comment. Default 0; the adapter sets it.
    /// </summary>
    public int Digits { get; init; }

    /// <summary>
    /// Smallest price step of the symbol, cTrader <c>Symbol.TickSize</c> (EXE-017). Entry, stop and
    /// target are rounded to a whole multiple of it. Default 0, which is not usable, so a symbol
    /// record without it is refused as BrokerDataInvalid.
    /// </summary>
    public double TickSize { get; init; }

    /// <summary>
    /// The symbol's weekly trading sessions, UTC, from cTrader <c>Symbol.MarketHours.Sessions</c>
    /// (REC-016). Time outside them is closed time for the gap and recency check. Default empty:
    /// no session reported, and the check uses the weekend closure and holidays alone.
    /// </summary>
    public IReadOnlyList<TradingSession> Sessions { get; init; } = Array.Empty<TradingSession>();
}
