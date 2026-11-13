using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Indicators;

/// <summary>
/// Average true range over a fixed window of closed bars (SIG-003). The value depends on no bar
/// outside its window, so a restarted bot reproduces it exactly.
/// </summary>
public static class Atr
{
    /// <summary>Default ATR period (spec section 7, AtrPeriod).</summary>
    public const int DefaultPeriod = 14;

    /// <summary>
    /// ATR as of the bar at <paramref name="endIndex"/>: the arithmetic mean of the true range of
    /// the last <paramref name="period"/> bars ending at that bar (SIG-003). The true range of a
    /// bar is the largest of high minus low, the distance from the previous close to the high and
    /// the distance from the previous close to the low, so one more bar than the period is needed.
    /// </summary>
    /// <param name="bars">Closed bars, oldest first.</param>
    /// <param name="endIndex">Index of the newest bar of the window. Bars after it are not read.</param>
    /// <param name="period">Number of true ranges averaged. Must be at least 1.</param>
    /// <returns>The ATR, or null when fewer than period + 1 bars exist at or before the end index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The period is below 1, or the end index is beyond the last bar.</exception>
    public static double? Value(IReadOnlyList<Bar> bars, int endIndex, int period = DefaultPeriod)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (period < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "The ATR period must be at least 1.");
        }

        if (endIndex >= bars.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(endIndex), "The end index is beyond the last closed bar.");
        }

        if (endIndex < period)
        {
            return null;
        }

        double sum = 0.0;
        for (int i = endIndex - period + 1; i <= endIndex; i++)
        {
            sum += TrueRange(bars[i], bars[i - 1].Close);
        }

        return sum / period;
    }

    private static double TrueRange(Bar bar, double previousClose)
    {
        double range = bar.High - bar.Low;
        double fromPreviousCloseToHigh = Math.Abs(bar.High - previousClose);
        double fromPreviousCloseToLow = Math.Abs(bar.Low - previousClose);
        return Math.Max(range, Math.Max(fromPreviousCloseToHigh, fromPreviousCloseToLow));
    }
}
