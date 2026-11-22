using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-018: <see cref="BarSeriesCheck.ProblemOtherThanGap"/> takes the number of bar lengths of
/// open-market time after which the last bar is stale (default 2, the limit of REC-003; below 1
/// throws). Open-market time as in REC-003: Saturday and Sunday closed, Friday open until
/// FridayCloseUtc (20:00), a listed holiday closed for the whole UTC day. The D1 bars open at
/// 22:00 UTC, so a bar closes at 22:00 the next day.
/// </summary>
public class BarSeriesCheckStaleLimitTests
{
    private static readonly TimeSpan FridayClose = TimeSpan.FromHours(20.0);

    // Monday 2026-03-02, Tuesday 3, Wednesday 4, Thursday 5, Friday 6, Monday 9, Tuesday 10.
    private static DateTime At(int day, int hour, int minute = 0)
    {
        return new DateTime(2026, 3, day, hour, minute, 0, DateTimeKind.Utc);
    }

    // One D1 bar that opens at 22:00 on the given day and closes at 22:00 on the next.
    private static IReadOnlyList<Bar> D1Closing(int openDay)
    {
        return new[] { BarBuilder.One(TimeFrame.D1, At(openDay, 22), 1.0, 1.0, 1.0, 1.0) };
    }

    [Theory]
    [Trait("Req", "REC-018")]
    [InlineData(4, 22, 0, true)]
    [InlineData(4, 21, 59, false)]
    [InlineData(4, 22, 1, true)]
    [InlineData(3, 22, 0, false)]
    public void ProblemOtherThanGap_D1LimitOne_StaleFromOneBarLengthOfOpenMarketTime(int day, int hour, int minute, bool stale)
    {
        // The bar closes Tuesday 22:00; Wednesday 22:00 is 24 hours of open market later.
        string? problem = BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, D1Closing(2), 1, At(day, hour, minute), FridayClose, staleBarLengths: 1);

        if (stale)
        {
            Assert.Equal("D1: last bar closed 2026-03-03 22:00, not recent at " + At(day, hour, minute).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), problem);
        }
        else
        {
            Assert.Null(problem);
        }
    }

    [Theory]
    [Trait("Req", "REC-018")]
    [InlineData(4, 22, 0)]
    [InlineData(4, 21, 59)]
    [InlineData(5, 21, 59)]
    public void ProblemOtherThanGap_D1DefaultLimit_RecentUpToTwoBarLengths(int day, int hour, int minute)
    {
        IReadOnlyList<Bar> bars = D1Closing(2);

        Assert.Null(BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, bars, 1, At(day, hour, minute), FridayClose));
        Assert.Null(BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, bars, 1, At(day, hour, minute), FridayClose, staleBarLengths: 2));
    }

    [Fact]
    [Trait("Req", "REC-018")]
    public void ProblemOtherThanGap_D1DefaultLimit_StaleAfterTwoBarLengthsAsToday()
    {
        const string expected = "D1: last bar closed 2026-03-03 22:00, not recent at 2026-03-05 22:00";

        Assert.Equal(expected, BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, D1Closing(2), 1, At(5, 22), FridayClose));
        Assert.Equal(expected, BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, D1Closing(2), 1, At(5, 22), FridayClose, staleBarLengths: 2));
    }

    [Theory]
    [Trait("Req", "REC-018")]
    [InlineData(5, 21, 59, false)]
    [InlineData(5, 22, 0, false)]
    [InlineData(6, 20, 0, false)]
    [InlineData(9, 1, 59, false)]
    [InlineData(9, 2, 0, true)]
    public void ProblemOtherThanGap_LimitThree_StaleFromThreeBarLengths(int day, int hour, int minute, bool stale)
    {
        // Closed Tuesday 22:00: Tuesday 2 h, Wednesday 24 h, Thursday 24 h, Friday to 20:00 20 h (70 h),
        // the weekend closed, then Monday 9 from 00:00: 72 hours of open market at Monday 02:00.
        string? problem = BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, D1Closing(2), 1, At(day, hour, minute), FridayClose, staleBarLengths: 3);

        Assert.Equal(stale, problem != null);
    }

    [Theory]
    [Trait("Req", "REC-018")]
    [InlineData(9, 23, 59, false)]
    [InlineData(10, 0, 0, true)]
    public void ProblemOtherThanGap_D1LimitOne_WeekendIsClosed(int day, int hour, int minute, bool stale)
    {
        // Friday's bar closes Friday 22:00, after the Friday close at 20:00; the market opens again on
        // Monday 00:00, and 24 hours of open market have passed at Tuesday 00:00.
        string? problem = BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, D1Closing(5), 1, At(day, hour, minute), FridayClose, staleBarLengths: 1);

        if (stale)
        {
            Assert.Equal("D1: last bar closed 2026-03-06 22:00, not recent at 2026-03-10 00:00", problem);
        }
        else
        {
            Assert.Null(problem);
        }
    }

    [Fact]
    [Trait("Req", "REC-018")]
    public void ProblemOtherThanGap_D1LimitOne_ListedHolidayIsClosed()
    {
        // Closed Tuesday 22:00; Wednesday 4 March is a holiday. At Thursday 07:00: 2 h on Tuesday,
        // none on Wednesday, 7 h on Thursday = 9 h. Without the holiday: 2 + 24 + 7 = 33 h.
        IReadOnlyCollection<MonthDay> holidays = MonthDay.ParseList("12-25,03-04");

        Assert.Null(BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, D1Closing(2), 1, At(5, 7), FridayClose, holidays, staleBarLengths: 1));
        Assert.Equal(
            "D1: last bar closed 2026-03-03 22:00, not recent at 2026-03-05 07:00",
            BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, D1Closing(2), 1, At(5, 7), FridayClose, MonthDay.ParseList("12-25"), staleBarLengths: 1));
    }

    [Theory]
    [Trait("Req", "REC-018")]
    [InlineData(0)]
    [InlineData(-1)]
    public void ProblemOtherThanGap_LimitBelowOne_Throws(int limit)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, D1Closing(2), 1, At(3, 23), FridayClose, staleBarLengths: limit));

        Assert.Equal("staleBarLengths", exception.ParamName);
    }

    [Fact]
    [Trait("Req", "REC-018")]
    public void ProblemOtherThanGap_LimitOneOnM15_IsOneBarLengthToo()
    {
        // The rule counts bar lengths of the series' own timeframe: an M15 bar that closed 15 minutes
        // ago is stale with the limit 1 and recent with the default.
        IReadOnlyList<Bar> bars = new[] { BarBuilder.One(TimeFrame.M15, At(4, 10), 1.0, 1.0, 1.0, 1.0) };

        Assert.Null(BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, bars, 1, At(4, 10, 29), FridayClose, staleBarLengths: 1));
        Assert.Equal(
            "M15: last bar closed 2026-03-04 10:15, not recent at 2026-03-04 10:30",
            BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, bars, 1, At(4, 10, 30), FridayClose, staleBarLengths: 1));
        Assert.Null(BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, bars, 1, At(4, 10, 30), FridayClose));
    }
}
