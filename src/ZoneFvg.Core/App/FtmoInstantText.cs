using System;
using System.Globalization;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.App;

/// <summary>
/// The two cBot texts of FTMO Instant (PLT-022, RSK-021, OPS-012, spec v0.20 rows BH and BK),
/// turned into the core's values, so that what can be tested without cTrader is tested in the core.
/// The adapter calls both in <c>BuildRisk</c> at start in every mode. A refusal names the parameter
/// as cTrader shows it. Pure: ordinal comparison and invariant parsing only.
/// </summary>
public static class FtmoInstantText
{
    /// <summary>The value of FtmoMaxLossMode for the 2-Step rule of RSK-009 (PLT-022).</summary>
    public const string Static = "Static";

    /// <summary>The value of FtmoMaxLossMode for FTMO Instant, the cBot default (PLT-022, RSK-021).</summary>
    public const string TrailingBalance = "TrailingBalance";

    /// <summary>
    /// Reads FtmoMaxLossMode (RSK-021, PLT-022): exactly <c>Static</c> or <c>TrailingBalance</c>,
    /// case and spaces as written.
    /// </summary>
    /// <param name="text">The text of FtmoMaxLossMode.</param>
    /// <returns>The mode.</returns>
    /// <exception cref="ArgumentException">Any other text, the empty text or null; the parameter name of the exception is <c>FtmoMaxLossMode</c>.</exception>
    public static FtmoMaxLossMode MaxLossMode(string? text)
    {
        if (string.Equals(text, Static, StringComparison.Ordinal))
        {
            return FtmoMaxLossMode.Static;
        }

        if (string.Equals(text, TrailingBalance, StringComparison.Ordinal))
        {
            return FtmoMaxLossMode.TrailingBalance;
        }

        throw new ArgumentException("FtmoMaxLossMode must be one of: Static, TrailingBalance.", "FtmoMaxLossMode");
    }

    /// <summary>
    /// Reads RewardPeriodStartDate (OPS-012, PLT-022) as RiskEpochDate is read: an empty or
    /// whitespace text means "not set" (the readout then counts from RiskEpochDate); any other text
    /// must be a date written as <c>yyyy-MM-dd</c> (invariant culture) and gives that date at 00:00
    /// with kind UTC, which the core reads as 00:00 CE(S)T on that date.
    /// </summary>
    /// <param name="text">The text of RewardPeriodStartDate.</param>
    /// <returns>Null for an empty text, otherwise the date with kind UTC.</returns>
    /// <exception cref="ArgumentException">The text is not empty and not such a date; the parameter name of the exception is <c>RewardPeriodStartDate</c>.</exception>
    public static DateTime? RewardPeriodStart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime value))
        {
            return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
        }

        throw new ArgumentException("RewardPeriodStartDate must be empty or a date written as yyyy-MM-dd, for example 2026-12-15.", "RewardPeriodStartDate");
    }
}
