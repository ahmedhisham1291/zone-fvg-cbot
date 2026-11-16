using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Recovery;

/// <summary>
/// Checks a bar series before replay and while running (REC-003, spec 8.3 step 1 and 8.4):
/// enough bars, closed bars only, strictly increasing open times, no gap and a recent last bar.
/// </summary>
/// <remarks>
/// <para>A gap is a missing bar where the market was open (prompt WP07). The market counts as
/// closed during the weekend closure, from Friday at FridayCloseUtc to Monday 00:00 UTC; at every
/// other time it counts as open. Two consecutive bars of a series have a gap when the open-market
/// time between the close of the first and the open of the second is at least one bar length.
/// For M15 bars, which are aligned to 15 minutes, this is the rule "consecutive M15 bars more than
/// 15 minutes apart outside the weekend closure". For H4 and D1 it tolerates the one-hour shift of
/// a broker whose bars follow a daylight-saving clock.</para>
/// <para>The last bar is recent when less than two bar lengths of open-market time have passed
/// since its close. One bar of slack covers a bar that closes while the history is being read.</para>
/// </remarks>
public static class BarSeriesCheck
{
    /// <summary>
    /// Open-market time between two instants (REC-003): their distance minus the part inside a
    /// weekend closure, Friday at <paramref name="fridayCloseUtc"/> to Monday 00:00 UTC.
    /// </summary>
    /// <param name="fromUtc">Start, kind UTC.</param>
    /// <param name="toUtc">End, kind UTC.</param>
    /// <param name="fridayCloseUtc">UTC time of day at which the weekend closure starts on Fridays.</param>
    /// <returns>The open-market time; zero when <paramref name="toUtc"/> is not after <paramref name="fromUtc"/>.</returns>
    public static TimeSpan OpenMarketTime(DateTime fromUtc, DateTime toUtc, TimeSpan fridayCloseUtc)
    {
        if (toUtc <= fromUtc)
        {
            return TimeSpan.Zero;
        }

        int sinceMonday = ((int)fromUtc.DayOfWeek + 6) % 7;
        DateTime monday = fromUtc.Date.AddDays(-sinceMonday);
        TimeSpan closed = TimeSpan.Zero;
        for (DateTime friday = monday.AddDays(-3.0); friday < toUtc; friday = friday.AddDays(7.0))
        {
            DateTime start = friday + fridayCloseUtc;
            DateTime end = friday.AddDays(3.0);
            DateTime from = start > fromUtc ? start : fromUtc;
            DateTime to = end < toUtc ? end : toUtc;
            if (to > from)
            {
                closed += to - from;
            }
        }

        return toUtc - fromUtc - closed;
    }

    /// <summary>True when bars are missing between two consecutive bars of a series (REC-003).</summary>
    /// <param name="timeFrame">Timeframe of both bars.</param>
    /// <param name="previous">The earlier bar.</param>
    /// <param name="next">The bar that follows it in the series.</param>
    /// <param name="fridayCloseUtc">UTC time of day at which the weekend closure starts on Fridays.</param>
    /// <returns>True when at least one bar length of open-market time lies between them.</returns>
    /// <exception cref="ArgumentNullException">A bar is null.</exception>
    public static bool IsGap(TimeFrame timeFrame, Bar previous, Bar next, TimeSpan fridayCloseUtc)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        TimeSpan length = BarIntakeOrder.Length(timeFrame);
        return OpenMarketTime(previous.OpenTimeUtc + length, next.OpenTimeUtc, fridayCloseUtc) >= length;
    }

    /// <summary>
    /// The first problem of a series, or null when it is complete (REC-003): fewer bars than
    /// <paramref name="required"/>, a missing bar, open times not strictly increasing, a bar not
    /// yet closed at <paramref name="nowUtc"/>, a gap, or a last bar that is not recent.
    /// </summary>
    /// <param name="timeFrame">Timeframe of the series.</param>
    /// <param name="bars">The series, oldest first.</param>
    /// <param name="required">Fewest bars needed (the warm-up requirement).</param>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <param name="fridayCloseUtc">UTC time of day at which the weekend closure starts on Fridays.</param>
    /// <returns>A short invariant description of the first problem, or null.</returns>
    public static string? Problem(TimeFrame timeFrame, IReadOnlyList<Bar>? bars, int required, DateTime nowUtc, TimeSpan fridayCloseUtc)
    {
        if (bars == null)
        {
            return timeFrame + ": no bars";
        }

        if (bars.Count < required)
        {
            return timeFrame + ": " + Text(bars.Count) + " bars, " + Text(required) + " needed";
        }

        TimeSpan length = BarIntakeOrder.Length(timeFrame);
        for (int i = 0; i < bars.Count; i++)
        {
            Bar? bar = bars[i];
            if (bar == null)
            {
                return timeFrame + ": bar " + Text(i) + " is missing";
            }

            if (bar.OpenTimeUtc + length > nowUtc)
            {
                return timeFrame + ": bar opening " + Stamp(bar.OpenTimeUtc) + " has not closed at " + Stamp(nowUtc);
            }

            if (i > 0 && bars[i - 1] is Bar previous)
            {
                if (bar.OpenTimeUtc <= previous.OpenTimeUtc)
                {
                    return timeFrame + ": bar opening " + Stamp(bar.OpenTimeUtc) + " does not follow " + Stamp(previous.OpenTimeUtc);
                }

                if (IsGap(timeFrame, previous, bar, fridayCloseUtc))
                {
                    return timeFrame + ": gap between " + Stamp(previous.OpenTimeUtc) + " and " + Stamp(bar.OpenTimeUtc);
                }
            }
        }

        if (bars.Count > 0)
        {
            DateTime lastClose = bars[bars.Count - 1].OpenTimeUtc + length;
            if (OpenMarketTime(lastClose, nowUtc, fridayCloseUtc) >= length + length)
            {
                return timeFrame + ": last bar closed " + Stamp(lastClose) + ", not recent at " + Stamp(nowUtc);
            }
        }

        return null;
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Stamp(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
