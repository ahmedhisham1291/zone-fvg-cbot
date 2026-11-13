using System;

namespace ZoneFvg.Core.Domain;

/// <summary>
/// A trade signal of the signal layer (SIG-017, SIG-018). Contract of docs/architecture.md
/// section 3. A signal says nothing about whether an order is placed: the execution layer decides
/// that.
/// </summary>
/// <param name="ZoneId">Id of the zone that produced the signal (SIG-013, SIG-017).</param>
/// <param name="Side">Buy from a demand zone, Sell from a supply zone (SIG-017, SIG-018).</param>
/// <param name="Entry">Limit entry price inside the gap, at EntryLevelPct of the gap height from its far edge (SIG-017).</param>
/// <param name="Stop">Stop price beyond the zone and the extreme since the touch, plus the ATR(M15) buffer (SIG-017).</param>
/// <param name="Target">Target price: RewardRisk times the stop distance from the entry (SIG-017).</param>
/// <param name="Fvg">The fair value gap that confirmed the zone, with the open times of its three bars (SIG-016, SIG-017).</param>
/// <param name="ConfirmationCloseUtc">UTC close time of c1, the bar that confirmed the gap: its open time plus 15 minutes (SIG-004, SIG-017).</param>
/// <param name="FromReplay">True when the signal was found while replaying history rather than on a live bar (spec 8.3).</param>
public sealed record Signal(
    string ZoneId,
    Side Side,
    double Entry,
    double Stop,
    double Target,
    Fvg Fvg,
    DateTime ConfirmationCloseUtc,
    bool FromReplay);
