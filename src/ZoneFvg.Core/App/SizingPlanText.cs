using System;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.App;

/// <summary>
/// The cBot text of SizingPlan (PLT-017, RSK-018, spec 4.1 and 7), turned into the core's value, so
/// that what can be tested without cTrader is tested in the core. The adapter calls
/// <see cref="Parse"/> at start in every mode. A refusal names the parameter as cTrader shows it.
/// Pure: ordinal comparison only.
/// </summary>
public static class SizingPlanText
{
    /// <summary>The value of SizingPlan for the ladder of RSK-013, the default (PLT-017).</summary>
    public const string Ladder = "Ladder";

    /// <summary>The value of SizingPlan for the Recovery plan of spec 4.1.1 (PLT-017).</summary>
    public const string Recovery = "Recovery";

    /// <summary>Reads SizingPlan: exactly <c>Ladder</c> or <c>Recovery</c>, case and spaces as written (PLT-017).</summary>
    /// <param name="text">The text of SizingPlan.</param>
    /// <returns>The sizing plan.</returns>
    /// <exception cref="ArgumentException">Any other text, the empty text or null; the parameter name of the exception is <c>SizingPlan</c>.</exception>
    public static SizingPlan Parse(string? text)
    {
        if (string.Equals(text, Ladder, StringComparison.Ordinal))
        {
            return SizingPlan.Ladder;
        }

        if (string.Equals(text, Recovery, StringComparison.Ordinal))
        {
            return SizingPlan.Recovery;
        }

        throw new ArgumentException("SizingPlan must be one of: Ladder, Recovery.", "SizingPlan");
    }
}
