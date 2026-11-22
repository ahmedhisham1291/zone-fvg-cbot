using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Recovery;

/// <summary>
/// A gap between two consecutive bars of a series (REC-003): the open times of the bar before and
/// the bar after the missing bars.
/// </summary>
/// <param name="TimeFrame">Timeframe of the series.</param>
/// <param name="PreviousOpenUtc">Open time of the bar before the gap, kind UTC.</param>
/// <param name="NextOpenUtc">Open time of the bar after the gap, kind UTC.</param>
public sealed record BarGap(TimeFrame TimeFrame, DateTime PreviousOpenUtc, DateTime NextOpenUtc)
{
    /// <summary>The gap as <c>yyyy-MM-dd HH:mm&gt;yyyy-MM-dd HH:mm</c> (REC-003 journal and report).</summary>
    public string Text => Minute(PreviousOpenUtc) + ">" + Minute(NextOpenUtc);
}

/// <summary>
/// Checks a bar series before replay and while running (REC-003, spec 8.3 step 1 and 8.4):
/// enough bars, closed bars only, strictly increasing open times, no gap and a recent last bar.
/// </summary>
/// <remarks>
/// <para>A gap is a missing bar where the market was open (prompt WP07). The market counts as
/// closed during the weekend closure, from Friday at FridayCloseUtc to Monday 00:00 UTC, and on
/// the whole UTC day of a market holiday (owner decision of 2026-11-16); at every other time it
/// counts as open. When the broker reports the symbol's weekly trading sessions, a moment also
/// counts as closed when it lies outside every session (REC-016, owner decision D of
/// 2026-11-17), for example gold's daily break; sessions never make a closed moment open. A
/// session list that is empty, holds an invalid session, or covers the whole week (start equal to
/// end) adds no closed time, so the check then has more gaps, never fewer. Two consecutive bars
/// of a series have a gap when the open-market time between the close of the first and the open
/// of the second is at least one bar length.
/// For M15 bars, which are aligned to 15 minutes, this is the rule "consecutive M15 bars more than
/// 15 minutes apart outside the weekend closure". For H4 and D1 it tolerates the one-hour shift of
/// a broker whose bars follow a daylight-saving clock.</para>
/// <para>The last bar is recent when less than two bar lengths of open-market time have passed
/// since its close. One bar of slack covers a bar that closes while the history is being read.
/// <see cref="ProblemOtherThanGap"/> takes another number of bar lengths (REC-018): recovery uses
/// one for the D1 series in Breakout mode, where the last closed D1 bar is the reference.</para>
/// </remarks>
public static class BarSeriesCheck
{
    private static readonly TimeSpan Week = TimeSpan.FromDays(7.0);

    /// <summary>
    /// Open-market time between two instants (REC-003): their distance minus the part inside a
    /// weekend closure, Friday at <paramref name="fridayCloseUtc"/> to Monday 00:00 UTC, or on a
    /// market holiday (the whole UTC day).
    /// </summary>
    /// <param name="fromUtc">Start, kind UTC.</param>
    /// <param name="toUtc">End, kind UTC.</param>
    /// <param name="fridayCloseUtc">UTC time of day at which the weekend closure starts on Fridays.</param>
    /// <param name="holidays">Market holidays (owner decision of 2026-11-16); null means none.</param>
    /// <param name="sessions">The symbol's weekly trading sessions, UTC (REC-016); null or empty means none.</param>
    /// <returns>The open-market time; zero when <paramref name="toUtc"/> is not after <paramref name="fromUtc"/>.</returns>
    public static TimeSpan OpenMarketTime(DateTime fromUtc, DateTime toUtc, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays = null, IReadOnlyList<TradingSession>? sessions = null)
    {
        return OpenTime(fromUtc, toUtc, fridayCloseUtc, holidays, WeeklyOpen(sessions));
    }

    private static TimeSpan OpenTime(DateTime fromUtc, DateTime toUtc, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays, IReadOnlyList<OpenSpan>? week)
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
                DateTime end = openUntil < to ? openUntil : to;
                open += week == null ? end - from : InSessions(from, end, week);
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
    /// <param name="sessions">The symbol's weekly trading sessions, UTC (REC-016); null or empty means none.</param>
    public static bool IsGap(TimeFrame timeFrame, Bar previous, Bar next, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays = null, IReadOnlyList<TradingSession>? sessions = null)
    {
        return IsGapIn(timeFrame, previous, next, fridayCloseUtc, holidays, WeeklyOpen(sessions));
    }

    private static bool IsGapIn(TimeFrame timeFrame, Bar previous, Bar next, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays, IReadOnlyList<OpenSpan>? week)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        TimeSpan length = BarIntakeOrder.Length(timeFrame);
        return OpenTime(previous.OpenTimeUtc + length, next.OpenTimeUtc, fridayCloseUtc, holidays, week) >= length;
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
    /// <param name="sessions">The symbol's weekly trading sessions, UTC (REC-016); null or empty means none.</param>
    public static string? Problem(TimeFrame timeFrame, IReadOnlyList<Bar>? bars, int required, DateTime nowUtc, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays = null, IReadOnlyList<TradingSession>? sessions = null)
    {
        return FirstProblem(timeFrame, bars, required, nowUtc, fridayCloseUtc, holidays, WeeklyOpen(sessions), true, 2);
    }

    /// <summary>
    /// The first problem of a series other than a gap, or null (REC-003, owner decision A of
    /// 2026-11-17): fewer bars than <paramref name="required"/>, a missing bar, open times not
    /// strictly increasing, a bar not yet closed at <paramref name="nowUtc"/>, or a last bar that
    /// is not recent. The texts are those of <see cref="Problem"/>. Gaps are listed by
    /// <see cref="Gaps"/>.
    /// </summary>
    /// <param name="timeFrame">Timeframe of the series.</param>
    /// <param name="bars">The series, oldest first.</param>
    /// <param name="required">Fewest bars needed (the warm-up requirement).</param>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <param name="fridayCloseUtc">UTC time of day at which the weekend closure starts on Fridays.</param>
    /// <param name="holidays">Market holidays (owner decision of 2026-11-16); null means none.</param>
    /// <param name="sessions">The symbol's weekly trading sessions, UTC (REC-016); null or empty means none.</param>
    /// <param name="staleBarLengths">
    /// REC-018: the number of bar lengths of open-market time since the close of the last bar at
    /// which the last bar is not recent. Default 2, the limit of REC-003; recovery passes 1 for the
    /// D1 series in Breakout mode.
    /// </param>
    /// <returns>A short invariant description of the first problem, or null.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="staleBarLengths"/> is below 1.</exception>
    public static string? ProblemOtherThanGap(TimeFrame timeFrame, IReadOnlyList<Bar>? bars, int required, DateTime nowUtc, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays = null, IReadOnlyList<TradingSession>? sessions = null, int staleBarLengths = 2)
    {
        if (staleBarLengths < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(staleBarLengths), staleBarLengths, "The number of bar lengths after which the last bar is stale must be at least 1.");
        }

        return FirstProblem(timeFrame, bars, required, nowUtc, fridayCloseUtc, holidays, WeeklyOpen(sessions), false, staleBarLengths);
    }

    /// <summary>
    /// Every gap of a series, oldest first (REC-003, owner decision A of 2026-11-17): each pair of
    /// consecutive bars for which <see cref="IsGap"/> is true. Pairs with a missing bar or with
    /// open times that do not increase are not gaps; <see cref="ProblemOtherThanGap"/> reports them.
    /// </summary>
    /// <param name="timeFrame">Timeframe of the series.</param>
    /// <param name="bars">The series, oldest first; null gives no gap.</param>
    /// <param name="fridayCloseUtc">UTC time of day at which the weekend closure starts on Fridays.</param>
    /// <param name="holidays">Market holidays (owner decision of 2026-11-16); null means none.</param>
    /// <param name="sessions">The symbol's weekly trading sessions, UTC (REC-016); null or empty means none.</param>
    /// <returns>The gaps, oldest first.</returns>
    public static IReadOnlyList<BarGap> Gaps(TimeFrame timeFrame, IReadOnlyList<Bar>? bars, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays = null, IReadOnlyList<TradingSession>? sessions = null)
    {
        IReadOnlyList<OpenSpan>? week = WeeklyOpen(sessions);
        var gaps = new List<BarGap>();
        if (bars == null)
        {
            return gaps.AsReadOnly();
        }

        for (int i = 1; i < bars.Count; i++)
        {
            if (bars[i - 1] is Bar previous && bars[i] is Bar next && next.OpenTimeUtc > previous.OpenTimeUtc && IsGapIn(timeFrame, previous, next, fridayCloseUtc, holidays, week))
            {
                gaps.Add(new BarGap(timeFrame, previous.OpenTimeUtc, next.OpenTimeUtc));
            }
        }

        return gaps.AsReadOnly();
    }

    // staleBarLengths (REC-018): the last bar is not recent from this many whole bar lengths of open-market time
    // since its close; compared in whole bar lengths, so that no product of ticks can overflow.
    private static string? FirstProblem(TimeFrame timeFrame, IReadOnlyList<Bar>? bars, int required, DateTime nowUtc, TimeSpan fridayCloseUtc, IReadOnlyCollection<MonthDay>? holidays, IReadOnlyList<OpenSpan>? week, bool gaps, int staleBarLengths)
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

                if (gaps && IsGapIn(timeFrame, previous, bar, fridayCloseUtc, holidays, week))
                {
                    return timeFrame + ": gap between " + Minute(previous.OpenTimeUtc) + " and " + Minute(bar.OpenTimeUtc);
                }
            }
        }

        if (bars.Count > 0)
        {
            DateTime lastClose = bars[bars.Count - 1].OpenTimeUtc + length;
            if (OpenTime(lastClose, nowUtc, fridayCloseUtc, holidays, week).Ticks / length.Ticks >= staleBarLengths)
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

    // REC-016: the open parts of the week as sorted, disjoint spans of offsets from Sunday 00:00 UTC,
    // or null when the sessions add no closed time (none, an invalid one, or one covering the whole
    // week); the caller then uses the weekend and holiday model alone.
    private static IReadOnlyList<OpenSpan>? WeeklyOpen(IReadOnlyList<TradingSession>? sessions)
    {
        if (sessions == null || sessions.Count == 0)
        {
            return null;
        }

        var spans = new List<OpenSpan>();
        foreach (TradingSession? session in sessions)
        {
            if (session == null || !session.IsValid)
            {
                return null;
            }

            TimeSpan start = WeekOffset(session.StartDay, session.StartTime);
            TimeSpan end = WeekOffset(session.EndDay, session.EndTime);
            if (start == end)
            {
                return null;
            }

            if (start < end)
            {
                spans.Add(new OpenSpan(start, end));
            }
            else
            {
                spans.Add(new OpenSpan(start, Week));
                spans.Add(new OpenSpan(TimeSpan.Zero, end));
            }
        }

        return Merge(spans);
    }

    private static IReadOnlyList<OpenSpan> Merge(List<OpenSpan> spans)
    {
        spans.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
        var merged = new List<OpenSpan>();
        foreach (OpenSpan span in spans)
        {
            if (span.End <= span.Start)
            {
                continue;
            }

            int last = merged.Count - 1;
            if (last >= 0 && span.Start <= merged[last].End)
            {
                merged[last] = new OpenSpan(merged[last].Start, span.End > merged[last].End ? span.End : merged[last].End);
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged.AsReadOnly();
    }

    // Offset of a day and time of day from Sunday 00:00, in [0, 7 days): Saturday 24:00 is Sunday 00:00.
    private static TimeSpan WeekOffset(DayOfWeek day, TimeSpan time)
    {
        TimeSpan offset = TimeSpan.FromDays((int)day) + time;
        return offset >= Week ? offset - Week : offset;
    }

    // The part of [from, to) inside the open spans; from and to lie in the same UTC day (to may be its end).
    private static TimeSpan InSessions(DateTime from, DateTime to, IReadOnlyList<OpenSpan> week)
    {
        TimeSpan a = TimeSpan.FromDays((int)from.DayOfWeek) + from.TimeOfDay;
        TimeSpan b = a + (to - from);
        TimeSpan inside = TimeSpan.Zero;
        foreach (OpenSpan span in week)
        {
            TimeSpan start = span.Start > a ? span.Start : a;
            TimeSpan end = span.End < b ? span.End : b;
            if (end > start)
            {
                inside += end - start;
            }
        }

        return inside;
    }

    private readonly record struct OpenSpan(TimeSpan Start, TimeSpan End);
}
