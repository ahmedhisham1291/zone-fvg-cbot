using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.News;

/// <summary>
/// The news calendar at one moment: the merged events of the trusted sources plus the manual
/// blackouts, and the news windows they create for each symbol (NWS-001, NWS-002, NWS-011,
/// NWS-012). Immutable apart from an internal cache of computed windows.
/// </summary>
public sealed class NewsCalendar
{
    /// <summary>Currency code of events that concern every symbol, compared ignoring case (NWS-001, owner decision of 2026-11-14).</summary>
    public const string AllCurrencies = "ALL";

    private readonly int _beforeMinutes;
    private readonly int _afterMinutes;
    private readonly List<NewsEvent> _windowEvents;
    private readonly Dictionary<string, NewsWindow[]> _windows = new Dictionary<string, NewsWindow[]>(StringComparer.Ordinal);

    /// <summary>
    /// Builds the calendar. <see cref="Events"/> holds the source events merged with
    /// <see cref="NewsMerge.Merge(IReadOnlyList{IReadOnlyList{NewsEvent}})"/> for logging; the
    /// windows come from <see cref="NewsMerge.WindowEvents"/>, so every event keeps its own
    /// window (NWS-002). The manual blackouts are added unmerged (NWS-011).
    /// </summary>
    /// <param name="sources">Events of each trusted source, in a fixed source order. May be empty.</param>
    /// <param name="extraBlackouts">Events from the ExtraBlackouts parameter (NWS-011).</param>
    /// <param name="beforeMinutes">NewsBeforeMin: minutes a window starts before its event (NWS-001).</param>
    /// <param name="afterMinutes">NewsAfterMin: minutes a window ends after its event (NWS-001).</param>
    /// <exception cref="ArgumentNullException">A list or an inner list is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A minute value is negative.</exception>
    public NewsCalendar(IReadOnlyList<IReadOnlyList<NewsEvent>> sources, IReadOnlyList<NewsEvent> extraBlackouts, int beforeMinutes, int afterMinutes)
    {
        if (sources == null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        if (extraBlackouts == null)
        {
            throw new ArgumentNullException(nameof(extraBlackouts));
        }

        if (beforeMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(beforeMinutes), "NewsBeforeMin must not be negative.");
        }

        if (afterMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterMinutes), "NewsAfterMin must not be negative.");
        }

        _beforeMinutes = beforeMinutes;
        _afterMinutes = afterMinutes;

        foreach (NewsEvent blackout in extraBlackouts)
        {
            if (blackout == null)
            {
                throw new ArgumentNullException(nameof(extraBlackouts), "An event must not be null.");
            }
        }

        var events = new List<NewsEvent>(NewsMerge.Merge(sources));
        events.AddRange(extraBlackouts);
        events.Sort(NewsEventOrder.Instance);
        Events = events;
        EventsWithoutWindow = events.FindAll(e => !e.HasClockTime);

        var windowEvents = new List<NewsEvent>(NewsMerge.WindowEvents(sources));
        windowEvents.AddRange(extraBlackouts);
        windowEvents.Sort(NewsEventOrder.Instance);
        _windowEvents = windowEvents;
    }

    /// <summary>All events of the calendar, matched events merged, in <see cref="NewsEventOrder"/> order; for logging (NWS-002).</summary>
    public IReadOnlyList<NewsEvent> Events { get; }

    /// <summary>
    /// Events without a clock time: no time, or a holiday. They create no window and are listed
    /// here for the log (NWS-012).
    /// </summary>
    public IReadOnlyList<NewsEvent> EventsWithoutWindow { get; }

    /// <summary>
    /// The news windows of a symbol, in time order, merged where they overlap or touch (NWS-001).
    /// Every high-impact event with a clock time whose currency is the symbol's base or quote, or
    /// "All", opens its own window; an event matched with a high one in another feed counts as
    /// high (NWS-002).
    /// </summary>
    /// <param name="symbol">Symbol name, for example EURUSD.</param>
    /// <returns>The windows.</returns>
    /// <exception cref="ArgumentException">The symbol does not start with six letters.</exception>
    internal IReadOnlyList<NewsWindow> WindowsFor(string symbol)
    {
        return Windows(symbol);
    }

    /// <summary>True when the instant is inside a news window of the symbol, both ends included (NWS-001, NWS-009).</summary>
    /// <param name="symbol">Symbol name.</param>
    /// <param name="utc">The instant, kind UTC.</param>
    /// <returns>True when inside a window.</returns>
    /// <exception cref="ArgumentException">The symbol is not valid or the kind of <paramref name="utc"/> is not UTC.</exception>
    public bool IsInWindow(string symbol, DateTime utc)
    {
        NewsGuard.RequireUtc(utc, nameof(utc));
        NewsWindow[] windows = Windows(symbol);
        int index = FirstEndingAtOrAfter(windows, utc);
        return index < windows.Length && windows[index].Contains(utc);
    }

    /// <summary>
    /// True when the period from <paramref name="fromUtc"/> (included) to <paramref name="toUtc"/>
    /// (excluded) shares an instant with a news window of the symbol (NWS-009). A bar is such a
    /// period: its open time to its close time.
    /// </summary>
    /// <param name="symbol">Symbol name.</param>
    /// <param name="fromUtc">Start of the period, included, kind UTC.</param>
    /// <param name="toUtc">End of the period, excluded, kind UTC, after the start.</param>
    /// <returns>True when the period overlaps a window.</returns>
    /// <exception cref="ArgumentException">The symbol is not valid, a time is not UTC, or the period is empty.</exception>
    public bool OverlapsWindow(string symbol, DateTime fromUtc, DateTime toUtc)
    {
        NewsGuard.RequireUtc(fromUtc, nameof(fromUtc));
        NewsGuard.RequireUtc(toUtc, nameof(toUtc));
        if (toUtc <= fromUtc)
        {
            throw new ArgumentException("The period must end after it starts.", nameof(toUtc));
        }

        NewsWindow[] windows = Windows(symbol);
        int index = FirstEndingAtOrAfter(windows, fromUtc);
        return index < windows.Length && windows[index].StartUtc < toUtc;
    }

    /// <summary>
    /// Start of the first news window of the symbol that starts at or after the instant, or null
    /// when the calendar holds none (NWS-001). Used by the execution layer to shorten an order's
    /// expiry (spec 6.1).
    /// </summary>
    /// <param name="symbol">Symbol name.</param>
    /// <param name="afterUtc">The instant, kind UTC.</param>
    /// <returns>The window start, or null.</returns>
    /// <exception cref="ArgumentException">The symbol is not valid or the kind of <paramref name="afterUtc"/> is not UTC.</exception>
    public DateTime? NextWindowStart(string symbol, DateTime afterUtc)
    {
        NewsGuard.RequireUtc(afterUtc, nameof(afterUtc));
        NewsWindow[] windows = Windows(symbol);
        int low = 0;
        int high = windows.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (windows[middle].StartUtc >= afterUtc)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low < windows.Length ? windows[low].StartUtc : null;
    }

    private static int FirstEndingAtOrAfter(NewsWindow[] windows, DateTime utc)
    {
        // Merged windows are disjoint and sorted, so their ends increase too.
        int low = 0;
        int high = windows.Length;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (windows[middle].EndUtc >= utc)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    private NewsWindow[] Windows(string symbol)
    {
        string pair = NewsSymbol.Pair(symbol);
        if (_windows.TryGetValue(pair, out NewsWindow[]? cached))
        {
            return cached;
        }

        string baseCurrency = NewsSymbol.BaseCurrency(pair);
        string quoteCurrency = NewsSymbol.QuoteCurrency(pair);
        TimeSpan before = TimeSpan.FromMinutes(_beforeMinutes);
        TimeSpan after = TimeSpan.FromMinutes(_afterMinutes);
        var raw = new List<NewsWindow>();
        foreach (NewsEvent newsEvent in _windowEvents)
        {
            string currency = newsEvent.Currency.Trim().ToUpperInvariant();
            bool relevant = currency == baseCurrency || currency == quoteCurrency || currency == AllCurrencies;
            if (newsEvent.HasClockTime && newsEvent.Impact == NewsImpact.High && relevant)
            {
                DateTime time = newsEvent.TimeUtc!.Value;
                raw.Add(new NewsWindow(time - before, time + after));
            }
        }

        NewsWindow[] windows = MergeOverlapping(raw);
        _windows.Add(pair, windows);
        return windows;
    }

    private static NewsWindow[] MergeOverlapping(List<NewsWindow> raw)
    {
        // Events are sorted by time and every window has the same length, so both the starts and
        // the ends are in ascending order and a merged window always ends at the later window's end.
        var merged = new List<NewsWindow>(raw.Count);
        foreach (NewsWindow window in raw)
        {
            int last = merged.Count - 1;
            if (last >= 0 && window.StartUtc <= merged[last].EndUtc)
            {
                merged[last] = new NewsWindow(merged[last].StartUtc, window.EndUtc);
            }
            else
            {
                merged.Add(window);
            }
        }

        return merged.ToArray();
    }
}
