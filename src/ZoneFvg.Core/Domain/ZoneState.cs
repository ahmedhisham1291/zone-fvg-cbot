namespace ZoneFvg.Core.Domain;

/// <summary>Signal-layer state of a zone (spec section 3.3). Contract of docs/architecture.md section 3.</summary>
public enum ZoneState
{
    /// <summary>Fresh, never touched.</summary>
    Active,

    /// <summary>Price has reached the zone and the confirmation window is open.</summary>
    Touched,

    /// <summary>A signal was produced. The zone moves on to <see cref="Used"/> at once.</summary>
    Signalled,

    /// <summary>No further signals can come from the zone. Terminal.</summary>
    Used,

    /// <summary>Broken: a bar closed beyond the distal line. Terminal.</summary>
    Invalid,

    /// <summary>Older than the maximum zone age. Terminal.</summary>
    Expired,
}
