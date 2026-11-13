using Xunit;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-012. Central European time is UTC+1, or UTC+2 from 01:00 UTC on the last Sunday of March
/// until 01:00 UTC on the last Sunday of October. The changeover dates below were read from a
/// calendar by hand: the last Sunday of each month.
/// </summary>
public class CestClockTests
{
    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second)
    {
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
    }

    // year, last Sunday of March, last Sunday of October
    public static TheoryData<int, int, int> Changeovers => new()
    {
        { 2025, 30, 26 },
        { 2026, 29, 25 },
        { 2027, 28, 31 },
        { 2028, 26, 29 },
        { 2029, 25, 28 },
        { 2030, 31, 27 },
    };

    // year, last Sunday of March
    public static TheoryData<int, int> MarchSwitches => new()
    {
        { 2025, 30 },
        { 2026, 29 },
        { 2027, 28 },
        { 2028, 26 },
        { 2029, 25 },
        { 2030, 31 },
    };

    // year, last Sunday of October
    public static TheoryData<int, int> OctoberSwitches => new()
    {
        { 2025, 26 },
        { 2026, 25 },
        { 2027, 31 },
        { 2028, 29 },
        { 2029, 28 },
        { 2030, 27 },
    };

    [Theory]
    [Trait("Req", "RSK-012")]
    [MemberData(nameof(Changeovers))]
    public void SummerTimeStartAndEndUtc_AreOneOClockUtcOnTheLastSundays(int year, int marchDay, int octoberDay)
    {
        Assert.Equal(Utc(year, 3, marchDay, 1, 0, 0), CestClock.SummerTimeStartUtc(year));
        Assert.Equal(Utc(year, 10, octoberDay, 1, 0, 0), CestClock.SummerTimeEndUtc(year));
        Assert.Equal(DayOfWeek.Sunday, CestClock.SummerTimeStartUtc(year).DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, CestClock.SummerTimeEndUtc(year).DayOfWeek);
        Assert.Equal(DateTimeKind.Utc, CestClock.SummerTimeStartUtc(year).Kind);
        Assert.Equal(DateTimeKind.Utc, CestClock.SummerTimeEndUtc(year).Kind);
    }

    [Theory]
    [Trait("Req", "RSK-012")]
    [MemberData(nameof(MarchSwitches))]
    public void IsSummerTime_OneSecondBeforeAndAtTheMarchSwitch_ChangesExactlyAtTheSwitch(int year, int marchDay)
    {
        DateTime march = Utc(year, 3, marchDay, 1, 0, 0);

        Assert.False(CestClock.IsSummerTime(march.AddSeconds(-1.0)));
        Assert.True(CestClock.IsSummerTime(march));
        Assert.Equal(TimeSpan.FromHours(1.0), CestClock.UtcOffset(march.AddSeconds(-1.0)));
        Assert.Equal(TimeSpan.FromHours(2.0), CestClock.UtcOffset(march));
    }

    [Theory]
    [Trait("Req", "RSK-012")]
    [MemberData(nameof(OctoberSwitches))]
    public void IsSummerTime_OneSecondBeforeAndAtTheOctoberSwitch_ChangesExactlyAtTheSwitch(int year, int octoberDay)
    {
        DateTime october = Utc(year, 10, octoberDay, 1, 0, 0);

        Assert.True(CestClock.IsSummerTime(october.AddSeconds(-1.0)));
        Assert.False(CestClock.IsSummerTime(october));
        Assert.Equal(TimeSpan.FromHours(2.0), CestClock.UtcOffset(october.AddSeconds(-1.0)));
        Assert.Equal(TimeSpan.FromHours(1.0), CestClock.UtcOffset(october));
    }

    [Theory]
    [Trait("Req", "RSK-012")]
    [MemberData(nameof(MarchSwitches))]
    public void StartOfCestDayUtc_OnTheMarchSwitchSunday_IsSaturday2300Utc(int year, int marchDay)
    {
        // Midnight of the switch Sunday is still CET (UTC+1), so the day began at 23:00 UTC on Saturday,
        // both before and after the 01:00 UTC switch.
        DateTime expected = Utc(year, 3, marchDay - 1, 23, 0, 0);

        Assert.Equal(expected, CestClock.StartOfCestDayUtc(Utc(year, 3, marchDay, 0, 59, 59)));
        Assert.Equal(expected, CestClock.StartOfCestDayUtc(Utc(year, 3, marchDay, 1, 0, 0)));

        // The Monday after begins at 00:00 CEST = Sunday 22:00 UTC; one second earlier is still Sunday.
        Assert.Equal(Utc(year, 3, marchDay, 22, 0, 0), CestClock.StartOfCestDayUtc(Utc(year, 3, marchDay, 22, 0, 0)));
        Assert.Equal(expected, CestClock.StartOfCestDayUtc(Utc(year, 3, marchDay, 21, 59, 59)));
    }

    [Theory]
    [Trait("Req", "RSK-012")]
    [MemberData(nameof(OctoberSwitches))]
    public void StartOfCestDayUtc_OnTheOctoberSwitchSunday_IsSaturday2200Utc(int year, int octoberDay)
    {
        // Midnight of the switch Sunday is still CEST (UTC+2), so the day began at 22:00 UTC on Saturday,
        // both before and after the 01:00 UTC switch.
        DateTime expected = Utc(year, 10, octoberDay - 1, 22, 0, 0);

        Assert.Equal(expected, CestClock.StartOfCestDayUtc(Utc(year, 10, octoberDay, 0, 59, 59)));
        Assert.Equal(expected, CestClock.StartOfCestDayUtc(Utc(year, 10, octoberDay, 1, 0, 0)));

        // The Monday after begins at 00:00 CET = Sunday 23:00 UTC; one second earlier is still Sunday.
        Assert.Equal(Utc(year, 10, octoberDay, 23, 0, 0), CestClock.StartOfCestDayUtc(Utc(year, 10, octoberDay, 23, 0, 0)));
        Assert.Equal(expected, CestClock.StartOfCestDayUtc(Utc(year, 10, octoberDay, 22, 59, 59)));
    }

    [Theory]
    [Trait("Req", "RSK-012")]
    [InlineData(2026, 1, 1, false)]
    [InlineData(2026, 2, 15, false)]
    [InlineData(2026, 4, 15, true)]
    [InlineData(2026, 7, 15, true)]
    [InlineData(2026, 10, 1, true)]
    [InlineData(2026, 11, 15, false)]
    [InlineData(2026, 12, 31, false)]
    public void IsSummerTime_AwayFromTheSwitch_FollowsTheSeason(int year, int month, int day, bool expected)
    {
        Assert.Equal(expected, CestClock.IsSummerTime(Utc(year, month, day, 12, 0, 0)));
    }

    [Theory]
    [Trait("Req", "RSK-012")]
    // Winter: 2026-01-14 10:00 UTC is 11:00 CET; the day began at 00:00 CET = 2026-01-13 23:00 UTC.
    [InlineData("2026-01-14T10:00:00", "2026-01-13T23:00:00")]
    // Summer: 2026-07-14 10:00 UTC is 12:00 CEST; the day began at 00:00 CEST = 2026-07-13 22:00 UTC.
    [InlineData("2026-07-14T10:00:00", "2026-07-13T22:00:00")]
    // Exactly at the start of a winter day, and one second before it.
    [InlineData("2026-01-13T23:00:00", "2026-01-13T23:00:00")]
    [InlineData("2026-01-13T22:59:59", "2026-01-12T23:00:00")]
    // Exactly at the start of a summer day, and one second before it.
    [InlineData("2026-07-13T22:00:00", "2026-07-13T22:00:00")]
    [InlineData("2026-07-13T21:59:59", "2026-07-12T22:00:00")]
    // Year end: 2026-12-31 23:00 UTC is 2027-01-01 00:00 CET.
    [InlineData("2026-12-31T23:00:00", "2026-12-31T23:00:00")]
    [InlineData("2026-12-31T22:59:59", "2026-12-30T23:00:00")]
    public void StartOfCestDayUtc_OrdinaryDays_IsLocalMidnightInUtc(string nowUtc, string expectedUtc)
    {
        DateTime start = CestClock.StartOfCestDayUtc(Parse(nowUtc));

        Assert.Equal(Parse(expectedUtc), start);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
    }

    [Theory]
    [Trait("Req", "RSK-012")]
    // March switch, Sunday 2026-03-29. Midnight of that Sunday was still CET: Saturday 23:00 UTC.
    [InlineData("2026-03-28T22:59:59", "2026-03-27T23:00:00")]
    [InlineData("2026-03-28T23:00:00", "2026-03-28T23:00:00")]
    [InlineData("2026-03-29T00:59:59", "2026-03-28T23:00:00")]
    [InlineData("2026-03-29T01:00:00", "2026-03-28T23:00:00")]
    [InlineData("2026-03-29T21:59:59", "2026-03-28T23:00:00")]
    // Monday 2026-03-30 begins at 00:00 CEST = Sunday 22:00 UTC.
    [InlineData("2026-03-29T22:00:00", "2026-03-29T22:00:00")]
    // October switch, Sunday 2026-10-25. Midnight of that Sunday was still CEST: Saturday 22:00 UTC.
    [InlineData("2026-10-24T21:59:59", "2026-10-23T22:00:00")]
    [InlineData("2026-10-24T22:00:00", "2026-10-24T22:00:00")]
    [InlineData("2026-10-25T00:59:59", "2026-10-24T22:00:00")]
    [InlineData("2026-10-25T01:00:00", "2026-10-24T22:00:00")]
    [InlineData("2026-10-25T22:59:59", "2026-10-24T22:00:00")]
    // Monday 2026-10-26 begins at 00:00 CET = Sunday 23:00 UTC.
    [InlineData("2026-10-25T23:00:00", "2026-10-25T23:00:00")]
    // Other years: 2027 switches on 28 March and 31 October; 2030 on 31 March and 27 October.
    [InlineData("2027-03-28T12:00:00", "2027-03-27T23:00:00")]
    [InlineData("2027-10-31T12:00:00", "2027-10-30T22:00:00")]
    [InlineData("2030-03-31T12:00:00", "2030-03-30T23:00:00")]
    [InlineData("2030-10-27T12:00:00", "2030-10-26T22:00:00")]
    public void StartOfCestDayUtc_AroundTheChangeoverDays_UsesTheOffsetInForceAtLocalMidnight(string nowUtc, string expectedUtc)
    {
        Assert.Equal(Parse(expectedUtc), CestClock.StartOfCestDayUtc(Parse(nowUtc)));
    }

    [Theory]
    [Trait("Req", "RSK-012")]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Methods_TimeThatIsNotUtc_Throw(DateTimeKind kind)
    {
        var time = new DateTime(2026, 7, 14, 10, 0, 0, kind);

        Assert.Throws<ArgumentException>(() => CestClock.IsSummerTime(time));
        Assert.Throws<ArgumentException>(() => CestClock.UtcOffset(time));
        Assert.Throws<ArgumentException>(() => CestClock.StartOfCestDayUtc(time));
    }

    [Theory]
    [Trait("Req", "RSK-012")]
    [InlineData(0)]
    [InlineData(10000)]
    public void SummerTimeStartUtc_YearOutOfRange_Throws(int year)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CestClock.SummerTimeStartUtc(year));
        Assert.Throws<ArgumentOutOfRangeException>(() => CestClock.SummerTimeEndUtc(year));
    }

    private static DateTime Parse(string text)
    {
        DateTime local = DateTime.ParseExact(text, "yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        return DateTime.SpecifyKind(local, DateTimeKind.Utc);
    }
}
