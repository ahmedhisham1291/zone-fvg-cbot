using Xunit;
using ZoneFvg.Core.News;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// PLT-014: the number of events of the embedded history, which decides whether the news filter
/// is on in a backtest. Every line that the parser does not ignore (empty or starting with #) is
/// an event line; a malformed line counts, so a malformed history keeps the filter on (NWS-010).
/// </summary>
public class EmbeddedHistoryEventCountTests
{
    private const string History =
        "# Forex Factory high-impact history, UTC\n"
        + "2026-11-10 13:30,USD,High,CPI m/m\r\n"
        + "\n"
        + "   \r\n"
        + "2026-11-12 07:00,GBP,High,GDP m/m, first estimate\n"
        + "2026-11-11 00:00,USD,Holiday,Bank Holiday\n";

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData(null)]
    [InlineData("")]
    public void CountEvents_NullOrEmptyText_Zero(string? text)
    {
        Assert.Equal(0, EmbeddedHistoryProvider.CountEvents(text));
        Assert.False(EmbeddedHistoryProvider.Parse(text).IsValid);
    }

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData("\n")]
    [InlineData("\r\n  \r\n")]
    [InlineData("# only a comment\n#2026-11-10 13:30,USD,High,CPI m/m\n")]
    public void CountEvents_OnlyEmptyAndCommentLines_ZeroAndTheParserStillRejects(string text)
    {
        NewsParseResult result = EmbeddedHistoryProvider.Parse(text);

        Assert.Equal(0, EmbeddedHistoryProvider.CountEvents(text));
        Assert.False(result.IsValid);
        Assert.False(result.HasMalformedEvent);
        Assert.Equal("The embedded history contains no events.", result.Reason);
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void CountEvents_SmallValidHistory_IsTheNumberOfParsedEvents()
    {
        NewsParseResult result = EmbeddedHistoryProvider.Parse(History);

        Assert.Equal(3, EmbeddedHistoryProvider.CountEvents(History));
        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(3, result.Events.Count);
        Assert.Equal("3 events", result.Reason);
    }

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData("2026-11-10 13:30,USD,High,CPI m/m\n", 1)]
    [InlineData("2026-11-10 13:30,USD,High,CPI m/m", 1)]
    [InlineData("2026-11-10 13:30,USD,High,CPI m/m\n2026-11-11 13:30,USD,High,PPI m/m\n", 2)]
    public void CountEvents_OneOrTwoEvents_CountsEachLineOnce(string text, int expected)
    {
        Assert.Equal(expected, EmbeddedHistoryProvider.CountEvents(text));
        Assert.Equal(expected, EmbeddedHistoryProvider.Parse(text).Events.Count);
    }

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData("2026-11-10 13:30,USD,Severe,CPI m/m\n", 1)]
    [InlineData(" # indented, not a comment\n", 1)]
    [InlineData("2026-11-10 13:30,USD,High,CPI m/m\nnot an event\n", 2)]
    public void CountEvents_MalformedHistory_CountedAndTheParserStillRejectsIt(string text, int expected)
    {
        NewsParseResult result = EmbeddedHistoryProvider.Parse(text);

        Assert.Equal(expected, EmbeddedHistoryProvider.CountEvents(text));
        Assert.False(result.IsValid);
        Assert.True(result.HasMalformedEvent);
        Assert.Empty(result.Events);
    }
}
