using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Recovery;

/// <summary>
/// Checks a bar series before replay and while running (REC-003, spec 8.3 step 1 and 8.4):
/// enough bars, closed bars only, strictly increasing open times, no gap and a recent last bar.
/// </summary>
/// <remarks>
/// <para>A gap is a missing bar where the market was open (prompt WP07). The market counts as
/// closed during the weekend closure, from Friday at FridayCloseUtc to Monday 00:00 UTC, and on
/// the whole UTC day of a market holiday (owner decision of 2026-11-16); at every other time it
/// counts as open. Two consecutive bars of a series have a gap when the open-market
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
    /// weekend closure, Friday at <paramref name="fridayCloseUtc"/> to Monday 00:00 UTC, or on a
    /// market holiday (the whole UTC day).
    /// </summary>
    /// <param name="fromUtc">Start, kind UTC.</param>
    /// <param name="toUtc">End, kind UTC.</param>
    /// <param name="fridayCloseUtc">UTC time of day at which the weekend closure starts on Fridays.</param>
    /// <param name="holidays">Market holidays (owner decision of 2026-11-16); null means none.</param>
    /// <returns>The open-market time; zero when <paramref name="toUtc"/> is not after <paramref name="fromUtc"/>.</returns>
    public static TimeSpan OpenMarketTime(DateTime fromUtc, DateTime toUtc, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays = null)
    {
        TimeSpan open = TimeSpan.Zero;
        DateTime from = fromUtc;
        while (from < toUtc)
        {
            DateTime dayStart = from.Date;
            DateTime dayEnd = dayStart.AddDays(1.0);
            DateTime to = dayEnd < toUtc ? dayEnd : toUtc;
            DateTime openUntil = OpenUntil(dayStart, fridayCloseUtc, holidays);
            if (openUntil > from)
            {
                open += (openUntil < to ? openUntil : to) - from;
            }

            from = to;
        }

        return open;
    }

    /// <summary>True when bars are missing between two consecutive bars of a series (REC-003).</summary>
    /// <param name="timeFrame">Timeframe of both bars.</param>
    /// <param name="previous">The earlier bar.</param>
    /// <param name="next">The bar that follows it in the series.</param>
    /// <param name="fridayCloseUtc">UTC time of day at which the weekend closure starts on Fridays.</param>
    /// <returns>True when at least one bar length of open-market time lies between them.</returns>
    /// <exception cref="ArgumentNullException">A bar is null.</exception>
    /// <param name="holidays">Market holidays (owner decision of 2026-11-16); null means none.</param>
    public static bool IsGap(TimeFrame timeFrame, Bar previous, Bar next, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays = null)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        TimeSpan length = BarIntakeOrder.Length(timeFrame);
        return OpenMarketTime(previous.OpenTimeUtc + length, next.OpenTimeUtc, fridayCloseUtc, holidays) >= length;
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
    /// <param name="holidays">Market holidays (owner decision of 2026-11-16); null means none.</param>
    public static string? Problem(TimeFrame timeFrame, IReadOnlyList<Bar>? bars, int required, DateTime nowUtc, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays = null)
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
                return timeFrame + ": bar opening " + Minute(bar.OpenTimeUtc) + " has not closed at " + Minute(nowUtc);
            }

            if (i > 0 && bars[i - 1] is Bar previous)
            {
                if (bar.OpenTimeUtc <= previous.OpenTimeUtc)
                {
                    return timeFrame + ": bar opening " + Minute(bar.OpenTimeUtc) + " does not follow " + Minute(previous.OpenTimeUtc);
                }

                if (IsGap(timeFrame, previous, bar, fridayCloseUtc, holidays))
                {
                    return timeFrame + ": gap between " + Minute(previous.OpenTimeUtc) + " and " + Minute(bar.OpenTimeUtc);
                }
            }
        }

        if (bars.Count > 0)
        {
            DateTime lastClose = bars[bars.Count - 1].OpenTimeUtc + length;
            if (OpenMarketTime(lastClose, nowUtc, fridayCloseUtc, holidays) >= length + length)
            {
                return timeFrame + ": last bar closed " + Minute(lastClose) + ", not recent at " + Minute(nowUtc);
            }
        }

        return null;
    }

    // The end of the open part of a UTC day that starts at its open: Saturday, Sunday and holidays are
    // closed all day, Friday closes at FridayCloseUtc, other days are open all day.
    private static DateTime OpenUntil(DateTime dayStart, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays)
    {
        if (dayStart.DayOfWeek == DayOfWeek.Saturday || dayStart.DayOfWeek == DayOfWeek.Sunday || MonthDay.Contains(holidays, dayStart))
        {
            return dayStart;
        }

        return dayStart.DayOfWeek == DayOfWeek.Friday ? dayStart + fridayCloseUtc : dayStart.AddDays(1.0);
    }
}
