namespace ZoneFvg.Core.Domain;

/// <summary>
/// The entry mode that produced a signal (spec sections 3.7 to 3.10, SIG-026, SIG-034, SIG-041, SIG-043). Contract of
/// docs/architecture.md section 3.
/// </summary>
public enum SignalKind
{
    /// <summary>A zone and fair value gap signal (SIG-017); placed as a limit order. The value of an unset kind.</summary>
    ZoneFvg,

    /// <summary>A breakout of the reference day's range (SIG-022 to SIG-026); placed as a stop order (EXE-018).</summary>
    Breakout,

    /// <summary>A break of the session range (SIG-031 to SIG-034, spec 3.8); placed as a stop order with the signal's expiry (EXE-024).</summary>
    SessionRange,

    /// <summary>A change of character at a touched zone (SIG-035 to SIG-041, spec 3.9); placed as a limit order at the broken swing level.</summary>
    Choch,

    /// <summary>A buy after the daily reopening of gold (SIG-043, spec 3.10); placed as a buy stop with the signal's expiry and closed by the timed exit (EXE-033).</summary>
    Overnight,
}
