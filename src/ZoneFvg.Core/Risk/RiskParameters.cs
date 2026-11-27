using System;
using System.Globalization;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// The risk parameters of spec sections 4 and 7 with their documented defaults. Instances are
/// immutable; use a <c>with</c> expression to derive a changed copy.
/// </summary>
/// <remarks>
/// <see cref="RiskEpochDate"/> has no usable default: spec section 7 says it is set manually,
/// so <see cref="Validate"/> rejects the unset value and nothing can run until it is set.
/// </remarks>
public sealed record RiskParameters
{
    /// <summary>
    /// Smallest free margin after a trade, in percent of equity (RSK-003, spec 4.1). Fixed by the
    /// spec; not a parameter.
    /// </summary>
    public const double MinFreeMarginPercentOfEquity = 50.0;

    /// <summary>Risk per trade in percent of the current balance (RSK-001). Default 0.5.</summary>
    public double RiskPercent { get; init; } = 0.5;

    /// <summary>
    /// Drawdown brake of spec 4.1 (RSK-019): distance below the peak balance, in percent of the peak,
    /// from which fixed sizing (SizingPlan Ladder with LadderEnabled false) uses
    /// <see cref="RiskPercent"/> x <see cref="DrawdownBrakeFactor"/>. Default 0 (off). At least 0 and
    /// below <see cref="MaxDrawdownPercent"/>.
    /// </summary>
    public double DrawdownBrakePercent { get; init; }

    /// <summary>
    /// Drawdown brake (RSK-019): the multiplier of <see cref="RiskPercent"/> while braked. Default 0.5.
    /// Greater than 0 and at most 1.
    /// </summary>
    public double DrawdownBrakeFactor { get; init; } = 0.5;

    /// <summary>Total open risk cap in percent of <see cref="InitialCapital"/> (RSK-004). Default 1.5.</summary>
    public double MaxOpenRiskPercent { get; init; } = 1.5;

    /// <summary>Most bot positions and pending orders on the same side of one currency (RSK-005). Default 2.</summary>
    public int MaxPositionsPerCurrency { get; init; } = 2;

    /// <summary>Daily risk budget in percent of <see cref="InitialCapital"/> (RSK-006, RSK-018). Default 2.0 (owner decision R of 2026-11-24; 1.5 before).</summary>
    public double DailyLossLimitPercent { get; init; } = 2.0;

    /// <summary>Drawdown from the peak balance at which the kill switch halts, in percent (RSK-008). Default 8.</summary>
    public double MaxDrawdownPercent { get; init; } = 8.0;

    /// <summary>Initial capital, the base of the FTMO percentages (RSK-004, RSK-006, RSK-009, RSK-018). Default 200,000 (owner decision P of 2026-11-24; 100,000 before).</summary>
    public double InitialCapital { get; init; } = 200000.0;

    /// <summary>FTMO maximum daily loss in percent of <see cref="InitialCapital"/> (RSK-009). Default 5.</summary>
    public double FtmoDailyLossPercent { get; init; } = 5.0;

    /// <summary>FTMO maximum loss in percent of <see cref="InitialCapital"/> (RSK-009). Default 10.</summary>
    public double FtmoMaxLossPercent { get; init; } = 10.0;

    /// <summary>Distance from an FTMO floor, in percent of <see cref="InitialCapital"/>, at which the emergency guard acts (RSK-009). Default 1.</summary>
    public double FtmoGuardBufferPercent { get; init; } = 1.0;

    /// <summary>
    /// Start of drawdown measurement for the kill switch (RSK-008): a date meaning 00:00 Prague time
    /// (CE(S)T) on that day (architecture section 10). Give it as a date with time 00:00:00 and kind
    /// UTC. Set manually; the default (kind Unspecified) is rejected by <see cref="Validate"/>.
    /// </summary>
    public DateTime RiskEpochDate { get; init; }

    /// <summary>Most entries per symbol per CE(S)T day (RSK-010). Default 2.</summary>
    public int MaxEntriesPerSymbolPerDay { get; init; } = 2;

    /// <summary>Most entries per account per CE(S)T day (RSK-010). Default 6.</summary>
    public int MaxEntriesPerDay { get; init; } = 6;

    /// <summary>Most order requests per instance in one hour (RSK-010, spec 4.3). Default 20.</summary>
    public int MaxRequestsPerHour { get; init; } = 20;

    /// <summary>Most order requests per instance per CE(S)T day (RSK-010). Default 100.</summary>
    public int MaxRequestsPerDay { get; init; } = 100;

    /// <summary>Most unhandled errors per instance in one hour (RSK-010, spec 4.3). Default 5.</summary>
    public int MaxErrorsPerHour { get; init; } = 5;

    /// <summary>Largest volume of one trade in units (RSK-002, RSK-018). Default 3,000,000 (30 lots; owner decision X of 2026-11-24, 500,000 before).</summary>
    public double MaxVolumeUnits { get; init; } = 3000000.0;

    /// <summary>
    /// The check "computed risk above <see cref="SanityCapPercent"/> of balance" on or off (RSK-002,
    /// RSK-018, spec 4.3). Default false (owner decision S of 2026-11-24). The other sanity checks
    /// of RSK-002 always apply.
    /// </summary>
    public bool SanityCapEnabled { get; init; }

    /// <summary>
    /// Largest computed risk of one trade, in percent of balance, when <see cref="SanityCapEnabled"/>
    /// is true (RSK-002, RSK-018). Default 0.55.
    /// </summary>
    public double SanityCapPercent { get; init; } = 0.55;

    /// <summary>
    /// The total open-risk cap of <see cref="MaxOpenRiskPercent"/> on or off (RSK-004, RSK-018, spec
    /// 4.2). Default false (owner decision W of 2026-11-24). The open risk is computed and reported
    /// either way.
    /// </summary>
    public bool OpenRiskCapEnabled { get; init; }

    /// <summary>
    /// The same-direction currency cap of <see cref="MaxPositionsPerCurrency"/> on or off (RSK-005,
    /// RSK-015, RSK-018, spec 4.2). Default false (owner decision W of 2026-11-24).
    /// </summary>
    public bool CurrencyCapEnabled { get; init; }

    /// <summary>
    /// How the risk of a new trade is set (RSK-016, RSK-018, spec 4.1): <c>Ladder</c>
    /// (RSK-013, or the fixed RiskPercent with LadderEnabled false) or <c>Recovery</c>
    /// (spec 4.1.1). Default Ladder.
    /// </summary>
    public SizingPlan SizingPlan { get; init; } = SizingPlan.Ladder;

    /// <summary>
    /// Recovery plan: the money the eight base amounts of a losing run lose together, in percent of
    /// <see cref="InitialCapital"/> (RSK-016, RSK-018, owner decision Z of 2026-11-24, "1% start").
    /// Default 1.0.
    /// </summary>
    public double RecoveryRunLossPercent { get; init; } = 1.0;

    /// <summary>
    /// Recovery plan: largest risk of one trade in percent of the balance at placement (RSK-016,
    /// RSK-018). Default 1.28.
    /// </summary>
    public double RecoveryMaxRiskPercent { get; init; } = 1.28;

    /// <summary>
    /// Ladder sizing on or off (RSK-013, spec 4.1). When true the risk percent of each new entry
    /// comes from <see cref="LadderSizer"/>; when false the fixed <see cref="RiskPercent"/> is used
    /// (RSK-001). Default true.
    /// </summary>
    public bool LadderEnabled { get; init; } = true;

    /// <summary>Risk of one ladder deal in percent of balance, before growth (RSK-013). Default 0.30.</summary>
    public double LadderDealRiskPercent { get; init; } = 0.30;

    /// <summary>Factor by which a ladder deal grows per consecutive winning trade (RSK-013). Default 1.5924.</summary>
    public double LadderGrowth { get; init; } = 1.5924;

    /// <summary>
    /// Weight w1 of the first of the four parts of a ladder deal (RSK-013). The other weights
    /// follow from it: w2 = w1/2, w3 = (w1+w2)/2, w4 = (w1+w2+w3)/2. Default 0.2962.
    /// </summary>
    public double LadderFirstWeight { get; init; } = 0.2962;

    /// <summary>Largest ladder risk of one entry in percent of balance (RSK-013). Default 0.5.</summary>
    public double MaxRiskPercentPerTrade { get; init; } = 0.5;

    /// <summary>
    /// Checks every value and throws for the first one that is out of range, in the order the
    /// parameters were added (the drawdown brake values of RSK-019 last). The message names the
    /// parameter, its allowed range and the rejected value in invariant formatting.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range. The exception's parameter name is the name of the parameter.</exception>
    public void Validate()
    {
        RequirePercent(RiskPercent, nameof(RiskPercent));
        RequirePercent(MaxOpenRiskPercent, nameof(MaxOpenRiskPercent));
        RequireCount(MaxPositionsPerCurrency, nameof(MaxPositionsPerCurrency));
        RequirePercent(DailyLossLimitPercent, nameof(DailyLossLimitPercent));
        RequirePercent(MaxDrawdownPercent, nameof(MaxDrawdownPercent));
        if (!double.IsFinite(InitialCapital) || InitialCapital <= 0.0)
        {
            throw OutOfRange(nameof(InitialCapital), "must be a finite number greater than zero", Text(InitialCapital));
        }

        RequirePercent(FtmoDailyLossPercent, nameof(FtmoDailyLossPercent));
        RequirePercent(FtmoMaxLossPercent, nameof(FtmoMaxLossPercent));
        double smallerFtmoLimit = Math.Min(FtmoDailyLossPercent, FtmoMaxLossPercent);
        if (!double.IsFinite(FtmoGuardBufferPercent) || FtmoGuardBufferPercent < 0.0 || FtmoGuardBufferPercent >= smallerFtmoLimit)
        {
            throw OutOfRange(nameof(FtmoGuardBufferPercent), "must be at least 0 and below FtmoDailyLossPercent and FtmoMaxLossPercent", Text(FtmoGuardBufferPercent));
        }

        if (RiskEpochDate.Kind != DateTimeKind.Utc)
        {
            throw OutOfRange(nameof(RiskEpochDate), "must be set as a date of kind UTC", RiskEpochDate.Kind.ToString());
        }

        if (RiskEpochDate.TimeOfDay != TimeSpan.Zero)
        {
            throw OutOfRange(nameof(RiskEpochDate), "must be a date without a time of day", RiskEpochDate.ToString("o", CultureInfo.InvariantCulture));
        }

        RequireCount(MaxEntriesPerSymbolPerDay, nameof(MaxEntriesPerSymbolPerDay));
        RequireCount(MaxEntriesPerDay, nameof(MaxEntriesPerDay));
        RequireCount(MaxRequestsPerHour, nameof(MaxRequestsPerHour));
        RequireCount(MaxRequestsPerDay, nameof(MaxRequestsPerDay));
        RequireCount(MaxErrorsPerHour, nameof(MaxErrorsPerHour));
        if (!double.IsFinite(MaxVolumeUnits) || MaxVolumeUnits <= 0.0)
        {
            throw OutOfRange(nameof(MaxVolumeUnits), "must be a finite number greater than zero", Text(MaxVolumeUnits));
        }

        // RSK-018: checked after every value that existed before, so their refusals are unchanged.
        RequirePercent(SanityCapPercent, nameof(SanityCapPercent));
        ValidateSizing();

        // RSK-019: checked after every value that existed before, so their refusals are unchanged.
        if (!double.IsFinite(DrawdownBrakePercent) || DrawdownBrakePercent < 0.0 || DrawdownBrakePercent >= MaxDrawdownPercent)
        {
            throw OutOfRange(nameof(DrawdownBrakePercent), "must be at least 0 and below MaxDrawdownPercent", Text(DrawdownBrakePercent));
        }

        if (!double.IsFinite(DrawdownBrakeFactor) || DrawdownBrakeFactor <= 0.0 || DrawdownBrakeFactor > 1.0)
        {
            throw OutOfRange(nameof(DrawdownBrakeFactor), "must be greater than 0 and at most 1", Text(DrawdownBrakeFactor));
        }
    }

    /// <summary>
    /// Checks only the values that decide the risk percent of a new entry (RSK-001, RSK-013), in
    /// this order: RiskPercent, LadderDealRiskPercent and MaxRiskPercentPerTrade greater than 0
    /// and at most 100, LadderGrowth a finite number of at least 1, LadderFirstWeight greater than
    /// 0 and at most 1 / 3.375, so that the four parts (3.375 x w1 together) never exceed the
    /// deal; then (RSK-018) SizingPlan Ladder or Recovery, RecoveryRunLossPercent and
    /// RecoveryMaxRiskPercent greater than 0 and at most 100. Called by
    /// <see cref="Validate"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range. The exception's parameter name is the name of the parameter.</exception>
    public void ValidateSizing()
    {
        RequirePercent(RiskPercent, nameof(RiskPercent));
        RequirePercent(LadderDealRiskPercent, nameof(LadderDealRiskPercent));
        if (!double.IsFinite(LadderGrowth) || LadderGrowth < 1.0)
        {
            throw OutOfRange(nameof(LadderGrowth), "must be a finite number of at least 1", Text(LadderGrowth));
        }

        if (!double.IsFinite(LadderFirstWeight) || LadderFirstWeight <= 0.0 || PriceMath.GreaterThan(LadderFirstWeight * 3.375, 1.0, PriceMath.DefaultTolerance))
        {
            throw OutOfRange(nameof(LadderFirstWeight), "must be greater than 0 and at most 1 / 3.375", Text(LadderFirstWeight));
        }

        RequirePercent(MaxRiskPercentPerTrade, nameof(MaxRiskPercentPerTrade));
        if (SizingPlan != SizingPlan.Ladder && SizingPlan != SizingPlan.Recovery)
        {
            throw OutOfRange(nameof(SizingPlan), "must be Ladder or Recovery", ((int)SizingPlan).ToString(CultureInfo.InvariantCulture));
        }

        RequirePercent(RecoveryRunLossPercent, nameof(RecoveryRunLossPercent));
        RequirePercent(RecoveryMaxRiskPercent, nameof(RecoveryMaxRiskPercent));
    }

    private static void RequirePercent(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0 || value > 100.0)
        {
            throw OutOfRange(name, "must be greater than 0 and at most 100", Text(value));
        }
    }

    private static void RequireCount(int value, string name)
    {
        if (value < 1)
        {
            throw OutOfRange(name, "must be at least 1", value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static ArgumentOutOfRangeException OutOfRange(string name, string rule, string actual)
    {
        return new ArgumentOutOfRangeException(name, name + " " + rule + " but was " + actual + ".");
    }

    private static string Text(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}
