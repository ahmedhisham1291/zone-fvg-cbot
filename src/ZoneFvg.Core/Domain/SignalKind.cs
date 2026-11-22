namespace ZoneFvg.Core.Domain;

/// <summary>
/// The entry mode that produced a signal (spec section 3.7, SIG-026). Contract of
/// docs/architecture.md section 3.
/// </summary>
public enum SignalKind
{
    /// <summary>A zone and fair value gap signal (SIG-017); placed as a limit order. The value of an unset kind.</summary>
    ZoneFvg,

    /// <summary>A breakout of the reference day's range (SIG-022 to SIG-026); placed as a stop order (EXE-018).</summary>
    Breakout,
}
