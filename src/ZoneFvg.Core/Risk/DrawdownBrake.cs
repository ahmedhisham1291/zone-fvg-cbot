using System;
using System.Globalization;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// What the drawdown brake of spec 4.1 decided for one taken trade (RSK-019): the kill switch's
/// peak balance, the account balance and the braked risk percent. Journal data only.
/// </summary>
/// <param name="Peak">The peak balance (<see cref="RiskEngine.PeakBalance"/>).</param>
/// <param name="Balance">The account balance at the decision.</param>
/// <param name="RiskPercent">The braked risk percent, RiskPercent x DrawdownBrakeFactor.</param>
public sealed record DrawdownBrakeNote(double Peak, double Balance, double RiskPercent)
{
    /// <summary>The peak in invariant "R" format after rounding to 2 decimals (RSK-019).</summary>
    public string PeakText => Text(Peak, 2);

    /// <summary>The balance in invariant "R" format after rounding to 2 decimals (RSK-019).</summary>
    public string BalanceText => Text(Balance, 2);

    /// <summary>The braked risk percent in invariant "R" format after rounding to 9 decimals (RSK-019).</summary>
    public string RiskPercentText => Text(RiskPercent, 9);

    private static string Text(double value, int decimals)
    {
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString("R", CultureInfo.InvariantCulture);
    }
}

/// <summary>The decision rule of the drawdown brake of spec 4.1 (RSK-019), a pure function.</summary>
public static class DrawdownBrake
{
    /// <summary>
    /// True when the brake applies (RSK-019): <paramref name="brakePercent"/> above 0, the peak above
    /// 0, and peak - balance at least peak x brakePercent / 100 within
    /// <see cref="PriceMath.DefaultTolerance"/> (equal counts as braked). A peak that is not
    /// positive never brakes; the kill switch halts on its own check.
    /// </summary>
    /// <param name="peak">The kill switch's peak balance.</param>
    /// <param name="balance">The account balance.</param>
    /// <param name="brakePercent">DrawdownBrakePercent.</param>
    /// <returns>True when braked.</returns>
    public static bool IsBraked(double peak, double balance, double brakePercent)
    {
        if (!(brakePercent > 0.0) || !(peak > 0.0))
        {
            return false;
        }

        double distance = peak * brakePercent / 100.0;
        return !PriceMath.LessThan(peak - balance, distance, PriceMath.DefaultTolerance);
    }
}
