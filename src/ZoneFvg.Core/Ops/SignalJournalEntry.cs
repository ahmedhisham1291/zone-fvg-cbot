using System;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// The fields of spec section 11 for one signal, taken or skipped (OPS-001): time, symbol, bias,
/// zone id and edges, FVG edges, entry, stop, target, stop pips, volume, spread, cost-to-risk,
/// news status and skip reason. A data record; a null value is journaled as <c>NA</c>. For a
/// breakout signal (OPS-010) the zone id is the breakout id, the zone and gap edges are null, and
/// <see cref="Kind"/> and <see cref="Level"/> are set; the same for a session range signal (SIG-034).
/// </summary>
/// <param name="TimeUtc">Server time of the decision, kind UTC.</param>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Bias">D1 bias at the decision.</param>
/// <param name="ZoneId">Id of the zone of the signal.</param>
/// <param name="ZoneProximal">Proximal edge of the zone, or null when the zone is not known; null for a breakout signal (OPS-010).</param>
/// <param name="ZoneDistal">Distal edge of the zone, or null when the zone is not known; null for a breakout signal (OPS-010).</param>
/// <param name="FvgUpper">Upper edge of the gap; null for a breakout signal (OPS-010).</param>
/// <param name="FvgLower">Lower edge of the gap; null for a breakout signal (OPS-010).</param>
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
    double? FvgUpper,
    double? FvgLower,
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
    /// <summary>The entry mode of the signal (OPS-010). Default ZoneFvg.</summary>
    public SignalKind Kind { get; init; }

    /// <summary>The broken level of a breakout signal (OPS-010), the traded range edge of a session range signal (SIG-034), the broken swing level of a Choch signal (OPS-011); null for a ZoneFvg signal.</summary>
    public double? Level { get; init; }

    /// <summary>Choch signal (OPS-011): whether a bar swept the most recent M15 swing before the change of character; null for the other kinds.</summary>
    public bool? Swept { get; init; }

    /// <summary>Choch signal (OPS-011): the source of the target, <c>zone:&lt;id&gt;</c>, <c>swing:&lt;time&gt;</c> or <c>default</c>; null for the other kinds.</summary>
    public string? TargetSource { get; init; }

    /// <summary>
    /// Choch signal (OPS-011, EXE-028): the reward, target distance over stop distance on the
    /// signal's own levels (signed); null for the other kinds and when the stop distance is zero.
    /// </summary>
    public double? RewardRisk { get; init; }

    /// <summary>
    /// The drawdown brake's note when the brake set the risk of the taken order (RSK-019); null
    /// otherwise, and the line is then unchanged.
    /// </summary>
    public DrawdownBrakeNote? Brake { get; init; }

    /// <summary>
    /// The entry of a signal that became an order (OPS-001). Levels, stop pips, volume and time
    /// come from the order; spread and cost-to-risk from the symbol data as in EXE-004.
    /// </summary>
    /// <param name="order">The planned order that passed every filter.</param>
    /// <param name="bias">D1 bias at the decision.</param>
    /// <param name="zone">The zone of the signal, or null when not known; not read for a breakout signal, whose zone edges are null (OPS-010).</param>
    /// <param name="symbol">Symbol data at the decision, or null when not known.</param>
    /// <param name="news">Calendar status, or null when not known.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="order"/> is null.</exception>
    public static SignalJournalEntry Taken(PlannedOrder order, Bias bias, Zone? zone, SymbolInfo? symbol, NewsStatus? news)
    {
        ArgumentNullException.ThrowIfNull(order);
        double? spread = SpreadPipsOf(symbol);
        Zone? edges = ZoneOf(order.Signal, zone);
        return new SignalJournalEntry(
            order.PlannedAtUtc,
            order.Symbol,
            bias,
            order.Signal.ZoneId,
            edges?.Proximal,
            edges?.Distal,
            order.Signal.Fvg?.Upper,
            order.Signal.Fvg?.Lower,
            order.Entry,
            order.StopLoss,
            order.TakeProfit,
            order.StopPips,
            order.VolumeUnits,
            spread,
            CostToRiskOf(symbol, spread, order.StopPips),
            news,
            null)
        {
            Kind = order.Signal.Kind,
            Level = order.Signal.Level,
            Swept = order.Signal.Swept,
            TargetSource = order.Signal.TargetSource,
            RewardRisk = RewardRiskOf(order.Signal),
            Brake = order.Brake,
        };
    }

    /// <summary>
    /// The entry of a skipped signal (OPS-001). Levels come from the signal, time and reason from
    /// the skip record; stop pips, spread and cost-to-risk from the symbol data as in EXE-004.
    /// </summary>
    /// <param name="signal">The signal.</param>
    /// <param name="symbolName">Symbol name.</param>
    /// <param name="bias">D1 bias at the decision.</param>
    /// <param name="zone">The zone of the signal, or null when not known; not read for a breakout signal, whose zone edges are null (OPS-010).</param>
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
        Zone? edges = ZoneOf(signal, zone);
        return new SignalJournalEntry(
            skip.AtUtc,
            symbolName,
            bias,
            signal.ZoneId,
            edges?.Proximal,
            edges?.Distal,
            signal.Fvg?.Upper,
            signal.Fvg?.Lower,
            signal.Entry,
            signal.Stop,
            signal.Target,
            stopPips,
            null,
            spread,
            CostToRiskOf(symbol, spread, stopPips),
            news,
            skip.Reason)
        {
            Kind = signal.Kind,
            Level = signal.Level,
            Swept = signal.Swept,
            TargetSource = signal.TargetSource,
            RewardRisk = RewardRiskOf(signal),
        };
    }

    // OPS-010: a breakout signal has no zone, so its zone edges are NA whatever zone the caller passes; SIG-034: nor has a session range signal.
    private static Zone? ZoneOf(Signal signal, Zone? zone)
    {
        return signal.Kind == SignalKind.Breakout || signal.Kind == SignalKind.SessionRange ? null : zone;
    }

    // OPS-011: the reward of EXE-028 on the signal's own levels, for a Choch signal only.
    private static double? RewardRiskOf(Signal signal)
    {
        double risk = Math.Abs(signal.Entry - signal.Stop);
        if (signal.Kind != SignalKind.Choch || !(risk > 0.0))
        {
            return null;
        }

        double reward = signal.Side == Side.Buy ? signal.Target - signal.Entry : signal.Entry - signal.Target;
        return reward / risk;
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
