using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Indicators;

/// <summary>
/// Finds confirmed swing highs and swing lows on the zone timeframe (SIG-006). It is a pure
/// function of the closed bars it is given and keeps no state.
/// </summary>
public static class SwingDetector
{
    /// <summary>Only swings whose bar lies within this many most recent closed bars are retained (SIG-006).</summary>
    public const int LookbackBars = 100;

    /// <summary>
    /// Returns the swings that are confirmed as of the newest bar in <paramref name="bars"/> (SIG-006).
    /// A swing high is a bar whose high is strictly greater than the highs of
    /// <paramref name="strength"/> bars on each side; a swing low mirrors this with lows. A swing
    /// is confirmed only when the last of those bars to its right has closed, and it is retained
    /// only while its bar is among the last <see cref="LookbackBars"/> closed bars. The bars to
    /// the left of a retained swing may be older than that. "Strictly" is decided with
    /// <see cref="PriceMath"/> and its default tolerance, so equal prices are never a swing.
    /// </summary>
    /// <param name="bars">Closed bars of the zone timeframe, oldest first. The last bar is the newest closed bar.</param>
    /// <param name="strength">Number of bars required on each side (spec section 7, SwingStrength). Must be at least 1.</param>
    /// <returns>The confirmed swings, oldest first. A bar that is both a swing high and a swing low gives the high first.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The strength is below 1.</exception>
    public static IReadOnlyList<Swing> Find(IReadOnlyList<Bar> bars, int strength)
    {
        ArgumentNullException.ThrowIfNull(bars);
        if (strength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(strength), "The swing strength must be at least 1.");
        }

        var swings = new List<Swing>();

        // The newest candidate needs `strength` closed bars to its right; the oldest must be
        // inside the lookback and needs `strength` bars to its left.
        int newestCandidate = bars.Count - 1 - strength;
        int oldestCandidate = Math.Max(bars.Count - LookbackBars, strength);
        for (int index = oldestCandidate; index <= newestCandidate; index++)
        {
            Bar bar = bars[index];
            if (IsSwing(bars, index, strength, SwingKind.High))
            {
                swings.Add(new Swing(SwingKind.High, bar.OpenTimeUtc, bar.High));
            }

            if (IsSwing(bars, index, strength, SwingKind.Low))
            {
                swings.Add(new Swing(SwingKind.Low, bar.OpenTimeUtc, bar.Low));
            }
        }

        return swings.AsReadOnly();
    }

    private static bool IsSwing(IReadOnlyList<Bar> bars, int index, int strength, SwingKind kind)
    {
        double candidate = Extreme(bars[index], kind);
        for (int offset = 1; offset <= strength; offset++)
        {
            bool beyondLeft = IsBeyond(candidate, Extreme(bars[index - offset], kind), kind);
            bool beyondRight = IsBeyond(candidate, Extreme(bars[index + offset], kind), kind);
            if (!beyondLeft || !beyondRight)
            {
                return false;
            }
        }

        return true;
    }

    private static double Extreme(Bar bar, SwingKind kind)
    {
        return kind == SwingKind.High ? bar.High : bar.Low;
    }

    private static bool IsBeyond(double candidate, double other, SwingKind kind)
    {
        if (kind == SwingKind.High)
        {
            return PriceMath.GreaterThan(candidate, other, PriceMath.DefaultTolerance);
        }

        return PriceMath.LessThan(candidate, other, PriceMath.DefaultTolerance);
    }
}
