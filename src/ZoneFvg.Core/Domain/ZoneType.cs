namespace ZoneFvg.Core.Domain;

/// <summary>Kind of zone (spec section 3.2). Contract of docs/architecture.md section 3.</summary>
public enum ZoneType
{
    /// <summary>Demand zone: price is expected to rise from it.</summary>
    Demand,

    /// <summary>Supply zone: price is expected to fall from it.</summary>
    Supply,
}
