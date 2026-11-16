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
/// and never by open time (architecture section 10, "Live bar order").
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
}
