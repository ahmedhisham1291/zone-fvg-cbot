using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// An impulse leg on the zone timeframe (SIG-007): 1 to MaxImpulseBars consecutive bars of the
/// same direction that end at the just-closed H4 bar and move far enough.
/// </summary>
/// <param name="Type">The zone type the leg can create: Demand for a bullish leg, Supply for a bearish leg.</param>
/// <param name="FirstIndex">Index of the leg's first bar in the buffer it was found in.</param>
/// <param name="LastIndex">Index of the leg's last bar: always the newest bar of that buffer.</param>
/// <param name="Atr">
/// ATR(H4) as of the bar immediately before the leg's first bar. The base (SIG-009) and the
/// width check (SIG-011) use this same value.
/// </param>
public sealed record ImpulseLeg(ZoneType Type, int FirstIndex, int LastIndex, double Atr)
{
    /// <summary>Number of bars in the leg, from 1 to MaxImpulseBars (SIG-007).</summary>
    public int BarCount => LastIndex - FirstIndex + 1;

    /// <summary>
    /// Finds the impulse leg that ends at the newest bar of <paramref name="h4Bars"/> (SIG-007).
    /// The candidates are the runs of 1 to MaxImpulseBars consecutive bars of the newest bar's
    /// direction (bullish: close above open; bearish: close below open) that end at the newest
    /// bar. A bar whose close equals its open (within <see cref="PriceMath.DefaultTolerance"/>)
    /// ends a run, and so does a bar of the other direction. A candidate qualifies when its net
    /// move |close(last) - open(first)| is at least ImpulseAtrMult x ATR(H4), the ATR being the
    /// value as of the bar immediately before the candidate's first bar. The longest qualifying
    /// candidate is the leg. A candidate without enough bars for that ATR does not qualify.
    /// </summary>
    /// <param name="h4Bars">Closed H4 bars, oldest first. The last bar is the just-closed bar.</param>
    /// <param name="parameters">Signal parameters. They are validated first.</param>
    /// <returns>The leg, or null when no candidate qualifies.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public static ImpulseLeg? Find(IReadOnlyList<Bar> h4Bars, SignalParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(h4Bars);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();

        int last = h4Bars.Count - 1;
        if (last < 0)
        {
            return null;
        }

        int direction = Direction(h4Bars[last]);
        if (direction == 0)
        {
            return null;
        }

        int runLength = 1;
        while (runLength < parameters.MaxImpulseBars && last - runLength >= 0 && Direction(h4Bars[last - runLength]) == direction)
        {
            runLength++;
        }

        ZoneType type = direction > 0 ? ZoneType.Demand : ZoneType.Supply;
        for (int count = runLength; count >= 1; count--)
        {
            int first = last - count + 1;
            double? atr = AtrBefore(h4Bars, first, parameters.AtrPeriod);
            if (atr is null)
            {
                continue;
            }

            double netMove = Math.Abs(h4Bars[last].Close - h4Bars[first].Open);
            double threshold = parameters.ImpulseAtrMult * atr.Value;
            if (!PriceMath.LessThan(netMove, threshold, PriceMath.DefaultTolerance))
            {
                return new ImpulseLeg(type, first, last, atr.Value);
            }
        }

        return null;
    }

    private static int Direction(Bar bar)
    {
        if (PriceMath.GreaterThan(bar.Close, bar.Open, PriceMath.DefaultTolerance))
        {
            return 1;
        }

        if (PriceMath.LessThan(bar.Close, bar.Open, PriceMath.DefaultTolerance))
        {
            return -1;
        }

        return 0;
    }

    private static double? AtrBefore(IReadOnlyList<Bar> bars, int first, int period)
    {
        int before = first - 1;
        if (before < 0)
        {
            return null;
        }

        return global::ZoneFvg.Core.Indicators.Atr.Value(bars, before, period);
    }
}
