using System;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// The fields of spec section 11 for one signal, taken or skipped (OPS-001): time, symbol, bias,
/// zone id and edges, FVG edges, entry, stop, target, stop pips, volume, spread, cost-to-risk,
/// news status and skip reason. A data record; a null value is journaled as <c>NA</c>.
/// </summary>
/// <param name="TimeUtc">Server time of the decision, kind UTC.</param>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Bias">D1 bias at the decision.</param>
/// <param name="ZoneId">Id of the zone of the signal.</param>
/// <param name="ZoneProximal">Proximal edge of the zone, or null when the zone is not known.</param>
/// <param name="ZoneDistal">Distal edge of the zone, or null when the zone is not known.</param>
/// <param name="FvgUpper">Upper edge of the gap.</param>
/// <param name="FvgLower">Lower edge of the gap.</param>
/// <param name="Entry">Entry price: the order's for a taken signal, the signal's for a skipped one.</param>
/// <param name="Stop">Stop price: the order's for a taken signal (spread included for a sell, EXE-007), the signal's for a skipped one.</param>
/// <param name="Target">Target price, as for <paramref name="Entry"/>.</param>
/// <param name="StopPips">Stop distance in pips, or null when the pip size is not known.</param>
/// <param name="VolumeUnits">Volume in units for a taken signal; null for a skipped one.</param>
/// <param name="SpreadPips">Spread in pips at the decision, or null when not known.</param>
/// <param name="CostToRisk">Spread plus round-trip commission in pips divided by the stop pips (EXE-004), or null when not known.</param>
/// <param name="News">Calendar status at the decision, or null when not known.</param>
/// <param name="Skip">The skip reason, or null for a taken signal.</param>
public sealed record SignalJournalEntry(
    DateTime TimeUtc,
    string Symbol,
    Bias Bias,
    string ZoneId,
    double? ZoneProximal,
    double? ZoneDistal,
    double FvgUpper,
    double FvgLower,
    double Entry,
    double Stop,
    double Target,
    double? StopPips,
    double? VolumeUnits,
    double? SpreadPips,
    double? CostToRisk,
    NewsStatus? News,
    SkipReason? Skip)
{
    /// <summary>
    /// The entry of a signal that became an order (OPS-001). Levels, stop pips, volume and time
    /// come from the order; spread and cost-to-risk from the symbol data as in EXE-004.
    /// </summary>
    /// <param name="order">The planned order that passed every filter.</param>
    /// <param name="bias">D1 bias at the decision.</param>
    /// <param name="zone">The zone of the signal, or null when not known.</param>
    /// <param name="symbol">Symbol data at the decision, or null when not known.</param>
    /// <param name="news">Calendar status, or null when not known.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> is null.</exception>
    public static SignalJournalEntry Taken(PlannedOrder order, Bias bias, Zone? zone, SymbolInfo? symbol, NewsStatus? news)
    {
        ArgumentNullException.ThrowIfNull(order);
        double? spread = SpreadPipsOf(symbol);
        return new SignalJournalEntry(
            order.PlannedAtUtc,
            order.Symbol,
            bias,
            order.Signal.ZoneId,
            zone?.Proximal,
            zone?.Distal,
            order.Signal.Fvg.Upper,
            order.Signal.Fvg.Lower,
            order.Entry,
            order.StopLoss,
            order.TakeProfit,
            order.StopPips,
            order.VolumeUnits,
            spread,
            CostToRiskOf(symbol, spread, order.StopPips),
            news,
            null);
    }

    /// <summary>
    /// The entry of a skipped signal (OPS-001). Levels come from the signal, time and reason from
    /// the skip record; stop pips, spread and cost-to-risk from the symbol data as in EXE-004.
    /// </summary>
    /// <param name="signal">The signal.</param>
    /// <param name="symbolName">Symbol name.</param>
    /// <param name="bias">D1 bias at the decision.</param>
    /// <param name="zone">The zone of the signal, or null when not known.</param>
    /// <param name="skip">The skip record (EXE-014).</param>
    /// <param name="symbol">Symbol data at the decision, or null when not known.</param>
    /// <param name="news">Calendar status, or null when not known.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="signal"/>, <paramref name="symbolName"/> or <paramref name="skip"/> is null.</exception>
    public static SignalJournalEntry Skipped(Signal signal, string symbolName, Bias bias, Zone? zone, SkipRecord skip, SymbolInfo? symbol, NewsStatus? news)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(symbolName);
        ArgumentNullException.ThrowIfNull(skip);
        double? stopPips = null;
        if (symbol != null && double.IsFinite(symbol.PipSize) && symbol.PipSize > 0.0)
        {
            stopPips = Math.Abs(signal.Entry - signal.Stop) / symbol.PipSize;
        }

        double? spread = SpreadPipsOf(symbol);
        return new SignalJournalEntry(
            skip.AtUtc,
            symbolName,
            bias,
            signal.ZoneId,
            zone?.Proximal,
            zone?.Distal,
            signal.Fvg.Upper,
            signal.Fvg.Lower,
            signal.Entry,
            signal.Stop,
            signal.Target,
            stopPips,
            null,
            spread,
            CostToRiskOf(symbol, spread, stopPips),
            news,
            skip.Reason);
    }

    private static double? SpreadPipsOf(SymbolInfo? symbol)
    {
        if (symbol == null || !double.IsFinite(symbol.PipSize) || symbol.PipSize <= 0.0 || !double.IsFinite(symbol.Spread))
        {
            return null;
        }

        return symbol.Spread / symbol.PipSize;
    }

    private static double? CostToRiskOf(SymbolInfo? symbol, double? spreadPips, double? stopPips)
    {
        if (symbol == null || !spreadPips.HasValue || !stopPips.HasValue || !(stopPips.Value > 0.0)
            || !double.IsFinite(symbol.PipValuePerUnit) || symbol.PipValuePerUnit <= 0.0 || !double.IsFinite(symbol.RoundTripCommissionPerUnit))
        {
            return null;
        }

        double commissionPips = symbol.RoundTripCommissionPerUnit / symbol.PipValuePerUnit;
        return (spreadPips.Value + commissionPips) / stopPips.Value;
    }
}
