namespace ZoneFvg.Core.Risk;

/// <summary>
/// How the risk of a new trade is set (RSK-016, RSK-018, spec 4.1, owner decision Q of 2026-11-24).
/// The values are a contract: Ladder is 0, the default.
/// </summary>
public enum SizingPlan
{
    /// <summary>The ladder of RSK-013, or the fixed RiskPercent of RSK-001 with LadderEnabled false: the behaviour before spec v0.12.</summary>
    Ladder = 0,

    /// <summary>The Recovery plan of spec 4.1.1 (RSK-016, RSK-017): a money amount from the losing run of the instance's symbol.</summary>
    Recovery = 1,
}
