using System;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Execution;

/// <summary>Mapping of risk-module reasons to skip reasons (EXE-014, RSK-002).</summary>
public static class SkipReasons
{
    /// <summary>The skip reason for a refusal of the risk module (EXE-014). Each risk reason has its own skip reason.</summary>
    /// <param name="reason">A refusal reason; not <see cref="RiskReason.None"/>.</param>
    /// <returns>The skip reason.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reason"/> is None or not a defined value.</exception>
    public static SkipReason FromRisk(RiskReason reason)
    {
        switch (reason)
        {
            case RiskReason.InvalidInput:
                return SkipReason.RiskInvalidInput;
            case RiskReason.StopOnWrongSide:
                return SkipReason.StopOnWrongSide;
            case RiskReason.TargetOnWrongSide:
                return SkipReason.TargetOnWrongSide;
            case RiskReason.BelowMinimumVolume:
                return SkipReason.BelowMinimumVolume;
            case RiskReason.VolumeAboveCap:
                return SkipReason.VolumeAboveCap;
            case RiskReason.RiskAboveSanityCap:
                return SkipReason.RiskAboveSanityCap;
            case RiskReason.HistoryIncomplete:
                return SkipReason.HistoryIncomplete;
            case RiskReason.MissingSymbolInfo:
                return SkipReason.MissingSymbolInfo;
            case RiskReason.FreeMarginTooLow:
                return SkipReason.FreeMarginTooLow;
            case RiskReason.OpenRiskCapExceeded:
                return SkipReason.OpenRiskCapExceeded;
            case RiskReason.CurrencyExposureCapExceeded:
                return SkipReason.CurrencyExposureCapExceeded;
            case RiskReason.DailyBudgetExceeded:
                return SkipReason.DailyBudgetExceeded;
            case RiskReason.SymbolEntryLimitReached:
                return SkipReason.SymbolEntryLimitReached;
            case RiskReason.AccountEntryLimitReached:
                return SkipReason.AccountEntryLimitReached;
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), "Only a refusal reason maps to a skip reason.");
        }
    }

    /// <summary>
    /// True for the sanity caps of RSK-002, which raise an alert besides the skip record: stop or
    /// target on the wrong side, volume above MaxVolumeUnits, risk above 0.55% of balance.
    /// </summary>
    /// <param name="reason">The skip reason.</param>
    /// <returns>True when an alert is required.</returns>
    public static bool IsSanityCap(SkipReason reason)
    {
        return reason == SkipReason.StopOnWrongSide
            || reason == SkipReason.TargetOnWrongSide
            || reason == SkipReason.VolumeAboveCap
            || reason == SkipReason.RiskAboveSanityCap;
    }
}
