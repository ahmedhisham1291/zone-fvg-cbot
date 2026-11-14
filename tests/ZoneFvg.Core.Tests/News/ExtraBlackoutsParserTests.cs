using Xunit;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>NWS-011: ExtraBlackouts is a semicolon-separated list of "yyyy-MM-dd HH:mm CUR" entries in UTC, each a high-impact event.</summary>
public class ExtraBlackoutsParserTests
{
    [Fact]
    [Trait("Req", "NWS-011")]
    public void Parse_ValidList_GivesOneHighEventPerEntryInOrder()
    {
        ExtraBlackoutsResult result = ExtraBlackoutsParser.Parse("2026-11-10 13:30 USD; 2026-11-12 07:00 GBP ;2026-12-24 00:00 JPY");

        Assert.True(result.IsValid);
        Assert.Equal(string.Empty, result.Error);
        Assert.Equal(
            new[]
            {
                new NewsEvent(Utc(2026, 11, 10, 13, 30), "USD", NewsImpact.High, "Extra blackout", "ExtraBlackouts"),
                new NewsEvent(Utc(2026, 11, 12, 7, 0), "GBP", NewsImpact.High, "Extra blackout", "ExtraBlackouts"),
                new NewsEvent(Utc(2026, 12, 24, 0, 0), "JPY", NewsImpact.High, "Extra blackout", "ExtraBlackouts"),
            },
            result.Events);
        Assert.All(result.Events, e => Assert.Equal(DateTimeKind.Utc, e.TimeUtc!.Value.Kind));
    }

    [Theory]
    [Trait("Req", "NWS-011")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyParameter_IsAnEmptyList(string text)
    {
        ExtraBlackoutsResult result = ExtraBlackoutsParser.Parse(text);

        Assert.True(result.IsValid);
        Assert.Empty(result.Events);
    }

    [Theory]
    [Trait("Req", "NWS-011")]
    [InlineData("2026-11-10 13:30 usd", 1, "2026-11-10 13:30 usd")]
    [InlineData("2026-11-10 13:30 USDX", 1, "2026-11-10 13:30 USDX")]
    [InlineData("2026-11-10 13:30 US", 1, "2026-11-10 13:30 US")]
    [InlineData("2026-11-10 13:30 US1", 1, "2026-11-10 13:30 US1")]
    [InlineData("2026-11-10 13:30  USD", 1, "2026-11-10 13:30  USD")]
    [InlineData("2026-11-10T13:30 USD", 1, "2026-11-10T13:30 USD")]
    [InlineData("2026-11-10 1330 USD", 1, "2026-11-10 1330 USD")]
    [InlineData("2026-13-10 13:30 USD", 1, "2026-13-10 13:30 USD")]
    [InlineData("2026-11-10 24:00 USD", 1, "2026-11-10 24:00 USD")]
    [InlineData("2026-11-10 13:30:00 USD", 1, "2026-11-10 13:30:00 USD")]
    [InlineData("10.11.2026 13:30 USD", 1, "10.11.2026 13:30 USD")]
    [InlineData("2026-11-10 13:30 USD;", 2, "")]
    [InlineData(";2026-11-10 13:30 USD", 1, "")]
    [InlineData("2026-11-10 13:30 USD;;2026-11-11 13:30 USD", 2, "")]
    [InlineData("2026-11-10 13:30 USD,2026-11-11 13:30 USD", 1, "2026-11-10 13:30 USD,2026-11-11 13:30 USD")]
    [InlineData("2026-11-10 13:30 USD;2026-11-31 13:30 EUR", 2, "2026-11-31 13:30 EUR")]
    public void Parse_MalformedEntry_IsRejectedQuotingIt(string text, int entry, string quoted)
    {
        ExtraBlackoutsResult result = ExtraBlackoutsParser.Parse(text);

        Assert.False(result.IsValid);
        Assert.Empty(result.Events);
        Assert.StartsWith("ExtraBlackouts entry " + entry.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is malformed: \"" + quoted + "\".", result.Error, StringComparison.Ordinal);
        Assert.Contains("Expected \"yyyy-MM-dd HH:mm CUR\" in UTC", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "NWS-011")]
    public void Parse_Null_IsRejected()
    {
        ExtraBlackoutsResult result = ExtraBlackoutsParser.Parse(null);

        Assert.False(result.IsValid);
        Assert.Equal("ExtraBlackouts must not be null.", result.Error);
    }

    [Fact]
    [Trait("Req", "NWS-011")]
    public void Blackout_OpensAWindowLikeAHighImpactEvent()
    {
        var calendar = new NewsCalendar(Array.Empty<IReadOnlyList<NewsEvent>>(), ExtraBlackoutsParser.Parse("2026-11-10 13:30 USD").Events, 30, 30);

        Assert.False(calendar.IsInWindow("EURUSD", Utc(2026, 11, 10, 12, 59, 59)));
        Assert.True(calendar.IsInWindow("EURUSD", Utc(2026, 11, 10, 13, 0)));
        Assert.True(calendar.IsInWindow("EURUSD", Utc(2026, 11, 10, 14, 0)));
        Assert.False(calendar.IsInWindow("EURUSD", Utc(2026, 11, 10, 14, 0, 1)));
        Assert.False(calendar.IsInWindow("EURGBP", Utc(2026, 11, 10, 13, 30)));
    }
}
