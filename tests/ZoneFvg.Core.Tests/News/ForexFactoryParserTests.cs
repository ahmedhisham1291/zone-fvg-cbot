using Xunit;
using ZoneFvg.Core.News;
using static ZoneFvg.Core.Tests.NewsTestData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// NWS-003: the Forex Factory weekly export, parsed against the owner's real sample
/// (TestData/News/forexfactory-thisweek.json, 106 events, all dates at -05:00, New York winter
/// time). Times convert to UTC by subtracting the offset: 08:30 at -05:00 is 08:30 + 5 h = 13:30 UTC.
/// </summary>
public class ForexFactoryParserTests
{
    private static NewsParseResult ParseSample()
    {
        return new ForexFactoryParser().Parse(Sample, SampleWednesday);
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_RealSample_Gives106EventsWithTheirImpacts()
    {
        NewsParseResult result = ParseSample();

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(106, result.Events.Count);
        Assert.Equal("106 events", result.Reason);
        Assert.Equal(5, result.Events.Count(e => e.Impact == NewsImpact.High));
        Assert.Equal(6, result.Events.Count(e => e.Impact == NewsImpact.Medium));
        Assert.Equal(92, result.Events.Count(e => e.Impact == NewsImpact.Low));
        Assert.Equal(3, result.Events.Count(e => e.Impact == NewsImpact.Holiday));
        Assert.All(result.Events, e => Assert.Equal(DateTimeKind.Utc, e.TimeUtc!.Value.Kind));
        Assert.All(result.Events, e => Assert.Equal(ForexFactoryParser.SourceName, e.Source));
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_RealSample_UsdCpiIsHighAt1330Utc()
    {
        // "2026-11-10T08:30:00-05:00": 08:30 New York winter time + 5 h = 13:30 UTC.
        NewsEvent cpi = Assert.Single(ParseSample().Events, e => e.Title == "CPI m/m");

        Assert.Equal(Utc(2026, 11, 10, 13, 30), cpi.TimeUtc);
        Assert.Equal("USD", cpi.Currency);
        Assert.Equal(NewsImpact.High, cpi.Impact);
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_RealSample_GbpGdpIsHighAt0700Utc()
    {
        // "2026-11-12T02:00:00-05:00": 02:00 + 5 h = 07:00 UTC.
        NewsEvent gdp = Assert.Single(ParseSample().Events, e => e.Title == "GDP m/m");

        Assert.Equal(Utc(2026, 11, 12, 7, 0), gdp.TimeUtc);
        Assert.Equal("GBP", gdp.Currency);
        Assert.Equal(NewsImpact.High, gdp.Impact);
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_RealSample_FirstEventCrossesMidnightIntoSundayEveningUtc()
    {
        // "2026-11-08T17:30:00-05:00": 17:30 + 5 h = 22:30 UTC on the same Sunday.
        NewsEvent first = ParseSample().Events[0];

        Assert.Equal("RBA Assist Gov Hunter Speaks", first.Title);
        Assert.Equal(Utc(2026, 11, 8, 22, 30), first.TimeUtc);
        Assert.Equal("AUD", first.Currency);
        Assert.Equal(NewsImpact.Low, first.Impact);
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    [Trait("Req", "NWS-012")]
    public void Parse_RealSample_HolidayKeepsItsDateButHasNoClockTime()
    {
        // "2026-11-11T02:01:00-05:00" = 07:01 UTC. A holiday has no real clock time (NWS-012).
        NewsEvent holiday = Assert.Single(ParseSample().Events, e => e.Title == "French Bank Holiday");

        Assert.Equal(Utc(2026, 11, 11, 7, 1), holiday.TimeUtc);
        Assert.Equal(NewsImpact.Holiday, holiday.Impact);
        Assert.False(holiday.HasClockTime);
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_RealSample_CountryAllIsKeptAsItsOwnCode()
    {
        NewsParseResult result = ParseSample();

        Assert.Equal(2, result.Events.Count(e => e.Currency == "All"));
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    [Trait("Req", "NWS-005")]
    public void Parse_RealSample_CoversTheMondayBasedUtcWeekOfNow()
    {
        NewsParseResult result = ParseSample();

        Assert.Equal(Utc(2026, 11, 9, 0, 0), result.CoverageStartUtc);
        Assert.Equal(Utc(2026, 11, 16, 0, 0), result.CoverageEndUtc);
        Assert.True(result.Covers(SampleWednesday));
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_DaylightSavingWeek_EachOffsetIsAppliedAsWritten()
    {
        // US summer time began on Sunday 2026-03-08. Thursday 03-05 is still at -05:00
        // (08:30 + 5 h = 13:30 UTC); Tuesday 03-10 is at -04:00 (08:30 + 4 h = 12:30 UTC).
        string document = "[\n"
            + "  {\"title\": \"Unemployment Claims\", \"country\": \"USD\", \"date\": \"2026-03-05T08:30:00-05:00\", \"impact\": \"Medium\", \"forecast\": \"\", \"previous\": \"\"},\n"
            + "  {\"title\": \"CPI m/m\", \"country\": \"USD\", \"date\": \"2026-03-10T08:30:00-04:00\", \"impact\": \"High\", \"forecast\": \"\", \"previous\": \"\"}\n"
            + "]";

        NewsParseResult result = new ForexFactoryParser().Parse(document, Utc(2026, 3, 9, 10, 0));

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(Utc(2026, 3, 5, 13, 30), result.Events[0].TimeUtc);
        Assert.Equal(Utc(2026, 3, 10, 12, 30), result.Events[1].TimeUtc);
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_PositiveOffset_IsSubtracted()
    {
        // 09:00 at +01:00 is 08:00 UTC.
        NewsParseResult result = new ForexFactoryParser().Parse(OneEventDocument("German CPI", "EUR", "2026-11-11T09:00:00+01:00", "High"), SampleWednesday);

        Assert.Equal(Utc(2026, 11, 11, 8, 0), Assert.Single(result.Events).TimeUtc);
    }

    [Theory]
    [Trait("Req", "NWS-003")]
    [InlineData("High", NewsImpact.High)]
    [InlineData("high", NewsImpact.High)]
    [InlineData(" HIGH ", NewsImpact.High)]
    [InlineData("Medium", NewsImpact.Medium)]
    [InlineData("Low", NewsImpact.Low)]
    [InlineData("Holiday", NewsImpact.Holiday)]
    [InlineData("Non-Economic", NewsImpact.Unknown)]
    [InlineData("", NewsImpact.Unknown)]
    [InlineData("Highest", NewsImpact.Unknown)]
    public void Parse_ImpactValues_MapToKnownRatingsOrUnknown(string impact, NewsImpact expected)
    {
        NewsParseResult result = new ForexFactoryParser().Parse(OneEventDocument("X", "USD", "2026-11-11T08:30:00-05:00", impact), SampleWednesday);

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(expected, Assert.Single(result.Events).Impact);
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_RequestDeniedHtmlPage_IsRejected()
    {
        // Written for this test: the parser rejects any document that starts with '<'.
        const string page = "\n  <!DOCTYPE html>\n<html><head><title>Request Denied</title></head><body>Request Denied</body></html>";

        NewsParseResult result = new ForexFactoryParser().Parse(page, SampleWednesday);

        Assert.False(result.IsValid);
        Assert.Contains("HTML", result.Reason, StringComparison.Ordinal);
        Assert.Contains("Request Denied", result.Reason, StringComparison.Ordinal);
        Assert.Empty(result.Events);
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_LongHtmlPage_ReasonQuotesOnlyTheStart()
    {
        string page = "<html>" + new string('x', 500) + "</html>";

        NewsParseResult result = new ForexFactoryParser().Parse(page, SampleWednesday);

        Assert.False(result.IsValid);
        Assert.Contains("...", result.Reason, StringComparison.Ordinal);
        Assert.True(result.Reason.Length < 200);
    }

    [Theory]
    [Trait("Req", "NWS-003")]
    [InlineData(0.5)]
    [InlineData(0.99)]
    [InlineData(0.01)]
    public void Parse_TruncatedSample_IsRejectedAsInvalidJson(double fraction)
    {
        string sample = Sample;
        string truncated = sample.Substring(0, (int)(sample.Length * fraction));

        NewsParseResult result = new ForexFactoryParser().Parse(truncated, SampleWednesday);

        Assert.False(result.IsValid);
        Assert.StartsWith("The document is not valid JSON", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "NWS-003")]
    [InlineData("[]")]
    [InlineData(" [ ] ")]
    public void Parse_EmptyList_IsRejected(string document)
    {
        NewsParseResult result = new ForexFactoryParser().Parse(document, SampleWednesday);

        Assert.False(result.IsValid);
        Assert.Equal("The document contains no events.", result.Reason);
    }

    [Theory]
    [Trait("Req", "NWS-003")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n\t")]
    [InlineData("﻿")]
    public void Parse_EmptyDocument_IsRejected(string? document)
    {
        NewsParseResult result = new ForexFactoryParser().Parse(document!, SampleWednesday);

        Assert.False(result.IsValid);
        Assert.Equal("The document is empty.", result.Reason);
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_SampleWithByteOrderMark_IsAccepted()
    {
        NewsParseResult result = new ForexFactoryParser().Parse("﻿" + Sample, SampleWednesday);

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(106, result.Events.Count);
    }

    [Theory]
    [Trait("Req", "NWS-003")]
    [InlineData("{}", "The document is not a JSON array of events.")]
    [InlineData("\"text\"", "The document is not a JSON array of events.")]
    [InlineData("[1]", "Event 0 is malformed: it is not a JSON object.")]
    [InlineData("[{\"country\":\"USD\",\"date\":\"2026-11-11T08:30:00-05:00\",\"impact\":\"High\"}]", "Event 0 is malformed: the field \"title\" is missing.")]
    [InlineData("[{\"title\":\"X\",\"date\":\"2026-11-11T08:30:00-05:00\",\"impact\":\"High\"}]", "Event 0 is malformed: the field \"country\" is missing.")]
    [InlineData("[{\"title\":\"X\",\"country\":\"USD\",\"impact\":\"High\"}]", "Event 0 is malformed: the field \"date\" is missing.")]
    [InlineData("[{\"title\":\"X\",\"country\":\"USD\",\"date\":\"2026-11-11T08:30:00-05:00\"}]", "Event 0 is malformed: the field \"impact\" is missing.")]
    [InlineData("[{\"title\":\"X\",\"country\":\"USD\",\"date\":\"2026-11-11T08:30:00-05:00\",\"impact\":3}]", "Event 0 is malformed: the field \"impact\" is not a string.")]
    [InlineData("[{\"title\":null,\"country\":\"USD\",\"date\":\"2026-11-11T08:30:00-05:00\",\"impact\":\"High\"}]", "Event 0 is malformed: the field \"title\" is not a string.")]
    [InlineData("[{\"title\":\"X\",\"country\":\" \",\"date\":\"2026-11-11T08:30:00-05:00\",\"impact\":\"High\"}]", "Event 0 is malformed: the field \"country\" is empty.")]
    public void Parse_MalformedDocument_IsRejectedWithItsReason(string document, string reason)
    {
        NewsParseResult result = new ForexFactoryParser().Parse(document, SampleWednesday);

        Assert.False(result.IsValid);
        Assert.Equal(reason, result.Reason);
        Assert.Empty(result.Events);
    }

    [Theory]
    [Trait("Req", "NWS-003")]
    [InlineData("2026-11-11T08:30:00")]
    [InlineData("2026-11-11T13:30:00Z")]
    [InlineData("2026-11-11 08:30:00-05:00")]
    [InlineData("2026-11-11T08:30-05:00")]
    [InlineData("11/11/2026 08:30")]
    [InlineData("2026-11-31T08:30:00-05:00")]
    [InlineData("All Day")]
    [InlineData("")]
    public void Parse_DateWithoutOffsetOrMalformed_RejectsTheWholeDocument(string date)
    {
        string document = "[" + OneEventDocument("Good", "USD", "2026-11-11T08:30:00-05:00", "High").Trim('[', ']')
            + "," + OneEventDocument("Bad", "USD", date, "High").Trim('[', ']') + "]";

        NewsParseResult result = new ForexFactoryParser().Parse(document, SampleWednesday);

        Assert.False(result.IsValid);
        Assert.StartsWith("Event 1 is malformed: the date \"" + date + "\" is not ISO 8601 with an offset", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "NWS-003")]
    [Trait("Req", "NWS-005")]
    [InlineData(2026, 10, 26, 12, 0, false)]
    [InlineData(2026, 11, 1, 23, 59, false)]
    [InlineData(2026, 11, 2, 0, 0, true)]
    [InlineData(2026, 11, 9, 0, 0, true)]
    [InlineData(2026, 11, 15, 23, 59, true)]
    [InlineData(2026, 11, 16, 0, 0, false)]
    [InlineData(2026, 11, 23, 12, 0, false)]
    public void Parse_RealSample_AcceptedOnlyWhenAnEventFallsInTheCurrentWeek(int year, int month, int day, int hour, int minute, bool expected)
    {
        // Sample events run from 2026-11-08 22:30 UTC to 2026-11-14 11:00 UTC. The Sunday 22:30
        // event lies in the week of Monday 11-02, so the document also counts for that week.
        NewsParseResult result = new ForexFactoryParser().Parse(Sample, Utc(year, month, day, hour, minute));

        Assert.Equal(expected, result.IsValid);
        if (!expected)
        {
            Assert.StartsWith("The document has no event in the current week from ", result.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Parse_NowNotUtc_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ForexFactoryParser().Parse(Sample, new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    [Trait("Req", "NWS-003")]
    public void Source_IsForexFactory()
    {
        Assert.Equal("ForexFactory", new ForexFactoryParser().Source);
    }
}
