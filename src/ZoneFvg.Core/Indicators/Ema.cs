using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Indicators;

/// <summary>
/// Exponential moving average of closes over a fixed window of closed bars (SIG-003). The value
/// depends on no bar outside its window, so a restarted bot reproduces it exactly.
/// </summary>
public static class Ema
{
    /// <summary>The EMA of period n reads exactly this many times n closes (SIG-003).</summary>
    public const int WindowMultiple = 4;

    /// <summary>
    /// EMA as of the bar at <paramref name="endIndex"/>, computed over exactly the last
    /// 4 x <paramref name="period"/> closes ending at that bar (SIG-003). The seed is the simple
    /// average of the first <paramref name="period"/> closes of that window; every later close c
    /// updates the value to alpha x c + (1 - alpha) x value, with alpha = 2 / (period + 1).
    /// </summary>
    /// <param name="bars">Closed bars, oldest first.</param>
    /// <param name="endIndex">Index of the newest bar of the window. Bars after it are not read.</param>
    /// <param name="period">EMA period. Must be at least 1.</param>
    /// <returns>The EMA, or null when fewer than 4 x period bars exist at or before the end index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The period is below 1, or the end index is beyond the last bar.</exception>
    public static double? Value(IReadOnlyList<Bar> bars, int endIndex, int period)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (period < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "The EMA period must be at least 1.");
        }

        if (endIndex >= bars.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(endIndex), "The end index is beyond the last closed bar.");
        }

        long window = (long)WindowMultiple * period;
        if (endIndex + 1L < window)
        {
            return null;
        }

        int start = endIndex - (int)window + 1;
        double seedSum = 0.0;
        for (int i = start; i < start + period; i++)
        {
            seedSum += bars[i].Close;
        }

        double ema = seedSum / period;
        double alpha = 2.0 / (period + 1.0);
        for (int i = start + period; i <= endIndex; i++)
        {
            ema = (alpha * bars[i].Close) + ((1.0 - alpha) * ema);
        }

        return ema;
    }
}
