using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// The ladder sizing of one entry (RSK-013): its position after the last win, the step of its
/// deal, the raw and the capped risk percent, and the four step columns C, D, E, F when it is
/// placed. Immutable.
/// </summary>
public sealed class LadderDecision
{
    /// <summary>Creates a decision (RSK-013).</summary>
    /// <param name="riskPercent">The raw percent capped at MaxRiskPercentPerTrade.</param>
    /// <param name="rawPercent">The uncapped sum of the parts that fall on the entry.</param>
    /// <param name="position">Position p of the entry after the last win, 1 or more.</param>
    /// <param name="step">Step of the entry's deal: the column of its position, or 0 from position 5.</param>
    /// <param name="columns">The columns C, D, E, F when the entry is placed; copied.</param>
    /// <param name="stepsFrozen">True when no win can raise a step until a new RiskEpochDate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="columns"/> is null.</exception>
    public LadderDecision(double riskPercent, double rawPercent, int position, int step, IReadOnlyList<int> columns, bool stepsFrozen)
    {
        ArgumentNullException.ThrowIfNull(columns);
        RiskPercent = riskPercent;
        RawPercent = rawPercent;
        Position = position;
        Step = step;
        Columns = new List<int>(columns).AsReadOnly();
        StepsFrozen = stepsFrozen;
    }

    /// <summary>The risk percent of the entry: the raw percent capped at MaxRiskPercentPerTrade (RSK-013).</summary>
    public double RiskPercent { get; }

    /// <summary>The uncapped sum of the parts of the deals that fall on the entry and have not ended (RSK-013).</summary>
    public double RawPercent { get; }

    /// <summary>Position p of the entry: 1 plus the entries placed after the latest win (RSK-013 clause 1).</summary>
    public int Position { get; }

    /// <summary>Step of the entry's deal: the column of its position, or 0 from position 5 (RSK-013 clause 2).</summary>
    public int Step { get; }

    /// <summary>The step columns C, D, E, F when the entry is placed (RSK-013 clause 2).</summary>
    public IReadOnlyList<int> Columns { get; }

    /// <summary>True when an entry since RiskEpochDate was above MaxRiskPercentPerTrade, so wins raise no step until a new RiskEpochDate (RSK-013 clause 5).</summary>
    public bool StepsFrozen { get; }
}
