using System;

namespace ZoneFvg.Core.Domain;

/// <summary>
/// A trade signal of the signal layer (SIG-017, SIG-018, SIG-026, SIG-034). Contract of
/// docs/architecture.md section 3. A signal says nothing about whether an order is placed: the
/// execution layer decides that.
/// </summary>
/// <param name="ZoneId">Id of the zone that produced the signal (SIG-013, SIG-017); for a breakout signal the breakout id (SIG-025, SIG-026); for a session range signal the setup id RH- or RL- (SIG-032, SIG-034).</param>
/// <param name="Side">Buy from a demand zone, Sell from a supply zone (SIG-017, SIG-018).</param>
/// <param name="Entry">Limit entry price inside the gap, at EntryLevelPct of the gap height from its far edge (SIG-017); for a breakout signal the stop-entry level (SIG-024).</param>
/// <param name="Stop">Stop price beyond the zone and the extreme since the touch, plus the ATR(M15) buffer (SIG-017).</param>
/// <param name="Target">Target price: RewardRisk times the stop distance from the entry (SIG-017).</param>
/// <param name="Fvg">The fair value gap that confirmed the zone, with the open times of its three bars (SIG-016, SIG-017); null for a breakout signal (SIG-026).</param>
/// <param name="ConfirmationCloseUtc">UTC close time of c1, the bar that confirmed the gap or the breakout: its open time plus 15 minutes (SIG-004, SIG-017, SIG-024).</param>
/// <param name="FromReplay">True when the signal was found while replaying history rather than on a live bar (spec 8.3).</param>
/// <param name="AtrH4">
/// The ATR(H4) the engine used for this signal: the latest value at the confirmation close
/// (architecture section 10). The execution layer bounds the stop distance with it (EXE-003), so
/// replay and live use the same number.
/// </param>
public sealed record Signal(
    string ZoneId,
    Side Side,
    double Entry,
    double Stop,
    double Target,
    Fvg? Fvg,
    DateTime ConfirmationCloseUtc,
    bool FromReplay,
    double AtrH4)
{
    /// <summary>
    /// The entry mode that produced the signal (SIG-026). Default <see cref="SignalKind.ZoneFvg"/>,
    /// the kind of every signal of <c>FvgDetector</c>.
    /// </summary>
    public SignalKind Kind { get; init; }

    /// <summary>
    /// For a breakout signal the reference high (buy) or low (sell) that was broken (SIG-024,
    /// SIG-026); for a session range signal the range edge that is traded, which is its entry
    /// (SIG-034); null for a ZoneFvg signal.
    /// </summary>
    public double? Level { get; init; }

    /// <summary>
    /// For a session range signal the UTC expiry of its order, RangeLastEntryNy of the trading
    /// day or the Friday cap (SIG-032, SIG-034); the execution layer shortens it for news
    /// (EXE-024). Null for the other kinds, whose expiry the execution layer sets (EXE-015).
    /// </summary>
    public DateTime? ExpiryUtc { get; init; }
}
