using Xunit;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// New York time and the trading day (SIG-030). The switch dates of 2020 to 2030 below were taken
/// from the IANA zone America/New_York and are written out here, so the test reads no time zone
/// data itself.
/// </summary>
public class NewYorkClockTests
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1.0);

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second)
    {
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
    }

    [Theory]
    [Trait("Req", "SIG-030")]
    [InlineData(2020, 8, 1)]
    [InlineData(2021, 14, 7)]
    [InlineData(2022, 13, 6)]
    [InlineData(2023, 12, 5)]
    [InlineData(2024, 10, 3)]
    [InlineData(2025, 9, 2)]
    [InlineData(2026, 8, 1)]
    [InlineData(2027, 14, 7)]
    [InlineData(2028, 12, 5)]
    [InlineData(2029, 11, 4)]
    [InlineData(2030, 10, 3)]
    public void SwitchInstants_2020To2030_AreSecondSundayOfMarchAt7AndFirstSundayOfNovemberAt6Utc(int year, int marchDay, int novemberDay)
    {
        DateTime start = Utc(year, 3, marchDay, 7, 0, 0);
        DateTime end = Utc(year, 11, novemberDay, 6, 0, 0);

        Assert.Equal(start, NewYorkClock.SummerTimeStartUtc(year));
        Assert.Equal(end, NewYorkClock.SummerTimeEndUtc(year));
        Assert.Equal(DateTimeKind.Utc, NewYorkClock.SummerTimeStartUtc(year).Kind);
        Assert.Equal(DayOfWeek.Sunday, start.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, end.DayOfWeek);
    }

    [Theory]
    [Trait("Req", "SIG-030")]
    [InlineData(2020, 8, 1)]
    [InlineData(2021, 14, 7)]
    [InlineData(2022, 13, 6)]
    [InlineData(2023, 12, 5)]
    [InlineData(2024, 10, 3)]
    [InlineData(2025, 9, 2)]
    [InlineData(2026, 8, 1)]
    [InlineData(2027, 14, 7)]
    [InlineData(2028, 12, 5)]
    [InlineData(2029, 11, 4)]
    [InlineData(2030, 10, 3)]
    public void ToNewYork_LastSecondBeforeAndFirstSecondOfEachSwitch_JumpsAsInNewYork(int year, int marchDay, int novemberDay)
    {
        DateTime start = Utc(year, 3, marchDay, 7, 0, 0);
        DateTime end = Utc(year, 11, novemberDay, 6, 0, 0);

        Assert.False(NewYorkClock.IsSummerTime(start - OneSecond));
        Assert.True(NewYorkClock.IsSummerTime(start));
        Assert.True(NewYorkClock.IsSummerTime(end - OneSecond));
        Assert.False(NewYorkClock.IsSummerTime(end));
        Assert.Equal(new DateTime(year, 3, marchDay, 1, 59, 59), NewYorkClock.ToNewYork(start - OneSecond));
        Assert.Equal(new DateTime(year, 3, marchDay, 3, 0, 0), NewYorkClock.ToNewYork(start));
        Assert.Equal(new DateTime(year, 11, novemberDay, 1, 59, 59), NewYorkClock.ToNewYork(end - OneSecond));
        Assert.Equal(new DateTime(year, 11, novemberDay, 1, 0, 0), NewYorkClock.ToNewYork(end));
    }

    [Fact]
    [Trait("Req", "SIG-030")]
    public void UtcOffset_SummerAndWinter_IsMinusFourAndMinusFiveHours()
    {
        Assert.Equal(TimeSpan.FromHours(-4.0), NewYorkClock.UtcOffset(Utc(2025, 7, 16, 12, 0, 0)));
        Assert.Equal(TimeSpan.FromHours(-5.0), NewYorkClock.UtcOffset(Utc(2025, 1, 15, 12, 0, 0)));
        Assert.Equal(new DateTime(2025, 7, 16, 8, 0, 0), NewYorkClock.ToNewYork(Utc(2025, 7, 16, 12, 0, 0)));
        Assert.Equal(new DateTime(2025, 1, 15, 7, 0, 0), NewYorkClock.ToNewYork(Utc(2025, 1, 15, 12, 0, 0)));
    }

    [Fact]
    [Trait("Req", "SIG-030")]
    public void ToNewYork_Result_HasKindUnspecified()
    {
        Assert.Equal(DateTimeKind.Unspecified, NewYorkClock.ToNewYork(Utc(2025, 1, 15, 12, 0, 0)).Kind);
    }

    [Theory]
    [Trait("Req", "SIG-030")]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void IsSummerTime_TimeNotUtc_Throws(DateTimeKind kind)
    {
        var time = new DateTime(2025, 7, 16, 12, 0, 0, kind);

        var exception = Assert.Throws<ArgumentException>(() => NewYorkClock.IsSummerTime(time));
        Assert.Equal("utc", exception.ParamName);
        Assert.Throws<ArgumentException>(() => NewYorkClock.ToNewYork(time));
        Assert.Throws<ArgumentException>(() => NewYorkClock.TradingDate(time));
    }

    [Theory]
    [Trait("Req", "SIG-030")]
    [InlineData(0)]
    [InlineData(10000)]
    public void SwitchInstants_YearOutOfRange_Throw(int year)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => NewYorkClock.SummerTimeStartUtc(year));
        Assert.Equal("year", exception.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => NewYorkClock.SummerTimeEndUtc(year));
    }

    [Theory]
    [Trait("Req", "SIG-030")]
    [InlineData(1)]
    [InlineData(9999)]
    public void SwitchInstants_FirstAndLastYear_AreSundays(int year)
    {
        Assert.Equal(DayOfWeek.Sunday, NewYorkClock.SummerTimeStartUtc(year).DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, NewYorkClock.SummerTimeEndUtc(year).DayOfWeek);
    }

    /// <summary>
    /// The trading day's name and start at 16:59:59 and 17:00:00 New York: an ordinary winter and
    /// summer Tuesday, a Friday evening, a Sunday evening, and the Saturday and Sunday evenings of
    /// both switch weekends of 2025.
    /// </summary>
    [Theory]
    [Trait("Req", "SIG-030")]
    [InlineData("2025-01-14 21:59:59", "2025-01-14", "2025-01-13 22:00:00")]
    [InlineData("2025-01-14 22:00:00", "2025-01-15", "2025-01-14 22:00:00")]
    [InlineData("2025-07-15 20:59:59", "2025-07-15", "2025-07-14 21:00:00")]
    [InlineData("2025-07-15 21:00:00", "2025-07-16", "2025-07-15 21:00:00")]
    [InlineData("2025-01-17 21:59:59", "2025-01-17", "2025-01-16 22:00:00")]
    [InlineData("2025-01-17 22:00:00", "2025-01-18", "2025-01-17 22:00:00")]
    [InlineData("2025-01-19 21:59:59", "2025-01-19", "2025-01-18 22:00:00")]
    [InlineData("2025-01-19 22:00:00", "2025-01-20", "2025-01-19 22:00:00")]
    [InlineData("2025-03-08 21:59:59", "2025-03-08", "2025-03-07 22:00:00")]
    [InlineData("2025-03-08 22:00:00", "2025-03-09", "2025-03-08 22:00:00")]
    [InlineData("2025-03-09 20:59:59", "2025-03-09", "2025-03-08 22:00:00")]
    [InlineData("2025-03-09 21:00:00", "2025-03-10", "2025-03-09 21:00:00")]
    [InlineData("2025-11-01 20:59:59", "2025-11-01", "2025-10-31 21:00:00")]
    [InlineData("2025-11-01 21:00:00", "2025-11-02", "2025-11-01 21:00:00")]
    [InlineData("2025-11-02 21:59:59", "2025-11-02", "2025-11-01 21:00:00")]
    [InlineData("2025-11-02 22:00:00", "2025-11-03", "2025-11-02 22:00:00")]
    public void TradingDate_At1659And1700NewYork_IsNamedByTheDateItEndsOn(string utcText, string expectedDate, string expectedStartUtc)
    {
        DateTime utc = Parse(utcText);
        DateTime tradingDate = NewYorkClock.TradingDate(utc);

        Assert.Equal(Parse(expectedDate + " 00:00:00"), tradingDate);
        Assert.Equal(TimeSpan.Zero, tradingDate.TimeOfDay);
        Assert.Equal(Parse(expectedStartUtc), NewYorkClock.TradingDayStartUtc(tradingDate));
        Assert.Equal(DateTimeKind.Utc, NewYorkClock.TradingDayStartUtc(tradingDate).Kind);
        Assert.True(NewYorkClock.TradingDayStartUtc(tradingDate) <= utc);
        Assert.True(utc < NewYorkClock.TradingDayStartUtc(tradingDate.AddDays(1)));
    }

    [Fact]
    [Trait("Req", "SIG-030")]
    public void TradingDayStartUtc_SwitchWeekends_GiveDaysOf23And25Hours()
    {
        Assert.Equal(TimeSpan.FromHours(23.0), NewYorkClock.TradingDayStartUtc(new DateTime(2025, 3, 10)) - NewYorkClock.TradingDayStartUtc(new DateTime(2025, 3, 9)));
        Assert.Equal(TimeSpan.FromHours(25.0), NewYorkClock.TradingDayStartUtc(new DateTime(2025, 11, 3)) - NewYorkClock.TradingDayStartUtc(new DateTime(2025, 11, 2)));
    }

    [Theory]
    [Trait("Req", "SIG-030")]
    [InlineData("2025-01-15", 19, 0, "2025-01-15 00:00:00")]
    [InlineData("2025-01-15", 3, 0, "2025-01-15 08:00:00")]
    [InlineData("2025-01-15", 12, 0, "2025-01-15 17:00:00")]
    [InlineData("2025-01-15", 16, 30, "2025-01-15 21:30:00")]
    [InlineData("2025-07-16", 19, 0, "2025-07-15 23:00:00")]
    [InlineData("2025-07-16", 3, 0, "2025-07-16 07:00:00")]
    [InlineData("2025-07-16", 12, 0, "2025-07-16 16:00:00")]
    [InlineData("2025-07-16", 16, 30, "2025-07-16 20:30:00")]
    [InlineData("2025-07-16", 17, 0, "2025-07-15 21:00:00")]
    [InlineData("2025-07-16", 16, 59, "2025-07-16 20:59:00")]
    public void UtcInTradingDay_SummerAndWinterDay_PlacesTheTimeByItsDistanceFrom1700(string tradingDate, int hour, int minute, string expectedUtc)
    {
        DateTime utc = NewYorkClock.UtcInTradingDay(Parse(tradingDate + " 00:00:00"), new TimeSpan(hour, minute, 0));

        Assert.Equal(Parse(expectedUtc), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    [Trait("Req", "SIG-030")]
    public void UtcInTradingDay_TimeOfDayOfTheDateArgument_IsIgnored()
    {
        Assert.Equal(
            NewYorkClock.UtcInTradingDay(new DateTime(2025, 1, 15), TimeSpan.FromHours(3.0)),
            NewYorkClock.UtcInTradingDay(new DateTime(2025, 1, 15, 13, 45, 0, DateTimeKind.Utc), TimeSpan.FromHours(3.0)));
    }

    [Theory]
    [Trait("Req", "SIG-030")]
    [InlineData(17, 0, 0, 0)]
    [InlineData(16, 59, 23, 59)]
    [InlineData(19, 0, 2, 0)]
    [InlineData(3, 0, 10, 0)]
    [InlineData(12, 0, 19, 0)]
    [InlineData(16, 30, 23, 30)]
    [InlineData(0, 0, 7, 0)]
    public void PlaceInTradingDay_TimeOfDay_IsItsDistanceFrom1700(int hour, int minute, int expectedHour, int expectedMinute)
    {
        Assert.Equal(new TimeSpan(expectedHour, expectedMinute, 0), NewYorkClock.PlaceInTradingDay(new TimeSpan(hour, minute, 0)));
    }

    [Fact]
    [Trait("Req", "SIG-030")]
    public void PlaceInTradingDay_OutsideADay_Throws()
    {
        var below = Assert.Throws<ArgumentOutOfRangeException>(() => NewYorkClock.PlaceInTradingDay(TimeSpan.FromTicks(-1)));
        var above = Assert.Throws<ArgumentOutOfRangeException>(() => NewYorkClock.PlaceInTradingDay(TimeSpan.FromHours(24.0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewYorkClock.UtcInTradingDay(new DateTime(2025, 1, 15), TimeSpan.FromHours(24.0)));

        Assert.Equal("newYorkTimeOfDay", below.ParamName);
        Assert.Contains("1.00:00:00", above.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIG-030")]
    public void UtcInTradingDay_WallClockInsideASwitch_UsesTheSummerOffsetOnlyWhenTheResultIsInSummerTime()
    {
        // 02:30 on 9 March 2025 does not exist in New York: read with the winter offset it is 07:30 UTC (03:30 summer time).
        Assert.Equal(Utc(2025, 3, 9, 7, 30, 0), NewYorkClock.UtcInTradingDay(new DateTime(2025, 3, 9), new TimeSpan(2, 30, 0)));

        // 01:30 on 2 November 2025 exists twice: the first, in summer time, is 05:30 UTC.
        Assert.Equal(Utc(2025, 11, 2, 5, 30, 0), NewYorkClock.UtcInTradingDay(new DateTime(2025, 11, 2), new TimeSpan(1, 30, 0)));
    }

    [Fact]
    [Trait("Req", "SIG-030")]
    public void UtcInTradingDay_EveryDayOf2020To2030_RoundTripsThroughToNewYorkAndTradingDate()
    {
        TimeSpan[] times = { TimeSpan.FromHours(17.0), TimeSpan.FromHours(19.0), TimeSpan.FromHours(3.0), TimeSpan.FromHours(12.0), new TimeSpan(16, 30, 0) };
        for (var date = new DateTime(2020, 1, 1); date <= new DateTime(2030, 12, 31); date = date.AddDays(1))
        {
            foreach (TimeSpan time in times)
            {
                DateTime utc = NewYorkClock.UtcInTradingDay(date, time);
                string at = Invariant.Second(date) + " " + Invariant.Text(time);

                Assert.True(NewYorkClock.ToNewYork(utc).TimeOfDay == time, at + ": New York time of day differs");
                Assert.True(NewYorkClock.TradingDate(utc) == date, at + ": trading date differs");
            }
        }
    }

    private static DateTime Parse(string text)
    {
        return DateTime.SpecifyKind(DateTime.ParseExact(text, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
    }
}
