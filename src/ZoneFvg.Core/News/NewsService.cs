using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.News;

/// <summary>
/// Owns the news data: refresh timing (NWS-006), validation and replacement of downloads
/// (NWS-005), the cache in the key-value store (NWS-007), the calendar status and its alert
/// (NWS-008), and the entry check (NWS-009). Server time is an argument of every call; the
/// service never reads a clock. In backtest mode it uses only the embedded history and never
/// touches the transport or the store (NWS-010).
/// </summary>
/// <remarks>
/// Refresh rule (NWS-006): the first <see cref="Refresh"/> of an instance is the start. At start
/// a source is fetched when its last attempt, read from the store, is at least
/// <see cref="NewsParameters.MinFetchIntervalMinutes"/> old or unknown; afterwards when it is at
/// least <see cref="NewsParameters.NewsRefreshHours"/> old. The attempt time is stored before the
/// request is sent, so a crash or restart during the request still counts it.
/// </remarks>
public sealed class NewsService
{
    /// <summary>Timeout of one feed request, in seconds.</summary>
    public const int HttpTimeoutSeconds = 10;

    private readonly NewsParameters _parameters;
    private readonly NewsFeed[] _feeds;
    private readonly CachedFeedData?[] _data;
    private readonly IKeyValueStore _store;
    private readonly IHttpTransport _http;
    private readonly bool _isBacktest;
    private readonly NewsParseResult? _history;
    private readonly IReadOnlyList<NewsEvent> _extraBlackouts;
    private bool _started;
    private bool _outageAlerted;
    private NewsCalendar? _calendar;
    private string? _calendarKey;

    /// <summary>Creates the service. Validates the parameters (NWS-004, NWS-011).</summary>
    /// <param name="parameters">News parameters.</param>
    /// <param name="feeds">Live feeds in a fixed order, unique by source name. At least one unless <paramref name="isBacktest"/>; ignored in backtests.</param>
    /// <param name="store">Key-value store for the cache and the attempt times (NWS-006, NWS-007). Not used in backtests.</param>
    /// <param name="http">HTTP transport for the feeds. Never called in backtests (NWS-010).</param>
    /// <param name="isBacktest">True in a backtest: events come only from <paramref name="embeddedHistory"/> (NWS-010).</param>
    /// <param name="embeddedHistory">Text of the embedded history file (NWS-010). Used only in backtests.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">A parameter is not accepted, no live feed is given, or two feeds share a source name.</exception>
    public NewsService(NewsParameters parameters, IReadOnlyList<NewsFeed> feeds, IKeyValueStore store, IHttpTransport http, bool isBacktest, string embeddedHistory)
    {
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        if (feeds == null)
        {
            throw new ArgumentNullException(nameof(feeds));
        }

        _store = store ?? throw new ArgumentNullException(nameof(store));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (embeddedHistory == null)
        {
            throw new ArgumentNullException(nameof(embeddedHistory));
        }

        parameters.Validate();
        _extraBlackouts = ExtraBlackoutsParser.Parse(parameters.ExtraBlackouts).Events;
        _isBacktest = isBacktest;
        if (isBacktest)
        {
            _feeds = Array.Empty<NewsFeed>();
            _history = EmbeddedHistoryProvider.Parse(embeddedHistory);
        }
        else
        {
            _feeds = CheckedFeeds(feeds);
        }

        _data = new CachedFeedData?[_feeds.Length];
    }

    /// <summary>
    /// Loads the cache at start, fetches every source that is due, and recomputes the status
    /// (NWS-005 to NWS-008, NWS-010). Call at start and on the minute timer, between bars.
    /// </summary>
    /// <param name="serverTimeUtc">Broker server time, kind UTC.</param>
    /// <returns>Status, alert flags and per-source reports.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="serverTimeUtc"/> is not UTC.</exception>
    public NewsUpdate Refresh(DateTime serverTimeUtc)
    {
        NewsGuard.RequireUtc(serverTimeUtc, nameof(serverTimeUtc));
        var reports = new List<NewsFeedReport>();
        bool isStart = !_started;
        _started = true;
        if (_isBacktest)
        {
            if (isStart)
            {
                reports.Add(HistoryReport());
            }
        }
        else
        {
            for (int i = 0; i < _feeds.Length; i++)
            {
                if (isStart)
                {
                    reports.Add(LoadCache(i));
                }

                NewsFeedReport? fetch = FetchIfDue(i, serverTimeUtc, isStart);
                if (fetch != null)
                {
                    reports.Add(fetch);
                }
            }
        }

        NewsStatus status = StatusAt(serverTimeUtc);
        bool alert = false;
        bool recovered = false;
        if (status == NewsStatus.Unavailable && !_outageAlerted)
        {
            alert = true;
            _outageAlerted = true;
        }
        else if (status == NewsStatus.Available && _outageAlerted)
        {
            recovered = true;
            _outageAlerted = false;
        }

        string? previousKey = _calendarKey;
        NewsCalendar calendar = CalendarAt(serverTimeUtc);
        bool changed = !string.Equals(previousKey, _calendarKey, StringComparison.Ordinal);
        return new NewsUpdate(status, alert, recovered, reports, calendar, changed);
    }

    /// <summary>
    /// Calendar status at the instant (NWS-008): Available when at least one source has trusted
    /// data that covers it. Live data is trusted when it is at most NewsMaxCacheHours old and not
    /// dated after the instant (NWS-007). In backtests: when the embedded history is valid and
    /// covers the instant.
    /// </summary>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <returns>The status.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="nowUtc"/> is not UTC.</exception>
    public NewsStatus StatusAt(DateTime nowUtc)
    {
        NewsGuard.RequireUtc(nowUtc, nameof(nowUtc));
        if (_isBacktest)
        {
            return _history!.Covers(nowUtc) ? NewsStatus.Available : NewsStatus.Unavailable;
        }

        for (int i = 0; i < _data.Length; i++)
        {
            if (IsTrusted(_data[i], nowUtc))
            {
                return NewsStatus.Available;
            }
        }

        return NewsStatus.Unavailable;
    }

    /// <summary>
    /// The calendar at the instant: events of the sources trusted then, merged (NWS-002), plus
    /// the manual blackouts (NWS-011). In backtests: the embedded history plus the blackouts.
    /// </summary>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <returns>The calendar.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="nowUtc"/> is not UTC.</exception>
    public NewsCalendar CalendarAt(DateTime nowUtc)
    {
        NewsGuard.RequireUtc(nowUtc, nameof(nowUtc));
        var sources = new List<IReadOnlyList<NewsEvent>>();
        var key = new StringBuilder();
        if (_isBacktest)
        {
            sources.Add(_history!.Events);
        }
        else
        {
            for (int i = 0; i < _data.Length; i++)
            {
                CachedFeedData? data = _data[i];
                if (data != null && IsTrusted(data, nowUtc))
                {
                    sources.Add(data.Events);
                    key.Append(i.ToString(CultureInfo.InvariantCulture)).Append('@')
                        .Append(data.FetchedUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append(';');
                }
            }
        }

        string calendarKey = key.ToString();
        if (_calendar == null || !string.Equals(calendarKey, _calendarKey, StringComparison.Ordinal))
        {
            _calendar = new NewsCalendar(sources, _extraBlackouts, _parameters.NewsBeforeMin, _parameters.NewsAfterMin);
            _calendarKey = calendarKey;
        }

        return _calendar;
    }

    /// <summary>
    /// The news checks for a new entry, in this order (NWS-008, NWS-009): the calendar must be
    /// available (NewsFailMode Block); the current time must not be inside a window of the
    /// symbol; none of the three FVG bars, each from its open time to its open time plus
    /// 15 minutes, may overlap a window.
    /// </summary>
    /// <param name="symbol">Symbol name.</param>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <param name="fvg">The gap of the signal.</param>
    /// <returns>Allowed, or the first failing reason.</returns>
    /// <exception cref="ArgumentNullException">The gap is null.</exception>
    /// <exception cref="ArgumentException">The symbol is not valid or a time is not UTC.</exception>
    public NewsDecision CheckEntry(string symbol, DateTime nowUtc, Fvg fvg)
    {
        if (fvg == null)
        {
            throw new ArgumentNullException(nameof(fvg));
        }

        NewsSymbol.Pair(symbol);
        if (StatusAt(nowUtc) == NewsStatus.Unavailable)
        {
            return new NewsDecision(NewsBlockReason.CalendarUnavailable, null);
        }

        NewsCalendar calendar = CalendarAt(nowUtc);
        if (calendar.IsInWindow(symbol, nowUtc))
        {
            return new NewsDecision(NewsBlockReason.InsideWindow, null);
        }

        TimeSpan barLength = TimeSpan.FromMinutes(15);
        foreach (DateTime open in new[] { fvg.C3OpenUtc, fvg.C2OpenUtc, fvg.C1OpenUtc })
        {
            if (calendar.OverlapsWindow(symbol, open, open + barLength))
            {
                return new NewsDecision(NewsBlockReason.FvgBarInWindow, open);
            }
        }

        return new NewsDecision(NewsBlockReason.None, null);
    }

    private static NewsFeed[] CheckedFeeds(IReadOnlyList<NewsFeed> feeds)
    {
        if (feeds.Count == 0)
        {
            throw new ArgumentException("At least one live feed is needed outside backtests.", nameof(feeds));
        }

        var copy = new NewsFeed[feeds.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = feeds[i] ?? throw new ArgumentException("A feed must not be null.", nameof(feeds));
            if (!names.Add(copy[i].Source))
            {
                throw new ArgumentException("Two feeds share the source name \"" + copy[i].Source + "\".", nameof(feeds));
            }
        }

        return copy;
    }

    private bool IsTrusted(CachedFeedData? data, DateTime nowUtc)
    {
        return data != null
            && data.FetchedUtc <= nowUtc
            && nowUtc - data.FetchedUtc <= TimeSpan.FromHours(_parameters.NewsMaxCacheHours)
            && data.CoverageStartUtc <= nowUtc
            && nowUtc < data.CoverageEndUtc;
    }

    private NewsFeedReport HistoryReport()
    {
        NewsParseResult history = _history!;
        return history.IsValid
            ? new NewsFeedReport(EmbeddedHistoryProvider.SourceName, NewsFeedOutcome.HistoryLoaded, history.Reason, history.Events)
            : new NewsFeedReport(EmbeddedHistoryProvider.SourceName, NewsFeedOutcome.HistoryRejected, history.Reason, Array.Empty<NewsEvent>());
    }

    private NewsFeedReport LoadCache(int index)
    {
        string source = _feeds[index].Source;
        string? text = _store.Get(NewsCache.DataKey(source));
        if (text == null)
        {
            return new NewsFeedReport(source, NewsFeedOutcome.CacheMissing, "No cached data.", Array.Empty<NewsEvent>());
        }

        string? error = NewsCache.ReadData(text, out CachedFeedData? data);
        if (error != null)
        {
            return new NewsFeedReport(source, NewsFeedOutcome.CacheUnreadable, "Cached data ignored: " + error, Array.Empty<NewsEvent>());
        }

        _data[index] = data;
        return new NewsFeedReport(source, NewsFeedOutcome.CacheLoaded, "Cached data fetched at " + Time(data!.FetchedUtc) + ".", data.Events);
    }

    private NewsFeedReport? FetchIfDue(int index, DateTime nowUtc, bool isStart)
    {
        NewsFeed feed = _feeds[index];
        string? attemptText = _store.Get(NewsCache.AttemptKey(feed.Source));
        NewsFeedReport? unreadable = null;
        if (attemptText != null)
        {
            string? error = NewsCache.ReadAttempt(attemptText, out DateTime lastAttempt);
            if (error == null)
            {
                if (lastAttempt > nowUtc)
                {
                    // Reported once, at start; afterwards the source simply waits.
                    return !isStart ? null : new NewsFeedReport(feed.Source, NewsFeedOutcome.AttemptTimeInFuture, "Last attempt stored as " + Time(lastAttempt) + ", after server time " + Time(nowUtc) + ".", Array.Empty<NewsEvent>());
                }

                TimeSpan interval = isStart ? TimeSpan.FromMinutes(NewsParameters.MinFetchIntervalMinutes) : TimeSpan.FromHours(_parameters.NewsRefreshHours);
                if (nowUtc - lastAttempt < interval)
                {
                    return null;
                }
            }
            else
            {
                unreadable = new NewsFeedReport(feed.Source, NewsFeedOutcome.CacheUnreadable, "Stored attempt time ignored: " + error, Array.Empty<NewsEvent>());
            }
        }

        NewsFeedReport result = Fetch(index, nowUtc);
        return unreadable == null ? result : new NewsFeedReport(result.Source, result.Outcome, unreadable.Detail + " " + result.Detail, result.Events);
    }

    private NewsFeedReport Fetch(int index, DateTime nowUtc)
    {
        NewsFeed feed = _feeds[index];
        _store.Set(NewsCache.AttemptKey(feed.Source), NewsCache.WriteAttempt(nowUtc));
        HttpResult? response = _http.Get(feed.Url, HttpTimeoutSeconds);
        if (response == null || !response.Ok)
        {
            string status = response == null ? "no result" : "status " + response.Status.ToString(CultureInfo.InvariantCulture);
            return new NewsFeedReport(feed.Source, NewsFeedOutcome.DownloadRejected, "Download failed (" + status + "); earlier data kept.", Array.Empty<NewsEvent>());
        }

        NewsParseResult parsed = feed.Parser.Parse(response.Body ?? string.Empty, nowUtc);
        if (!parsed.IsValid || !parsed.Covers(nowUtc))
        {
            string reason = parsed.IsValid ? "the document does not cover the current time." : parsed.Reason;
            return new NewsFeedReport(feed.Source, NewsFeedOutcome.DownloadRejected, "Download rejected: " + reason + " Earlier data kept.", Array.Empty<NewsEvent>());
        }

        var data = new CachedFeedData(parsed.Events, nowUtc, parsed.CoverageStartUtc, parsed.CoverageEndUtc);
        _data[index] = data;
        _store.Set(NewsCache.DataKey(feed.Source), NewsCache.WriteData(data));
        return new NewsFeedReport(feed.Source, NewsFeedOutcome.DownloadAccepted, "Download accepted: " + parsed.Reason + ".", parsed.Events);
    }

    private static string Time(DateTime utc)
    {
        return utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
    }
}
