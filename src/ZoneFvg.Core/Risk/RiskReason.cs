namespace ZoneFvg.Core.Risk;

/// <summary>
/// Why the risk module refused a trade (RSK-001 to RSK-006, RSK-010). Every refusal carries
/// exactly one reason; <see cref="None"/> means the trade is allowed.
/// </summary>
public enum RiskReason
{
    /// <summary>The trade is allowed.</summary>
    None,

    /// <summary>An input value is missing, not finite, out of range or inconsistent (fail closed).</summary>
    InvalidInput,

    /// <summary>The stop is not on the loss side of entry (RSK-002).</summary>
    StopOnWrongSide,

    /// <summary>The target is not on the profit side of entry (RSK-002).</summary>
    TargetOnWrongSide,

    /// <summary>The volume rounded down to the step is below the symbol's minimum volume (RSK-001).</summary>
    BelowMinimumVolume,

    /// <summary>The volume is above MaxVolumeUnits (RSK-002).</summary>
    VolumeAboveCap,

    /// <summary>The computed risk is above SanityCapPercent (default 0.55) of balance, checked only with SanityCapEnabled (RSK-002).</summary>
    RiskAboveSanityCap,

    /// <summary>The snapshot's history does not reach back far enough to compute the limits (RSK-007, RSK-008).</summary>
    HistoryIncomplete,

    /// <summary>The snapshot has no symbol data for a symbol the rules need (fail closed).</summary>
    MissingSymbolInfo,

    /// <summary>Free margin after the trade would be below 50% of equity (RSK-003).</summary>
    FreeMarginTooLow,

    /// <summary>Total open risk including the trade would exceed MaxOpenRiskPercent of initial capital (RSK-004).</summary>
    OpenRiskCapExceeded,

    /// <summary>Same-side positions and orders in one currency would exceed MaxPositionsPerCurrency (RSK-005).</summary>
    CurrencyExposureCapExceeded,

    /// <summary>The day's worst-case loss including the trade would exceed the daily risk budget (RSK-006).</summary>
    DailyBudgetExceeded,

    /// <summary>The symbol already has the most entries allowed in this CE(S)T day (RSK-010).</summary>
    SymbolEntryLimitReached,

    /// <summary>The account already has the most entries allowed in this CE(S)T day (RSK-010).</summary>
    AccountEntryLimitReached,

    /// <summary>The closed loss of the CE(S)T calendar month is at or beyond MonthlyLossLimitPercent of initial capital (RSK-020).</summary>
    MonthlyLossLimitReached,
}
