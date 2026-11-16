using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.Domain;

/// <summary>
/// The order in which closed bars are passed to the core (SIG-004): by nominal close time
/// (<see cref="CloseTimeUtc"/>), and among bars with the same nominal close time D1 first, then
/// H4, then M15. The higher timeframe is processed first so that bias and zones are up to date
/// before the M15 bar is evaluated.
/// </summary>
/// <remarks>
/// Whoever feeds bars to the core (live events or a replay of history) orders them by
/// (<see cref="CloseTimeUtc"/>, <see cref="Rank"/>), never by the group in which they arrived
/// and never by open time (architecture section 10, "Live bar order"). <see cref="Sort"/> orders
/// bars whose nominal close times are equal.
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
    /// Nominal length of one bar of the timeframe (SIG-004): 15 minutes for M15, 4 hours for H4,
    /// 24 hours for D1 (docs/architecture.md section 10, "Close time").
    /// </summary>
    /// <param name="timeFrame">The timeframe.</param>
    /// <returns>The length of one bar.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The timeframe is not a defined value.</exception>
    public static TimeSpan Length(TimeFrame timeFrame)
    {
        switch (timeFrame)
        {
            case TimeFrame.M15:
                return TimeSpan.FromMinutes(15.0);
            case TimeFrame.H4:
                return TimeSpan.FromHours(4.0);
            case TimeFrame.D1:
                return TimeSpan.FromHours(24.0);
            default:
                throw new ArgumentOutOfRangeException(nameof(timeFrame), "Unknown timeframe.");
        }
    }

    /// <summary>
    /// Nominal close time of a bar (SIG-004): its open time plus <see cref="Length"/> of its
    /// timeframe. Bars of different timeframes close at the same instant when these times are equal.
    /// </summary>
    /// <param name="timeFrame">The timeframe of the bar.</param>
    /// <param name="bar">The bar.</param>
    /// <returns>The close time in UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The timeframe is not a defined value, or the close time is beyond the largest date.</exception>
    public static DateTime CloseTimeUtc(TimeFrame timeFrame, Bar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return bar.OpenTimeUtc + Length(timeFrame);
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
