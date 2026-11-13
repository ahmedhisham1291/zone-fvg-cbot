using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Indicators;

/// <summary>
/// Trend bias from closed daily bars (SIG-005). The bias is a pure function of the closed D1
/// bars, so it can only change when a D1 bar closes; bars of other timeframes are never read.
/// </summary>
public static class BiasCalculator
{
    /// <summary>
    /// Bias as of the newest bar in <paramref name="d1Bars"/> (SIG-005): Long when the last closed
    /// D1 close is above the D1 EMA of <paramref name="emaPeriod"/>, Short when it is below, and
    /// Neutral when they are equal or when the EMA window of 4 x period bars is not yet full.
    /// Close and EMA are compared with <see cref="PriceMath"/> and its default tolerance.
    /// </summary>
    /// <param name="d1Bars">Closed D1 bars, oldest first. The last bar is the newest closed D1 bar.</param>
    /// <param name="emaPeriod">EMA period (spec section 7, BiasEmaPeriod). Must be at least 1.</param>
    /// <returns>The bias. Neutral never allows a trade.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The EMA period is below 1.</exception>
    public static Bias Compute(IReadOnlyList<Bar> d1Bars, int emaPeriod)
    {
        ArgumentNullException.ThrowIfNull(d1Bars);
        if (emaPeriod < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(emaPeriod), "The bias EMA period must be at least 1.");
        }

        int newest = d1Bars.Count - 1;
        double? ema = Ema.Value(d1Bars, newest, emaPeriod);
        if (ema is null)
        {
            return Bias.Neutral;
        }

        double close = d1Bars[newest].Close;
        if (PriceMath.GreaterThan(close, ema.Value, PriceMath.DefaultTolerance))
        {
            return Bias.Long;
        }

        if (PriceMath.LessThan(close, ema.Value, PriceMath.DefaultTolerance))
        {
            return Bias.Short;
        }

        return Bias.Neutral;
    }
}
