namespace ZoneFvg.Core.Risk;

/// <summary>
/// How the FTMO emergency guard places its overall floor (RSK-021, spec v0.20 rows BH and BI,
/// section 15.8). The values are a contract: Static is 0, the core default.
/// </summary>
public enum FtmoMaxLossMode
{
    /// <summary>The 2-Step rule of RSK-009: the floor is fixed from InitialCapital.</summary>
    Static = 0,

    /// <summary>
    /// FTMO Instant: the floor trails P, the larger of InitialCapital and the highest end-of-trade
    /// balance since RiskEpochDate (the kill switch's peak).
    /// </summary>
    TrailingBalance = 1,
}
