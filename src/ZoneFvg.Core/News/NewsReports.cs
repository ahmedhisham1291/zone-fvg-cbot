using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.News;

/// <summary>What happened to one source during <see cref="NewsService.Refresh"/>.</summary>
public enum NewsFeedOutcome
{
    /// <summary>Cached data was read from the store at start (NWS-007).</summary>
    CacheLoaded,

    /// <summary>The store held no cached data at start (NWS-007).</summary>
    CacheMissing,

    /// <summary>The cached data or the stored attempt time could not be read; it is ignored (NWS-006, NWS-007).</summary>
    CacheUnreadable,

    /// <summary>The download was accepted and replaced the earlier data (NWS-005).</summary>
    DownloadAccepted,

    /// <summary>The download failed or was rejected; earlier data is kept (NWS-005).</summary>
    DownloadRejected,

    /// <summary>The download was rejected as a whole because an event is malformed; earlier data is kept and an alert is due (NWS-005).</summary>
    DownloadMalformed,

    /// <summary>At start, the stored time of the last attempt is later than server time. The source is not fetched until the interval has passed from that time (NWS-006).</summary>
    AttemptTimeInFuture,

    /// <summary>The embedded history was loaded (NWS-010).</summary>
    HistoryLoaded,

    /// <summary>The embedded history was rejected; the calendar is unavailable in the backtest (NWS-010).</summary>
    HistoryRejected,
}

/// <summary>Report of one source for the journal (spec 6.2: each feed's events are logged).</summary>
public sealed class NewsFeedReport
{
    internal NewsFeedReport(string source, NewsFeedOutcome outcome, string detail, IReadOnlyList<NewsEvent> events)
    {
        Source = source;
        Outcome = outcome;
        Detail = detail;
        Events = events;
    }

    /// <summary>Name of the source.</summary>
    public string Source { get; }

    /// <summary>What happened.</summary>
    public NewsFeedOutcome Outcome { get; }

    /// <summary>Reason or summary in plain words.</summary>
    public string Detail { get; }

    /// <summary>The events that were loaded or accepted; empty otherwise.</summary>
    public IReadOnlyList<NewsEvent> Events { get; }
}

/// <summary>Result of one <see cref="NewsService.Refresh"/> call (NWS-005 to NWS-008).</summary>
public sealed class NewsUpdate
{
    internal NewsUpdate(NewsStatus status, bool alertRaised, bool recovered, IReadOnlyList<NewsFeedReport> reports, NewsCalendar calendar, bool calendarChanged)
    {
        bool malformed = false;
        foreach (NewsFeedReport report in reports)
        {
            malformed |= report.Outcome == NewsFeedOutcome.DownloadMalformed;
        }

        MalformedDownloadAlert = malformed;

        Status = status;
        AlertRaised = alertRaised;
        Recovered = recovered;
        Reports = reports;
        Calendar = calendar;
        CalendarChanged = calendarChanged;
    }

    /// <summary>Calendar status at the refresh time (NWS-008).</summary>
    public NewsStatus Status { get; }

    /// <summary>True exactly once per outage: on the first refresh that finds the calendar unavailable (NWS-008).</summary>
    public bool AlertRaised { get; }

    /// <summary>
    /// True when a download in this refresh was rejected because an event is malformed. The
    /// caller sends an alert with the report's detail (NWS-005).
    /// </summary>
    public bool MalformedDownloadAlert { get; }

    /// <summary>True on the first refresh that finds the calendar available again after an alerted outage (NWS-008).</summary>
    public bool Recovered { get; }

    /// <summary>One report per source that was loaded, fetched or found unreadable during this call; empty when nothing happened.</summary>
    public IReadOnlyList<NewsFeedReport> Reports { get; }

    /// <summary>The calendar at the refresh time.</summary>
    public NewsCalendar Calendar { get; }

    /// <summary>True when the calendar differs from the one of the previous refresh, so its events without a window should be logged again (NWS-012).</summary>
    public bool CalendarChanged { get; }
}

/// <summary>Why the news filter blocks an entry (NWS-008, NWS-009, NWS-014).</summary>
public enum NewsBlockReason
{
    /// <summary>Not blocked.</summary>
    None,

    /// <summary>The calendar is unavailable and NewsFailMode is Block (NWS-008).</summary>
    CalendarUnavailable,

    /// <summary>The current time is inside a news window of the symbol (NWS-009).</summary>
    InsideWindow,

    /// <summary>One of the three FVG bars overlaps a news window of the symbol (NWS-009).</summary>
    FvgBarInWindow,

    /// <summary>The confirming bar of a breakout signal overlaps a news window of the symbol (NWS-014).</summary>
    BreakoutBarInWindow,
}

/// <summary>Decision of <see cref="NewsService.CheckEntry"/> and <see cref="NewsService.CheckBreakoutEntry"/> (NWS-008, NWS-009, NWS-014).</summary>
public sealed class NewsDecision
{
    internal NewsDecision(NewsBlockReason reason, DateTime? barOpenUtc)
    {
        Reason = reason;
        BarOpenUtc = barOpenUtc;
    }

    /// <summary>True when the news filter allows the entry.</summary>
    public bool IsAllowed => Reason == NewsBlockReason.None;

    /// <summary>The first failing check, or <see cref="NewsBlockReason.None"/>.</summary>
    public NewsBlockReason Reason { get; }

    /// <summary>Open time of the first FVG bar (c3, c2, c1 order) or of the breakout's confirming bar that overlaps a window; null otherwise.</summary>
    public DateTime? BarOpenUtc { get; }
}
