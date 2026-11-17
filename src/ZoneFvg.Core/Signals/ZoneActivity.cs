using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.Signals;

/// <summary>Why a zone left the signal engine (OPS-009, journal only).</summary>
public enum ZoneEndReason
{
    /// <summary>A gap confirmed the zone and a signal was emitted (SIG-017), whatever then happened to it.</summary>
    Signalled,

    /// <summary>No valid gap within ConfirmWindowBars after the touch (SIG-014).</summary>
    NoFvg,

    /// <summary>A valid gap appeared, but the bias did not allow the zone's side (SIG-018).</summary>
    BiasMismatch,

    /// <summary>An M15 close beyond the distal line (SIG-014).</summary>
    Invalid,

    /// <summary>MaxZoneAgeBars H4 bars after the activation (SIG-014).</summary>
    Expired,

    /// <summary>Replaced by a newer overlapping zone of the same type while still Active (SIG-012).</summary>
    Superseded,
}

/// <summary>One zone that left the engine and why (OPS-009).</summary>
/// <param name="ZoneId">Id of the zone.</param>
/// <param name="Reason">Why it ended.</param>
public sealed record ZoneEnd(string ZoneId, ZoneEndReason Reason);

/// <summary>
/// What one <see cref="SignalEngine.OnBarClosed"/> call did to the zones (OPS-009): how many
/// zones were created and which zones ended, with their reasons, in the order they ended. Journal
/// only; no decision reads it.
/// </summary>
public sealed class ZoneActivity
{
    /// <summary>No zone created and none ended (OPS-009).</summary>
    public static readonly ZoneActivity None = new ZoneActivity(0, Array.Empty<ZoneEnd>());

    /// <summary>Creates the report (OPS-009).</summary>
    /// <param name="created">Zones created by the bar.</param>
    /// <param name="ended">Zones that ended at the bar, in the order they ended.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ended"/> is null.</exception>
    public ZoneActivity(int created, IReadOnlyList<ZoneEnd> ended)
    {
        ArgumentNullException.ThrowIfNull(ended);
        Created = created;
        Ended = ended;
    }

    /// <summary>Zones created by the bar (OPS-009).</summary>
    public int Created { get; }

    /// <summary>Zones that ended at the bar, with their reasons, in the order they ended (OPS-009).</summary>
    public IReadOnlyList<ZoneEnd> Ended { get; }
}
