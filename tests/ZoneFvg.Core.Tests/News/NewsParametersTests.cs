using Xunit;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The news parameters of spec section 7, NWS-004 (NewsSources accepts only ForexFactory, owner
/// decision of 2026-11-14) and the start-up check of NWS-011.
/// </summary>
public class NewsParametersTests
{
    [Fact]
    public void Defaults_MatchSpecSection7()
    {
        var parameters = new NewsParameters();

        Assert.True(parameters.NewsEnabled);
        Assert.Equal(30, parameters.NewsBeforeMin);
        Assert.Equal(30, parameters.NewsAfterMin);
        Assert.Equal(NewsFailMode.Block, parameters.NewsFailMode);
        Assert.Equal("ForexFactory", parameters.NewsSources);
        Assert.Equal(48, parameters.NewsMaxCacheHours);
        Assert.Equal(4, parameters.NewsRefreshHours);
        Assert.Equal(string.Empty, parameters.ExtraBlackouts);
        Assert.Equal(60, NewsParameters.MinFetchIntervalMinutes);
    }

    [Fact]
    [Trait("Req", "NWS-004")]
    public void Validate_Defaults_AreAccepted()
    {
        Assert.Null(Record.Exception(() => new NewsParameters().Validate()));
    }

    [Fact]
    [Trait("Req", "NWS-004")]
    public void Validate_ForexFactory_IsAccepted()
    {
        Assert.Null(Record.Exception(() => new NewsParameters { NewsSources = "ForexFactory" }.Validate()));
    }

    [Theory]
    [Trait("Req", "NWS-004")]
    [InlineData("Both")]
    [InlineData("ShowMyTrades")]
    [InlineData("forexfactory")]
    [InlineData("ForexFactory ")]
    [InlineData(" ForexFactory")]
    [InlineData("ForexFactory,ShowMyTrades")]
    [InlineData("")]
    public void Validate_AnyOtherNewsSources_ThrowsWithAClearMessage(string sources)
    {
        var parameters = new NewsParameters { NewsSources = sources };

        var exception = Assert.Throws<ArgumentException>(() => parameters.Validate());

        Assert.Equal("NewsSources", exception.ParamName);
        Assert.Contains("NewsSources must be \"ForexFactory\"", exception.Message, StringComparison.Ordinal);
        Assert.Contains("but was \"" + sources + "\"", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "NWS-004")]
    public void Validate_NullNewsSources_Throws()
    {
        var parameters = new NewsParameters { NewsSources = null! };

        var exception = Assert.Throws<ArgumentException>(() => parameters.Validate());

        Assert.Equal("NewsSources", exception.ParamName);
        Assert.Contains("but was \"null\"", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "NWS-004")]
    [InlineData("Both")]
    [InlineData("ShowMyTrades")]
    public void NewsService_OtherNewsSources_StopsAtStart(string sources)
    {
        var parameters = new NewsParameters { NewsSources = sources };

        var exception = Assert.Throws<ArgumentException>(() => LiveService(new MemoryStore(), new FakeTransport(Failed()), parameters));

        Assert.Equal("NewsSources", exception.ParamName);
    }

    [Fact]
    [Trait("Req", "NWS-011")]
    public void Validate_MalformedExtraBlackouts_ThrowsQuotingTheEntry()
    {
        var parameters = new NewsParameters { ExtraBlackouts = "2026-11-10 13:30 USD;2026-11-10 25:00 GBP" };

        var exception = Assert.Throws<ArgumentException>(() => parameters.Validate());

        Assert.Equal("ExtraBlackouts", exception.ParamName);
        Assert.Contains("entry 2 is malformed: \"2026-11-10 25:00 GBP\"", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "NWS-011")]
    public void NewsService_MalformedExtraBlackouts_StopsAtStart()
    {
        var exception = Assert.Throws<ArgumentException>(() => LiveService(new MemoryStore(), new FakeTransport(Failed()), Parameters("tomorrow USD")));

        Assert.Equal("ExtraBlackouts", exception.ParamName);
        Assert.Contains("\"tomorrow USD\"", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "NWS-011")]
    public void Validate_NullExtraBlackouts_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new NewsParameters { ExtraBlackouts = null! }.Validate());

        Assert.Equal("ExtraBlackouts", exception.ParamName);
    }

    [Theory]
    [InlineData("NewsBeforeMin", 0)]
    [InlineData("NewsBeforeMin", 1441)]
    [InlineData("NewsAfterMin", 0)]
    [InlineData("NewsAfterMin", -5)]
    [InlineData("NewsMaxCacheHours", 0)]
    [InlineData("NewsMaxCacheHours", 169)]
    [InlineData("NewsRefreshHours", 0)]
    [InlineData("NewsRefreshHours", 49)]
    public void Validate_NumberOutOfRange_ThrowsNamingTheParameter(string name, int value)
    {
        NewsParameters parameters = name switch
        {
            "NewsBeforeMin" => new NewsParameters { NewsBeforeMin = value },
            "NewsAfterMin" => new NewsParameters { NewsAfterMin = value },
            "NewsMaxCacheHours" => new NewsParameters { NewsMaxCacheHours = value },
            _ => new NewsParameters { NewsRefreshHours = value },
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal(name, exception.ParamName);
        Assert.Contains("but was " + value.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_NumbersAtTheirLimits_AreAccepted()
    {
        var low = new NewsParameters { NewsBeforeMin = 1, NewsAfterMin = 1, NewsMaxCacheHours = 1, NewsRefreshHours = 1 };
        var high = new NewsParameters { NewsBeforeMin = 1440, NewsAfterMin = 1440, NewsMaxCacheHours = 168, NewsRefreshHours = 168 };

        Assert.Null(Record.Exception(() => low.Validate()));
        Assert.Null(Record.Exception(() => high.Validate()));
    }

    [Fact]
    public void Validate_UndefinedFailMode_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new NewsParameters { NewsFailMode = (NewsFailMode)7 }.Validate());

        Assert.Equal("NewsFailMode", exception.ParamName);
    }
}
