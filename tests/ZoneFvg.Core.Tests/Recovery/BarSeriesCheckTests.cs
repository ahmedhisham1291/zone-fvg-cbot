using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-003: gap and completeness rules for bar series. The weekend closure runs from Friday at
/// FridayCloseUtc (20:00) to Monday 00:00 UTC. Two consecutive bars have a gap when at least one
/// bar length of open-market time lies between the first close and the second open; the last bar
/// is recent while less than two bar lengths of open-market time have passed since its close.
/// </summary>
public class BarSeriesCheckTests
{
    private static readonly TimeSpan FridayClose = TimeSpan.FromHours(20.0);

    // Wednesday 2026-03-04, Friday 2026-03-06, Saturday 7, Sunday 8, Monday 9.
    private static DateTime At(int day, int hour, int minute = 0, int second = 0)
    {
        return new DateTime(2026, 3, day, hour, minute, second, DateTimeKind.Utc);
    }

    private static Bar M15(DateTime open)
    {
        return BarBuilder.One(TimeFrame.M15, open, 1.0, 1.0, 1.0, 1.0);
    }

    private static IReadOnlyList<Bar> M15Series(DateTime first, int count)
    {
        return new BarBuilder(TimeFrame.M15).At(first).Repeat(count, (b, _) => b.Doji(1.0)).Build();
    }

    [Theory]
    [Trait("Req", "REC-003")]
    [InlineData(4, 10, 0, 4, 10, 15, 15.0)]
    [InlineData(6, 21, 45, 8, 22, 0, 0.0)]
    [InlineData(6, 19, 0, 9, 1, 0, 120.0)]
    [InlineData(7, 10, 0, 9, 0, 30, 30.0)]
    [InlineData(6, 19, 45, 6, 20, 15, 15.0)]
    [InlineData(4, 10, 15, 4, 10, 0, 0.0)]
    [InlineData(4, 10, 0, 4, 10, 0, 0.0)]
    public void OpenMarketTime_CountsOnlyTimeOutsideTheWeekendClosure(int fromDay, int fromHour, int fromMinute, int toDay, int toHour, int toMinute, double minutes)
    {
        TimeSpan open = BarSeriesCheck.OpenMarketTime(At(fromDay, fromHour, fromMinute), At(toDay, toHour, toMinute), FridayClose);

        Assert.Equal(TimeSpan.FromMinutes(minutes), open);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void OpenMarketTime_OverTwoWeekends_SubtractsBoth()
    {
        // Fri 6 12:00 to Fri 13 22:00 is 178 h; closed Fri 6 20:00 to Mon 9 00:00 (52 h) and Fri 13 20:00 to 22:00 (2 h).
        TimeSpan open = BarSeriesCheck.OpenMarketTime(At(6, 12), At(13, 22), FridayClose);

        Assert.Equal(TimeSpan.FromHours(124.0), open);
    }

    [Theory]
    [Trait("Req", "REC-003")]
    [InlineData(4, 10, 0, 4, 10, 15, false)]
    [InlineData(4, 10, 0, 4, 10, 30, true)]
    [InlineData(6, 19, 30, 6, 20, 0, true)]
    [InlineData(6, 19, 45, 8, 22, 0, false)]
    [InlineData(6, 21, 45, 8, 21, 0, false)]
    [InlineData(8, 23, 45, 9, 0, 15, true)]
    public void IsGap_M15_MissingBarOnlyWhereTheMarketWasOpen(int fromDay, int fromHour, int fromMinute, int toDay, int toHour, int toMinute, bool gap)
    {
        Bar previous = M15(At(fromDay, fromHour, fromMinute));
        Bar next = M15(At(toDay, toHour, toMinute));

        Assert.Equal(gap, BarSeriesCheck.IsGap(TimeFrame.M15, previous, next, FridayClose));
    }

    [Theory]
    [Trait("Req", "REC-003")]
    [InlineData(5, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    public void IsGap_H4_ToleratesAOneHourShiftButNotAMissingBar(int nextHour, bool gap)
    {
        Bar previous = BarBuilder.One(TimeFrame.H4, At(4, 0), 1.0, 1.0, 1.0, 1.0);
        Bar next = BarBuilder.One(TimeFrame.H4, At(4, nextHour), 1.0, 1.0, 1.0, 1.0);

        Assert.Equal(gap, BarSeriesCheck.IsGap(TimeFrame.H4, previous, next, FridayClose));
    }

    [Theory]
    [Trait("Req", "REC-003")]
    [InlineData(6, 9, false)]
    [InlineData(3, 5, true)]
    [InlineData(4, 5, false)]
    public void IsGap_D1_WeekendDaysAreNotMissing(int previousDay, int nextDay, bool gap)
    {
        Bar previous = BarBuilder.One(TimeFrame.D1, At(previousDay, 0), 1.0, 1.0, 1.0, 1.0);
        Bar next = BarBuilder.One(TimeFrame.D1, At(nextDay, 0), 1.0, 1.0, 1.0, 1.0);

        Assert.Equal(gap, BarSeriesCheck.IsGap(TimeFrame.D1, previous, next, FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void IsGap_NullBar_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => BarSeriesCheck.IsGap(TimeFrame.M15, null!, M15(At(4, 10)), FridayClose));
        Assert.Throws<ArgumentNullException>(() => BarSeriesCheck.IsGap(TimeFrame.M15, M15(At(4, 10)), null!, FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Problem_CompleteRecentSeries_IsNull()
    {
        IReadOnlyList<Bar> bars = M15Series(At(4, 9), 4);

        Assert.Null(BarSeriesCheck.Problem(TimeFrame.M15, bars, 4, At(4, 10), FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Problem_FewerBarsThanRequired_IsReported()
    {
        IReadOnlyList<Bar> bars = M15Series(At(4, 9), 4);

        Assert.Equal("M15: 4 bars, 5 needed", BarSeriesCheck.Problem(TimeFrame.M15, bars, 5, At(4, 10), FridayClose));
        Assert.Equal("D1: no bars", BarSeriesCheck.Problem(TimeFrame.D1, null, 1, At(4, 10), FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Problem_NullBarFormingBarOrDisorder_AreReported()
    {
        var withNull = new List<Bar>(M15Series(At(4, 9), 2)) { null! };
        IReadOnlyList<Bar> forming = M15Series(At(4, 9), 4);
        var disorder = new List<Bar> { M15(At(4, 9, 15)), M15(At(4, 9)) };

        Assert.Equal("M15: bar 2 is missing", BarSeriesCheck.Problem(TimeFrame.M15, withNull, 1, At(4, 10), FridayClose));
        Assert.Equal("M15: bar opening 2026-03-04 09:45 has not closed at 2026-03-04 09:59", BarSeriesCheck.Problem(TimeFrame.M15, forming, 1, At(4, 9, 59, 59), FridayClose));
        Assert.Equal("M15: bar opening 2026-03-04 09:00 does not follow 2026-03-04 09:15", BarSeriesCheck.Problem(TimeFrame.M15, disorder, 1, At(4, 10), FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Problem_GapOnAWeekday_IsReported()
    {
        var bars = new List<Bar> { M15(At(4, 9)), M15(At(4, 9, 15)), M15(At(4, 9, 45)) };

        Assert.Equal("M15: gap between 2026-03-04 09:15 and 2026-03-04 09:45", BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(4, 10), FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Problem_LastBarTwoBarsOld_IsNotRecent_OneSecondEarlierItIs()
    {
        // The last bar closes at 10:00. At 10:29:59 less than 30 minutes of open market have passed.
        IReadOnlyList<Bar> bars = M15Series(At(4, 9), 4);

        Assert.Null(BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(4, 10, 29, 59), FridayClose));
        Assert.Equal("M15: last bar closed 2026-03-04 10:00, not recent at 2026-03-04 10:30", BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(4, 10, 30), FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Problem_SaturdayAfterTheLastFridayBar_IsRecent()
    {
        IReadOnlyList<Bar> bars = M15Series(At(6, 21), 4);

        Assert.Null(BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(7, 15), FridayClose));
        Assert.Null(BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(9, 0, 29), FridayClose));
        Assert.NotNull(BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(9, 0, 30), FridayClose));
    }
}
