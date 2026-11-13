using System;

namespace ZoneFvg.Core.Domain;

/// <summary>
/// Snapshot of a supply or demand zone (SIG-010, SIG-013, SIG-014). Contract of
/// docs/architecture.md section 3. A snapshot never changes; the zone book replaces it with a
/// new snapshot when the zone's state or touch data change.
/// </summary>
/// <param name="Id">Zone id, "D-yyyyMMdd-HHmm" or "S-yyyyMMdd-HHmm" from the UTC open time of the first base bar (SIG-013).</param>
/// <param name="Type">Demand or supply.</param>
/// <param name="Proximal">Near edge: the highest base body top of a demand zone, the lowest base body bottom of a supply zone (SIG-010).</param>
/// <param name="Distal">Far edge: the lowest low of a demand zone, the highest high of a supply zone (SIG-010).</param>
/// <param name="ActivationTimeUtc">UTC time from which M15 bars are evaluated against the zone (SIG-013, SIG-014).</param>
/// <param name="AtrAtCreation">ATR(H4) used to detect the zone: the value as of the bar immediately before the impulse leg (SIG-007, SIG-011).</param>
/// <param name="State">Signal-layer state (SIG-014).</param>
/// <param name="TouchBarOpenUtc">UTC open time of the M15 bar that touched the zone; null until touched (SIG-014).</param>
/// <param name="ExtremeSinceTouch">
/// Lowest low (demand) or highest high (supply) of the M15 bars since the touch bar, the touch
/// bar included; null until touched (SIG-014).
/// </param>
public sealed record Zone(
    string Id,
    ZoneType Type,
    double Proximal,
    double Distal,
    DateTime ActivationTimeUtc,
    double AtrAtCreation,
    ZoneState State,
    DateTime? TouchBarOpenUtc,
    double? ExtremeSinceTouch);
