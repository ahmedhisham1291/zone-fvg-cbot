using System.Text;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;

namespace ZoneFvg.Core.Tests;

/// <summary>Shared data for the news tests (WP05). No network, no clock: every time is a constant.</summary>
public static class NewsTestData
{
    /// <summary>Address given to the Forex Factory feed in tests. Never contacted: the transport is a fake.</summary>
    public const string FeedUrl = "https://feed.invalid/ff_calendar_thisweek.json";

    /// <summary>Wednesday of the sample week, 2026-11-11 12:00 UTC. The sample's Monday-based UTC week is 2026-11-09 to 2026-11-16.</summary>
    public static readonly DateTime SampleWednesday = Utc(2026, 11, 11, 12, 0);

    /// <summary>The owner's real Forex Factory export, unedited.</summary>
    public static string Sample => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "News", "forexfactory-thisweek.json"), Encoding.UTF8);

    public static DateTime Utc(int year, int month, int day, int hour, int minute, int second = 0)
    {
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
    }

    public static NewsEvent Event(string currency, DateTime? time, NewsImpact impact = NewsImpact.High, string title = "Event", string source = "First")
    {
        return new NewsEvent(time, currency, impact, title, source);
    }

    public static NewsCalendar Calendar(params NewsEvent[] events)
    {
        return new NewsCalendar(new IReadOnlyList<NewsEvent>[] { events }, Array.Empty<NewsEvent>(), 30, 30);
    }

    public static NewsParameters Parameters(string extraBlackouts = "")
    {
        return new NewsParameters { ExtraBlackouts = extraBlackouts };
    }

    public static NewsFeed ForexFactoryFeed()
    {
        return new NewsFeed(FeedUrl, new ForexFactoryParser());
    }

    public static NewsService LiveService(MemoryStore store, FakeTransport http, NewsParameters? parameters = null)
    {
        return new NewsService(parameters ?? Parameters(), new[] { ForexFactoryFeed() }, store, http, false, string.Empty);
    }

    public static HttpResult Ok(string body)
    {
        return new HttpResult(true, 200, body);
    }

    public static HttpResult Failed(int status = 503)
    {
        return new HttpResult(false, status, string.Empty);
    }

    /// <summary>A one-event Forex Factory document with the real export's structure, for edge cases the sample does not contain.</summary>
    public static string OneEventDocument(string title, string country, string date, string impact)
    {
        return "[\n  {\n    \"title\": \"" + title + "\",\n    \"country\": \"" + country + "\",\n    \"date\": \"" + date
            + "\",\n    \"impact\": \"" + impact + "\",\n    \"forecast\": \"\",\n    \"previous\": \"\"\n  }\n]";
    }
}

/// <summary>In-memory key-value store that counts its calls.</summary>
public sealed class MemoryStore : IKeyValueStore
{
    public Dictionary<string, string> Values { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public int Gets { get; private set; }

    public int Sets { get; private set; }

    public string? Get(string key)
    {
        Gets++;
        return Values.TryGetValue(key, out string? value) ? value : null;
    }

    public void Set(string key, string value)
    {
        Sets++;
        Values[key] = value;
    }
}

/// <summary>Scripted HTTP transport that counts its calls. Never touches a network.</summary>
public sealed class FakeTransport : IHttpTransport
{
    public FakeTransport(HttpResult result)
    {
        Respond = _ => result;
    }

    public Func<string, HttpResult> Respond { get; set; }

    public int Calls { get; private set; }

    public List<string> Urls { get; } = new List<string>();

    public List<int> Timeouts { get; } = new List<int>();

    public HttpResult Get(string url, int timeoutSeconds)
    {
        Calls++;
        Urls.Add(url);
        Timeouts.Add(timeoutSeconds);
        return Respond(url);
    }
}

/// <summary>
/// A second source for the merge and fail-safe tests. Its events are given directly as a list;
/// it accepts any body equal to "ok" and rejects everything else. It does not stand for any real
/// feed format.
/// </summary>
public sealed class ListParser : INewsFeedParser
{
    private readonly IReadOnlyList<NewsEvent> _events;
    private readonly DateTime _coverageStart;
    private readonly DateTime _coverageEnd;

    public ListParser(string source, IReadOnlyList<NewsEvent> events, DateTime coverageStart, DateTime coverageEnd)
    {
        Source = source;
        _events = events;
        _coverageStart = coverageStart;
        _coverageEnd = coverageEnd;
    }

    public string Source { get; }

    public NewsParseResult Parse(string body, DateTime nowUtc)
    {
        return body == "ok"
            ? NewsParseResult.Accepted(_events, _coverageStart, _coverageEnd, "list")
            : NewsParseResult.Rejected("not ok");
    }
}
