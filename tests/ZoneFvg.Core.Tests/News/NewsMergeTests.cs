using Xunit;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-002: an event is high impact if either feed rates it high; events of the two feeds with
/// the same currency and times at most 5 minutes apart are one event, at the earlier time. The
/// second feed's events are given directly as a list (owner decision of 2026-11-14: Forex
/// Factory alone in production; the calendar stays open for a second source).
/// </summary>
public class NewsMergeTests
{
    private static readonly DateTime T = Utc(2026, 11, 10, 13, 30);

    private static NewsEvent First(string currency, DateTime? time, NewsImpact impact = NewsImpact.High, string title = "First title")
    {
        return Event(currency, time, impact, title, "First");
    }

    private static NewsEvent Second(string currency, DateTime? time, NewsImpact impact = NewsImpact.High, string title = "Second title")
    {
        return Event(currency, time, impact, title, "Second");
    }

    [Theory]
    [Trait("Req", "NWS-002")]
    [InlineData(NewsImpact.High, NewsImpact.Medium)]
    [InlineData(NewsImpact.Medium, NewsImpact.High)]
    [InlineData(NewsImpact.High, NewsImpact.Low)]
    [InlineData(NewsImpact.Unknown, NewsImpact.High)]
    [InlineData(NewsImpact.High, NewsImpact.Holiday)]
    [InlineData(NewsImpact.High, NewsImpact.High)]
    public void Merge_HighInEitherFeed_GivesOneHighEvent(NewsImpact first, NewsImpact second)
    {
        NewsEvent merged = Assert.Single(NewsMerge.Merge(new[] { First("USD", T, first) }, new[] { Second("USD", T, second) }));

        Assert.Equal(NewsImpact.High, merged.Impact);
        Assert.Equal(T, merged.TimeUtc);
        Assert.Equal("First+Second", merged.Source);
    }

    [Theory]
    [Trait("Req", "NWS-002")]
    [InlineData(NewsImpact.Medium, NewsImpact.Low, NewsImpact.Medium)]
    [InlineData(NewsImpact.Low, NewsImpact.Medium, NewsImpact.Medium)]
    [InlineData(NewsImpact.Low, NewsImpact.Unknown, NewsImpact.Low)]
    [InlineData(NewsImpact.Unknown, NewsImpact.Holiday, NewsImpact.Unknown)]
    [InlineData(NewsImpact.Holiday, NewsImpact.Holiday, NewsImpact.Holiday)]
    public void Merge_NeitherHigh_KeepsTheStrongerRating(NewsImpact first, NewsImpact second, NewsImpact expected)
    {
        NewsEvent merged = Assert.Single(NewsMerge.Merge(new[] { First("USD", T, first) }, new[] { Second("USD", T, second) }));

        Assert.Equal(expected, merged.Impact);
    }

    [Theory]
    [Trait("Req", "NWS-002")]
    [InlineData(0, 1)]
    [InlineData(240, 1)]
    [InlineData(299, 1)]
    [InlineData(300, 1)]
    [InlineData(-300, 1)]
    [InlineData(301, 2)]
    [InlineData(-301, 2)]
    [InlineData(360, 2)]
    [InlineData(-360, 2)]
    public void Merge_TimesApart_OneEventUpToFiveMinutes(int secondsLater, int expectedCount)
    {
        IReadOnlyList<NewsEvent> merged = NewsMerge.Merge(new[] { First("USD", T) }, new[] { Second("USD", T.AddSeconds(secondsLater)) });

        Assert.Equal(expectedCount, merged.Count);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Merge_SecondFeedEarlier_UsesItsTimeAndTitle()
    {
        NewsEvent merged = Assert.Single(NewsMerge.Merge(new[] { First("USD", T, NewsImpact.Medium) }, new[] { Second("USD", T.AddMinutes(-4)) }));

        Assert.Equal(T.AddMinutes(-4), merged.TimeUtc);
        Assert.Equal("Second title", merged.Title);
        Assert.Equal(NewsImpact.High, merged.Impact);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Merge_SameTime_TitleOfTheFirstFeed()
    {
        NewsEvent merged = Assert.Single(NewsMerge.Merge(new[] { First("usd", T) }, new[] { Second("USD", T) }));

        Assert.Equal("First title", merged.Title);
        Assert.Equal("USD", merged.Currency);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Merge_SixMinutesApart_StayTwoEventsUnchanged()
    {
        NewsEvent first = First("USD", T);
        NewsEvent second = Second("USD", T.AddMinutes(6));

        IReadOnlyList<NewsEvent> merged = NewsMerge.Merge(new[] { first }, new[] { second });

        Assert.Equal(new[] { first, second }, merged);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Merge_DifferentCurrencies_StayTwo()
    {
        IReadOnlyList<NewsEvent> merged = NewsMerge.Merge(new[] { First("USD", T) }, new[] { Second("EUR", T) });

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Merge_TwoEventsOfOneFeed_AreNotMergedWithEachOther()
    {
        IReadOnlyList<NewsEvent> merged = NewsMerge.Merge(new[] { First("USD", T, title: "CPI m/m"), First("USD", T, title: "Core CPI m/m") }, Array.Empty<NewsEvent>());

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Merge_EventsLinkedThroughTheOtherFeed_BecomeOneAtTheEarliestTime()
    {
        // First 13:30 matches Second 13:33, which matches First 13:36: one group, time 13:30.
        IReadOnlyList<NewsEvent> merged = NewsMerge.Merge(
            new[] { First("USD", T, NewsImpact.Medium), First("USD", T.AddMinutes(6), NewsImpact.Low) },
            new[] { Second("USD", T.AddMinutes(3), NewsImpact.High) });

        NewsEvent single = Assert.Single(merged);
        Assert.Equal(T, single.TimeUtc);
        Assert.Equal(NewsImpact.High, single.Impact);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Merge_TwoPairsAtTheSameTime_BecomeOneEvent()
    {
        // CPI and Core CPI in both feeds at 13:30: every pair matches, so one event.
        IReadOnlyList<NewsEvent> merged = NewsMerge.Merge(
            new[] { First("USD", T, title: "CPI m/m"), First("USD", T, title: "Core CPI m/m") },
            new[] { Second("USD", T, title: "CPI"), Second("USD", T, title: "Core CPI") });

        Assert.Single(merged);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    [Trait("Req", "NWS-012")]
    public void Merge_EventsWithoutTime_AreKeptUnmerged()
    {
        NewsEvent first = First("USD", null);
        NewsEvent second = Second("USD", null);

        IReadOnlyList<NewsEvent> merged = NewsMerge.Merge(new[] { first }, new[] { second });

        Assert.Equal(new[] { first, second }, merged);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Merge_InputOrder_DoesNotChangeTheResult()
    {
        NewsEvent[] first = { First("USD", T), First("EUR", T.AddHours(-5), NewsImpact.Low), First("GBP", T.AddHours(1), NewsImpact.Medium), First("JPY", null, NewsImpact.Holiday) };
        NewsEvent[] second = { Second("GBP", T.AddHours(1).AddMinutes(2)), Second("USD", T.AddMinutes(-1), NewsImpact.Medium), Second("AUD", T) };

        IReadOnlyList<NewsEvent> forward = NewsMerge.Merge(first, second);
        IReadOnlyList<NewsEvent> backward = NewsMerge.Merge(Enumerable.Reverse(first).ToArray(), Enumerable.Reverse(second).ToArray());

        Assert.Equal(forward, backward);
        Assert.Equal(5, forward.Count);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Calendar_TwoSources_HighFromEitherOpensOneWindowAtTheEarlierTime()
    {
        // First feed: USD medium at 13:30. Second feed: USD high at 13:34. Merged: high at 13:30,
        // so the window is 13:00-14:00 (not 13:04-14:04).
        var calendar = new NewsCalendar(
            new IReadOnlyList<NewsEvent>[] { new[] { First("USD", T, NewsImpact.Medium) }, new[] { Second("USD", T.AddMinutes(4)) } },
            Array.Empty<NewsEvent>(),
            30,
            30);

        NewsWindow window = Assert.Single(calendar.WindowsFor("EURUSD"));

        Assert.Equal(Utc(2026, 11, 10, 13, 0), window.StartUtc);
        Assert.Equal(Utc(2026, 11, 10, 14, 0), window.EndUtc);
        Assert.Single(calendar.Events);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Calendar_ThreeSources_AreMergedInOrder()
    {
        var calendar = new NewsCalendar(
            new IReadOnlyList<NewsEvent>[] { new[] { First("USD", T, NewsImpact.Low) }, new[] { Second("USD", T) }, new[] { Event("USD", T.AddMinutes(1), NewsImpact.Medium, "Third", "Third") } },
            Array.Empty<NewsEvent>(),
            30,
            30);

        NewsEvent merged = Assert.Single(calendar.Events);
        Assert.Equal("First+Second+Third", merged.Source);
        Assert.Equal(NewsImpact.High, merged.Impact);
    }

    [Fact]
    [Trait("Req", "NWS-002")]
    public void Merge_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => NewsMerge.Merge(null!, Array.Empty<NewsEvent>()));
        Assert.Throws<ArgumentNullException>(() => NewsMerge.Merge(Array.Empty<NewsEvent>(), null!));
    }
}
