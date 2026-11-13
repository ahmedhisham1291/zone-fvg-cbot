using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.Domain;

/// <summary>
/// The order in which closed bars of different timeframes are passed to the core when they
/// close at the same instant: D1 first, then H4, then M15 (SIG-004). The higher timeframe is
/// processed first so that bias and zones are up to date before the M15 bar is evaluated.
/// </summary>
/// <remarks>
/// Whoever feeds bars to the core (live events or a replay of history) sorts each group of
/// same-instant bars with <see cref="Sort"/> before passing them on one by one.
/// </remarks>
public static class BarIntakeOrder
{
    /// <summary>The timeframes in processing order for bars that close together: D1, H4, M15 (SIG-004).</summary>
    public static IReadOnlyList<TimeFrame> SameInstant { get; } = Array.AsReadOnly(new[] { TimeFrame.D1, TimeFrame.H4, TimeFrame.M15 });

    /// <summary>Position of a timeframe in the processing order: 0 for D1, 1 for H4, 2 for M15 (SIG-004).</summary>
    /// <param name="timeFrame">The timeframe.</param>
    /// <returns>The zero-based position; a lower number is processed earlier.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The timeframe is not a defined value.</exception>
    public static int Rank(TimeFrame timeFrame)
    {
        switch (timeFrame)
        {
            case TimeFrame.D1:
                return 0;
            case TimeFrame.H4:
                return 1;
            case TimeFrame.M15:
                return 2;
            default:
                throw new ArgumentOutOfRangeException(nameof(timeFrame), "Unknown timeframe.");
        }
    }

    /// <summary>
    /// Puts bars that closed at the same instant into processing order D1, H4, M15 (SIG-004).
    /// The caller guarantees that the bars closed together; at most one bar per timeframe is allowed.
    /// </summary>
    /// <param name="closedTogether">The bars that closed at the same instant, each with its timeframe, in any order.</param>
    /// <returns>The same bars ordered D1, H4, M15.</returns>
    /// <exception cref="ArgumentException">A bar is missing, or two bars have the same timeframe.</exception>
    public static IReadOnlyList<(TimeFrame TimeFrame, Bar Bar)> Sort(IEnumerable<(TimeFrame TimeFrame, Bar Bar)> closedTogether)
    {
        ArgumentNullException.ThrowIfNull(closedTogether);

        var slots = new Bar?[SameInstant.Count];
        foreach ((TimeFrame timeFrame, Bar bar) in closedTogether)
        {
            int rank = Rank(timeFrame);
            if (bar is null)
            {
                throw new ArgumentException("A bar is missing for timeframe " + timeFrame + ".", nameof(closedTogether));
            }

            if (slots[rank] is not null)
            {
                throw new ArgumentException("More than one bar was given for timeframe " + timeFrame + ".", nameof(closedTogether));
            }

            slots[rank] = bar;
        }

        var ordered = new List<(TimeFrame TimeFrame, Bar Bar)>(slots.Length);
        for (int rank = 0; rank < slots.Length; rank++)
        {
            Bar? bar = slots[rank];
            if (bar is not null)
            {
                ordered.Add((SameInstant[rank], bar));
            }
        }

        return ordered.AsReadOnly();
    }
}
