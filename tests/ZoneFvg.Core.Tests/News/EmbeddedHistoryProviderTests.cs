using Xunit;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-010: the embedded history format, one event per line: UTC time, currency, impact, title
/// (<c>yyyy-MM-dd HH:mm,CUR,Impact,Title</c>).
/// </summary>
public class EmbeddedHistoryProviderTests
{
    private const string History =
        "# Forex Factory high-impact history, UTC\n"
        + "2026-11-10 13:30,USD,High,CPI m/m\r\n"
        + "\n"
        + "2026-11-12 07:00,GBP,High,GDP m/m, first estimate\n"
        + "2026-11-11 00:00,USD,Holiday,Bank Holiday\n"
        + "2026-11-09 08:00,EUR,Medium,Speech\n"
        + "2026-11-13 09:00,JPY,Low,\n";

    [Fact]
    [Trait("Req", "NWS-010")]
    public void Parse_ValidHistory_GivesTheEventsInUtc()
    {
        NewsParseResult result = EmbeddedHistoryProvider.Parse(History);

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(
            new[]
            {
                new NewsEvent(Utc(2026, 11, 10, 13, 30), "USD", NewsImpact.High, "CPI m/m", "EmbeddedHistory"),
                new NewsEvent(Utc(2026, 11, 12, 7, 0), "GBP", NewsImpact.High, "GDP m/m, first estimate", "EmbeddedHistory"),
                new NewsEvent(Utc(2026, 11, 11, 0, 0), "USD", NewsImpact.Holiday, "Bank Holiday", "EmbeddedHistory"),
                new NewsEvent(Utc(2026, 11, 9, 8, 0), "EUR", NewsImpact.Medium, "Speech", "EmbeddedHistory"),
                new NewsEvent(Utc(2026, 11, 13, 9, 0), "JPY", NewsImpact.Low, string.Empty, "EmbeddedHistory"),
            },
            result.Events);
        Assert.Equal("5 events", result.Reason);
    }

    [Fact]
    [Trait("Req", "NWS-010")]
    public void Parse_ValidHistory_CoversFromTheFirstEventsWeekToTheEndOfTheLastEventsWeek()
    {
        NewsParseResult result = EmbeddedHistoryProvider.Parse("2026-11-18 13:30,USD,High,Late\n2026-03-04 13:30,USD,High,Early\n2026-06-01 13:30,USD,High,Middle");

        // Earliest Wednesday 2026-03-04: week from Monday 03-02. Latest Wednesday 11-18: week to Monday 11-23.
        Assert.Equal(Utc(2026, 3, 2, 0, 0), result.CoverageStartUtc);
        Assert.Equal(Utc(2026, 11, 23, 0, 0), result.CoverageEndUtc);
        Assert.False(result.Covers(Utc(2026, 3, 1, 23, 59)));
        Assert.True(result.Covers(Utc(2026, 3, 2, 0, 0)));
        Assert.True(result.Covers(Utc(2026, 11, 22, 23, 59)));
        Assert.False(result.Covers(Utc(2026, 11, 23, 0, 0)));
    }

    [Theory]
    [Trait("Req", "NWS-010")]
    [InlineData("2026-11-10 13:30,USD,High")]
    [InlineData("2026-11-10 13:30;USD;High;CPI")]
    [InlineData("2026-11-10T13:30,USD,High,CPI")]
    [InlineData("2026-11-10 13:30:00,USD,High,CPI")]
    [InlineData("2026-11-10 13:30,usd,High,CPI")]
    [InlineData("2026-11-10 13:30,USDX,High,CPI")]
    [InlineData("2026-11-10 13:30,U1D,High,CPI")]
    [InlineData("2026-11-10 13:30,US1,High,CPI")]
    [InlineData("2026-11-10 13:30,1SD,High,CPI")]
    [InlineData("2026-11-10 13:30,USD,high,CPI")]
    [InlineData("2026-11-10 13:30,USD,Unknown,CPI")]
    [InlineData("2026-11-10 13:30,USD,,CPI")]
    [InlineData(" 2026-11-10 13:30,USD,High,CPI")]
    public void Parse_MalformedLine_RejectsTheWholeHistoryQuotingTheLine(string line)
    {
        NewsParseResult result = EmbeddedHistoryProvider.Parse("2026-11-09 13:30,USD,High,Good\n" + line + "\n2026-11-11 13:30,USD,High,Good");

        Assert.False(result.IsValid);
        Assert.Equal("Embedded history line 2 is malformed: \"" + line + "\". Expected \"yyyy-MM-dd HH:mm,CUR,Impact,Title\".", result.Reason);
        Assert.Empty(result.Events);
    }

    [Theory]
    [Trait("Req", "NWS-010")]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_Empty_IsRejected(string? text)
    {
        NewsParseResult result = EmbeddedHistoryProvider.Parse(text);

        Assert.False(result.IsValid);
        Assert.Equal("The embedded history is empty.", result.Reason);
    }

    [Theory]
    [Trait("Req", "NWS-010")]
    [InlineData("\n")]
    [InlineData("# only a comment\n   \n")]
    public void Parse_NoEventLines_IsRejected(string text)
    {
        NewsParseResult result = EmbeddedHistoryProvider.Parse(text);

        Assert.False(result.IsValid);
        Assert.Equal("The embedded history contains no events.", result.Reason);
    }
}
