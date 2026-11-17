using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-003 as changed by owner decision A of 2026-11-17: recovery fails only on problems other
/// than a gap (<see cref="BarSeriesCheck.ProblemOtherThanGap"/>) and lists the gaps separately
/// (<see cref="BarSeriesCheck.Gaps"/>). <see cref="BarSeriesCheck.Problem"/> keeps reporting gaps
/// (see <see cref="BarSeriesCheckTests"/>). Wednesday 2026-03-04; FridayCloseUtc 20:00.
/// </summary>
public class BarSeriesGapListTests
{
    private static readonly TimeSpan FridayClose = TimeSpan.FromHours(20.0);

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

    [Fact]
    [Trait("Req", "REC-003")]
    public void ProblemOtherThanGap_SeriesWithAGap_IsNullWhileProblemReportsTheGap()
    {
        var bars = new List<Bar> { M15(At(4, 9)), M15(At(4, 9, 15)), M15(At(4, 9, 45)) };

        Assert.Null(BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, bars, 1, At(4, 10), FridayClose));
        Assert.Equal("M15: gap between 2026-03-04 09:15 and 2026-03-04 09:45", BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(4, 10), FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void ProblemOtherThanGap_EveryOtherProblem_ReportedWithTheTextsOfProblem()
    {
        IReadOnlyList<Bar> four = M15Series(At(4, 9), 4);
        var withNull = new List<Bar>(M15Series(At(4, 9), 2)) { null! };
        var disorder = new List<Bar> { M15(At(4, 9, 15)), M15(At(4, 9)) };

        Assert.Equal("M15: 4 bars, 5 needed", BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, four, 5, At(4, 10), FridayClose));
        Assert.Equal("D1: no bars", BarSeriesCheck.ProblemOtherThanGap(TimeFrame.D1, null, 1, At(4, 10), FridayClose));
        Assert.Equal("M15: bar 2 is missing", BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, withNull, 1, At(4, 10), FridayClose));
        Assert.Equal("M15: bar opening 2026-03-04 09:45 has not closed at 2026-03-04 09:59", BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, four, 1, At(4, 9, 59, 59), FridayClose));
        Assert.Equal("M15: bar opening 2026-03-04 09:00 does not follow 2026-03-04 09:15", BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, disorder, 1, At(4, 10), FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void ProblemOtherThanGap_LastBarExactlyTwoBarsOld_IsNotRecent_OneSecondEarlierItIs()
    {
        IReadOnlyList<Bar> bars = M15Series(At(4, 9), 4);

        Assert.Null(BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, bars, 1, At(4, 10, 29, 59), FridayClose));
        Assert.Equal("M15: last bar closed 2026-03-04 10:00, not recent at 2026-03-04 10:30", BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, bars, 1, At(4, 10, 30), FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Gaps_ListsEveryGapOldestFirstWithBothOpenTimes()
    {
        var bars = new List<Bar> { M15(At(4, 9)), M15(At(4, 9, 30)), M15(At(4, 9, 45)), M15(At(4, 10, 30)) };

        IReadOnlyList<BarGap> gaps = BarSeriesCheck.Gaps(TimeFrame.M15, bars, FridayClose);

        Assert.Equal(new[] { new BarGap(TimeFrame.M15, At(4, 9), At(4, 9, 30)), new BarGap(TimeFrame.M15, At(4, 9, 45), At(4, 10, 30)) }, gaps);
        Assert.Equal("2026-03-04 09:00>2026-03-04 09:30", gaps[0].Text);
    }

    [Fact]
    [Trait("Req", "REC-003")]
    public void Gaps_WeekendHolidayCompleteNullOrDisorderedSeries_HaveNone()
    {
        var weekend = new List<Bar> { M15(At(6, 19, 45)), M15(At(9, 0)) };
        var holiday = new List<Bar>
        {
            BarBuilder.One(TimeFrame.M15, new DateTime(2025, 12, 24, 23, 45, 0, DateTimeKind.Utc), 1.0, 1.0, 1.0, 1.0),
            BarBuilder.One(TimeFrame.M15, new DateTime(2025, 12, 26, 0, 0, 0, DateTimeKind.Utc), 1.0, 1.0, 1.0, 1.0),
        };
        var withNull = new List<Bar> { M15(At(4, 9)), null!, M15(At(4, 10)) };
        var disorder = new List<Bar> { M15(At(4, 10)), M15(At(4, 9)) };

        Assert.Empty(BarSeriesCheck.Gaps(TimeFrame.M15, weekend, FridayClose));
        Assert.Empty(BarSeriesCheck.Gaps(TimeFrame.M15, holiday, FridayClose, MonthDay.ParseList("12-25,01-01")));
        Assert.Single(BarSeriesCheck.Gaps(TimeFrame.M15, holiday, FridayClose));
        Assert.Empty(BarSeriesCheck.Gaps(TimeFrame.M15, M15Series(At(4, 9), 4), FridayClose));
        Assert.Empty(BarSeriesCheck.Gaps(TimeFrame.M15, null, FridayClose));
        Assert.Empty(BarSeriesCheck.Gaps(TimeFrame.M15, withNull, FridayClose));
        Assert.Empty(BarSeriesCheck.Gaps(TimeFrame.M15, disorder, FridayClose));
    }
}
