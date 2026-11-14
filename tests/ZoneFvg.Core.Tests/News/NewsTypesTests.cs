using Xunit;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>The small types of the news module: records, guards, the week and the symbol currencies.</summary>
public class NewsTypesTests
{
    private static readonly DateTime T = Utc(2026, 11, 10, 13, 30);

    [Fact]
    [Trait("Req", "NWS-003")]
    public void NewsEvent_ValidValues_AreKept()
    {
        var newsEvent = new NewsEvent(T, "USD", NewsImpact.High, "CPI", "ForexFactory");

        Assert.Equal(T, newsEvent.TimeUtc);
        Assert.Equal("USD", newsEvent.Currency);
        Assert.Equal(NewsImpact.High, newsEvent.Impact);
        Assert.Equal("CPI", newsEvent.Title);
        Assert.Equal("ForexFactory", newsEvent.Source);
        Assert.True(newsEvent.HasClockTime);
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void NewsEvent_InvalidValues_Throw()
    {
        Assert.Throws<ArgumentException>(() => new NewsEvent(new DateTime(2026, 11, 10, 13, 30, 0, DateTimeKind.Unspecified), "USD", NewsImpact.High, "CPI", "S"));
        Assert.Throws<ArgumentException>(() => new NewsEvent(T, " ", NewsImpact.High, "CPI", "S"));
        Assert.Throws<ArgumentException>(() => new NewsEvent(T, null!, NewsImpact.High, "CPI", "S"));
        Assert.Throws<ArgumentException>(() => new NewsEvent(T, "USD", NewsImpact.High, null!, "S"));
        Assert.Throws<ArgumentException>(() => new NewsEvent(T, "USD", NewsImpact.High, "CPI", ""));
    }

    [Fact]
    public void NewsImpact_Members_InTheOrderOfThePrompt()
    {
        Assert.Equal(new[] { "High", "Medium", "Low", "Holiday", "Unknown" }, Enum.GetNames(typeof(NewsImpact)));
    }

    [Fact]
    [Trait("Req", "NWS-005")]
    public void NewsParseResult_Accepted_ChecksItsArguments()
    {
        NewsEvent[] one = { Event("USD", T) };
        DateTime start = Utc(2026, 11, 9, 0, 0);
        DateTime end = Utc(2026, 11, 16, 0, 0);

        Assert.Throws<ArgumentException>(() => NewsParseResult.Accepted(null!, start, end, "x"));
        Assert.Throws<ArgumentException>(() => NewsParseResult.Accepted(Array.Empty<NewsEvent>(), start, end, "x"));
        Assert.Throws<ArgumentException>(() => NewsParseResult.Accepted(new NewsEvent[] { null! }, start, end, "x"));
        Assert.Throws<ArgumentException>(() => NewsParseResult.Accepted(one, DateTime.SpecifyKind(start, DateTimeKind.Local), end, "x"));
        Assert.Throws<ArgumentException>(() => NewsParseResult.Accepted(one, start, DateTime.SpecifyKind(end, DateTimeKind.Unspecified), "x"));
        Assert.Throws<ArgumentException>(() => NewsParseResult.Accepted(one, start, start, "x"));
    }

    [Fact]
    [Trait("Req", "NWS-005")]
    public void NewsParseResult_CoversFromStartIncludedToEndExcluded()
    {
        NewsParseResult result = NewsParseResult.Accepted(new[] { Event("USD", T) }, Utc(2026, 11, 9, 0, 0), Utc(2026, 11, 16, 0, 0), null!);

        Assert.Equal(string.Empty, result.Reason);
        Assert.False(result.Covers(Utc(2026, 11, 8, 23, 59, 59)));
        Assert.True(result.Covers(Utc(2026, 11, 9, 0, 0)));
        Assert.True(result.Covers(Utc(2026, 11, 15, 23, 59, 59)));
        Assert.False(result.Covers(Utc(2026, 11, 16, 0, 0)));
    }

    [Fact]
    [Trait("Req", "NWS-005")]
    public void NewsParseResult_Rejected_CoversNothing()
    {
        NewsParseResult result = NewsParseResult.Rejected(null!);

        Assert.False(result.IsValid);
        Assert.Equal(string.Empty, result.Reason);
        Assert.Empty(result.Events);
        Assert.False(result.Covers(T));
    }

    [Fact]
    [Trait("Req", "NWS-006")]
    public void NewsFeed_ChecksItsArguments()
    {
        var feed = new NewsFeed(FeedUrl, new ForexFactoryParser());

        Assert.Equal(FeedUrl, feed.Url);
        Assert.Equal("ForexFactory", feed.Source);
        Assert.IsType<ForexFactoryParser>(feed.Parser);
        Assert.Throws<ArgumentException>(() => new NewsFeed(" ", new ForexFactoryParser()));
        Assert.Throws<ArgumentException>(() => new NewsFeed(null!, new ForexFactoryParser()));
        Assert.Throws<ArgumentNullException>(() => new NewsFeed(FeedUrl, null!));
        Assert.Throws<ArgumentException>(() => new NewsFeed(FeedUrl, new ListParser(" ", Array.Empty<NewsEvent>(), T, T)));
    }

    [Theory]
    [Trait("Req", "NWS-005")]
    [InlineData(2026, 11, 9, 0, 0)]
    [InlineData(2026, 11, 11, 12, 0)]
    [InlineData(2026, 11, 15, 23, 59)]
    public void NewsWeek_DaysOfOneWeek_StartOnMondayAndEndTheNextMonday(int year, int month, int day, int hour, int minute)
    {
        DateTime utc = Utc(year, month, day, hour, minute);

        Assert.Equal(Utc(2026, 11, 9, 0, 0), NewsWeek.StartUtc(utc));
        Assert.Equal(Utc(2026, 11, 16, 0, 0), NewsWeek.EndUtc(utc));
        Assert.Equal(DateTimeKind.Utc, NewsWeek.StartUtc(utc).Kind);
    }

    [Fact]
    [Trait("Req", "NWS-005")]
    public void NewsWeek_NotUtc_Throws()
    {
        Assert.Throws<ArgumentException>(() => NewsWeek.StartUtc(new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Local)));
    }

    [Theory]
    [Trait("Req", "NWS-001")]
    [InlineData("EURUSD", "EUR", "USD")]
    [InlineData("gbpusd.r", "GBP", "USD")]
    [InlineData("USDJPYm", "USD", "JPY")]
    [InlineData("AUDUSD_x", "AUD", "USD")]
    public void NewsSymbol_Currencies_AreTheFirstSixLetters(string symbol, string baseCurrency, string quoteCurrency)
    {
        Assert.Equal(baseCurrency, NewsSymbol.BaseCurrency(symbol));
        Assert.Equal(quoteCurrency, NewsSymbol.QuoteCurrency(symbol));
        Assert.Equal(baseCurrency + quoteCurrency, NewsSymbol.Pair(symbol));
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void NewsSymbol_TooShort_ThrowsNamingTheRule()
    {
        var exception = Assert.Throws<ArgumentException>(() => NewsSymbol.BaseCurrency("EURU"));

        Assert.Contains("six letters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "NWS-001")]
    public void NewsWindow_Contains_BothEnds()
    {
        var window = new NewsWindow(Utc(2026, 11, 10, 13, 0), Utc(2026, 11, 10, 14, 0));

        Assert.True(window.Contains(Utc(2026, 11, 10, 13, 0)));
        Assert.True(window.Contains(Utc(2026, 11, 10, 14, 0)));
        Assert.False(window.Contains(Utc(2026, 11, 10, 12, 59, 59)));
        Assert.False(window.Contains(Utc(2026, 11, 10, 14, 0, 1)));
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void NewsEventOrder_NullsFirstAndEveryFieldDecides()
    {
        NewsEventOrder order = NewsEventOrder.Instance;
        NewsEvent a = Event("USD", T);

        Assert.Equal(0, order.Compare(a, a));
        Assert.Equal(0, order.Compare(null, null));
        Assert.True(order.Compare(null, a) < 0);
        Assert.True(order.Compare(a, null) > 0);
        Assert.True(order.Compare(Event("USD", null), a) < 0);
        Assert.True(order.Compare(Event("EUR", T), a) < 0);
        Assert.True(order.Compare(a, Event("USD", T, NewsImpact.Low)) < 0);
        Assert.True(order.Compare(Event("USD", T, title: "A"), Event("USD", T, title: "B")) < 0);
        Assert.True(order.Compare(Event("USD", T, source: "A"), Event("USD", T, source: "B")) < 0);
        Assert.Equal(0, order.Compare(Event("USD", T), Event("USD", T)));
    }

    [Fact]
    [Trait("Req", "NWS-005")]
    public void HttpResult_KeepsItsValues()
    {
        var result = new HttpResult(true, 200, "body");

        Assert.True(result.Ok);
        Assert.Equal(200, result.Status);
        Assert.Equal("body", result.Body);
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void MemoryStore_RoundTrip()
    {
        IKeyValueStore store = new MemoryStore();

        Assert.Null(store.Get("news.x"));
        store.Set("news.x", "{}");
        Assert.Equal("{}", store.Get("news.x"));
    }
}
