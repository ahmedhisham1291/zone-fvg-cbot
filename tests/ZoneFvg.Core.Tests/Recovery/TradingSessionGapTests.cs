using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-016 (owner decision D of 2026-11-17): the open-market time of REC-003 also counts as closed
/// every moment outside the symbol's weekly trading sessions. Sessions only add closed time to
/// the weekend closure (Friday at FridayCloseUtc 20:00 to Monday 00:00 UTC) and the holidays; they
/// never make a closed moment open. Start included, end excluded. The gold sessions are the
/// example values of <see cref="GoldData"/>: Sunday 22:00 to Friday 21:00 with a break from 21:00
/// to 22:00 on Monday to Thursday. 2026-03-03 is a Tuesday.
/// </summary>
public class TradingSessionGapTests
{
    private static readonly TimeSpan FridayClose = TimeSpan.FromHours(20.0);
    private static readonly IReadOnlyCollection<MonthDay> Defaults = MonthDay.ParseList("12-25,01-01");
    private static readonly IReadOnlyList<TradingSession> Gold = GoldData.Sessions;
    private static readonly IReadOnlyList<TradingSession> Forex = GoldData.ForexSessions;

    private static DateTime At(int day, int hour, int minute = 0, int second = 0)
    {
        return new DateTime(2026, 3, day, hour, minute, second, DateTimeKind.Utc);
    }

    private static Bar M15(DateTime open)
    {
        return BarBuilder.One(TimeFrame.M15, open, 2650.0, 2650.0, 2650.0, 2650.0);
    }

    private static IReadOnlyList<Bar> M15Series(DateTime first, int count)
    {
        return new BarBuilder(TimeFrame.M15).At(first).Repeat(count, (b, _) => b.Doji(2650.0)).Build();
    }

    private static TradingSession Session(DayOfWeek startDay, double startHours, DayOfWeek endDay, double endHours)
    {
        return new TradingSession(startDay, TimeSpan.FromHours(startHours), endDay, TimeSpan.FromHours(endHours));
    }

    // ------------------------------------------------------------------ gold break

    [Fact]
    [Trait("Req", "REC-016")]
    public void IsGap_GoldBarsAroundTheDailyBreak_NoGapWithTheSessionsAGapWithout()
    {
        // Tuesday 20:45 closes 21:00; the next bar opens 22:00 after the break.
        Bar before = M15(At(3, 20, 45));
        Bar after = M15(At(3, 22));

        Assert.False(BarSeriesCheck.IsGap(TimeFrame.M15, before, after, FridayClose, null, Gold));
        Assert.True(BarSeriesCheck.IsGap(TimeFrame.M15, before, after, FridayClose));
        Assert.True(BarSeriesCheck.IsGap(TimeFrame.M15, before, after, FridayClose, null, null));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void IsGap_GoldBarMissingInsideTheSession_IsAGapWithTheSessions()
    {
        // 20:30 then 22:00: the 20:45 bar is missing inside the session (15 minutes of open time).
        Assert.True(BarSeriesCheck.IsGap(TimeFrame.M15, M15(At(3, 20, 30)), M15(At(3, 22)), FridayClose, null, Gold));

        // 20:45 then 22:15: the 22:00 bar, the first after the break, is missing.
        Assert.True(BarSeriesCheck.IsGap(TimeFrame.M15, M15(At(3, 20, 45)), M15(At(3, 22, 15)), FridayClose, null, Gold));
    }

    [Theory]
    [Trait("Req", "REC-016")]
    [InlineData(21, 0, 0, 0)]
    [InlineData(21, 50, 0, 0)]
    [InlineData(22, 0, 0, 0)]
    [InlineData(22, 0, 1, 1)]
    [InlineData(22, 29, 59, 1799)]
    [InlineData(22, 30, 0, 1800)]
    public void OpenMarketTime_FromTheBreakStart_CountsOnlyTimeInsideTheSession(int hour, int minute, int second, int seconds)
    {
        TimeSpan open = BarSeriesCheck.OpenMarketTime(At(3, 21), At(3, hour, minute, second), FridayClose, null, Gold);

        Assert.Equal(TimeSpan.FromSeconds(seconds), open);
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void Problem_LastGoldBarBeforeTheBreak_RecentUntilThirtyMinutesOfSessionTime()
    {
        // Last bar opens 20:45 and closes 21:00. Session time since then: 0 at 21:50, 29:59 at 22:29:59, 30:00 at 22:30.
        IReadOnlyList<Bar> bars = M15Series(At(3, 20), 4);

        Assert.Null(BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(3, 21, 50), FridayClose, null, Gold));
        Assert.Null(BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(3, 22, 29, 59), FridayClose, null, Gold));
        Assert.Equal("M15: last bar closed 2026-03-03 21:00, not recent at 2026-03-03 22:30", BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(3, 22, 30), FridayClose, null, Gold));
        Assert.Null(BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, bars, 1, At(3, 22, 29, 59), FridayClose, null, Gold));
        Assert.Equal("M15: last bar closed 2026-03-03 21:00, not recent at 2026-03-03 22:30", BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, bars, 1, At(3, 22, 30), FridayClose, null, Gold));

        // Without the sessions the break counts as open: 30 minutes have passed at 21:30.
        Assert.Equal("M15: last bar closed 2026-03-03 21:00, not recent at 2026-03-03 21:30", BarSeriesCheck.Problem(TimeFrame.M15, bars, 1, At(3, 21, 30), FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void Gaps_GoldSeriesOverTheBreak_NoneWithTheSessionsOneWithout()
    {
        var bars = new List<Bar>(M15Series(At(3, 20), 4));
        bars.AddRange(M15Series(At(3, 22), 4));

        Assert.Empty(BarSeriesCheck.Gaps(TimeFrame.M15, bars, FridayClose, null, Gold));
        Assert.Null(BarSeriesCheck.Problem(TimeFrame.M15, bars, 8, At(3, 23), FridayClose, null, Gold));
        Assert.Equal(new[] { new BarGap(TimeFrame.M15, At(3, 20, 45), At(3, 22)) }, BarSeriesCheck.Gaps(TimeFrame.M15, bars, FridayClose));
        Assert.Equal("M15: gap between 2026-03-03 20:45 and 2026-03-03 22:00", BarSeriesCheck.Problem(TimeFrame.M15, bars, 8, At(3, 23), FridayClose));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void OpenMarketTime_GoldWeek_IsTheWeekdaysMinusFourBreaksAndTheFridayClose()
    {
        // Monday 00:00 to Friday 20:00 is 4 x 24 + 20 = 116 h; the breaks on Monday to Thursday remove 4 h.
        TimeSpan open = BarSeriesCheck.OpenMarketTime(At(1, 0), At(8, 0), FridayClose, null, Gold);

        Assert.Equal(TimeSpan.FromHours(112.0), open);
    }

    // ------------------------------------------------------------------ sessions never open time

    [Fact]
    [Trait("Req", "REC-016")]
    public void OpenMarketTime_SessionOverSaturday_SaturdayStaysClosed()
    {
        // A session from Friday 00:00 to Monday 00:00 covers the weekend; the weekend closure still applies.
        TradingSession[] weekend = { Session(DayOfWeek.Friday, 0.0, DayOfWeek.Monday, 0.0) };

        Assert.Equal(TimeSpan.Zero, BarSeriesCheck.OpenMarketTime(At(7, 0), At(8, 0), FridayClose, null, weekend));
        Assert.Equal(TimeSpan.Zero, BarSeriesCheck.OpenMarketTime(At(6, 20), At(9, 0), FridayClose, null, weekend));
        Assert.Equal(TimeSpan.FromHours(20.0), BarSeriesCheck.OpenMarketTime(At(6, 0), At(9, 0), FridayClose, null, weekend));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void OpenMarketTime_OnlyASaturdaySession_NothingIsOpen()
    {
        TradingSession[] saturday = { Session(DayOfWeek.Saturday, 0.0, DayOfWeek.Sunday, 0.0) };

        Assert.Equal(TimeSpan.Zero, BarSeriesCheck.OpenMarketTime(At(1, 0), At(15, 0), FridayClose, null, saturday));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void OpenMarketTime_HolidayInsideASession_StaysClosed()
    {
        // 2025-12-25 is a Thursday inside the forex session; the holiday still closes the whole day.
        var before = new DateTime(2025, 12, 24, 23, 45, 0, DateTimeKind.Utc);
        var after = new DateTime(2025, 12, 26, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TimeSpan.Zero, BarSeriesCheck.OpenMarketTime(before.AddMinutes(15.0), after, FridayClose, Defaults, Forex));
        Assert.False(BarSeriesCheck.IsGap(TimeFrame.M15, M15(before), M15(after), FridayClose, Defaults, Gold));
    }

    // ------------------------------------------------------------------ session shapes

    [Fact]
    [Trait("Req", "REC-016")]
    public void OpenMarketTime_SessionAcrossTheEndOfTheWeek_CountsOnBothSides()
    {
        // Saturday 22:00 to Monday 01:00: only Monday 00:00 to 01:00 is open (the weekend stays closed).
        TradingSession[] wrap = { Session(DayOfWeek.Saturday, 22.0, DayOfWeek.Monday, 1.0) };

        Assert.Equal(TimeSpan.FromHours(1.0), BarSeriesCheck.OpenMarketTime(At(9, 0), At(9, 2), FridayClose, null, wrap));
        Assert.Equal(TimeSpan.FromHours(1.0), BarSeriesCheck.OpenMarketTime(At(6, 0), At(10, 0), FridayClose, null, wrap));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void OpenMarketTime_EndAtTwentyFourHours_IsTheNextMidnight()
    {
        TradingSession[] monday = { new(DayOfWeek.Monday, TimeSpan.Zero, DayOfWeek.Monday, TimeSpan.FromHours(24.0)) };
        TradingSession[] saturdayToMonday = { new(DayOfWeek.Saturday, TimeSpan.FromHours(24.0), DayOfWeek.Monday, TimeSpan.FromHours(6.0)) };

        Assert.Equal(TimeSpan.FromHours(24.0), BarSeriesCheck.OpenMarketTime(At(9, 0), At(11, 0), FridayClose, null, monday));
        Assert.Equal(TimeSpan.FromHours(6.0), BarSeriesCheck.OpenMarketTime(At(9, 0), At(11, 0), FridayClose, null, saturdayToMonday));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void OpenMarketTime_OverlappingSessions_AreCountedOnce()
    {
        TradingSession[] overlapping = { Session(DayOfWeek.Monday, 6.0, DayOfWeek.Monday, 18.0), Session(DayOfWeek.Monday, 0.0, DayOfWeek.Monday, 12.0) };
        TradingSession[] contained = { Session(DayOfWeek.Monday, 0.0, DayOfWeek.Monday, 18.0), Session(DayOfWeek.Monday, 6.0, DayOfWeek.Monday, 12.0) };
        TradingSession[] touching = { Session(DayOfWeek.Monday, 0.0, DayOfWeek.Monday, 6.0), Session(DayOfWeek.Monday, 6.0, DayOfWeek.Monday, 12.0) };

        Assert.Equal(TimeSpan.FromHours(18.0), BarSeriesCheck.OpenMarketTime(At(9, 0), At(10, 0), FridayClose, null, overlapping));
        Assert.Equal(TimeSpan.FromHours(18.0), BarSeriesCheck.OpenMarketTime(At(9, 0), At(10, 0), FridayClose, null, contained));
        Assert.Equal(TimeSpan.FromHours(12.0), BarSeriesCheck.OpenMarketTime(At(9, 0), At(10, 0), FridayClose, null, touching));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void OpenMarketTime_SessionOrder_DoesNotMatter()
    {
        IReadOnlyList<TradingSession> reversed = Gold.Reverse().ToList();

        Assert.Equal(BarSeriesCheck.OpenMarketTime(At(1, 0), At(15, 0), FridayClose, null, Gold), BarSeriesCheck.OpenMarketTime(At(1, 0), At(15, 0), FridayClose, null, reversed));
    }

    // ------------------------------------------------------------------ lists that add no closed time

    public static IEnumerable<object?[]> ListsWithoutClosedTime()
    {
        yield return new object?[] { "null", null };
        yield return new object?[] { "empty", Array.Empty<TradingSession>() };
        yield return new object?[] { "whole week, start equals end", new[] { Session(DayOfWeek.Sunday, 0.0, DayOfWeek.Sunday, 0.0) } };
        yield return new object?[] { "whole week, Saturday 24:00", new[] { new TradingSession(DayOfWeek.Saturday, TimeSpan.FromHours(24.0), DayOfWeek.Sunday, TimeSpan.Zero) } };
        yield return new object?[] { "invalid time after a valid session", new[] { Gold[0], new TradingSession(DayOfWeek.Monday, TimeSpan.FromHours(25.0), DayOfWeek.Monday, TimeSpan.FromHours(26.0)) } };
        yield return new object?[] { "negative time", new[] { new TradingSession(DayOfWeek.Monday, TimeSpan.FromHours(-1.0), DayOfWeek.Monday, TimeSpan.FromHours(2.0)) } };
        yield return new object?[] { "invalid day", new[] { new TradingSession((DayOfWeek)7, TimeSpan.Zero, DayOfWeek.Monday, TimeSpan.FromHours(2.0)) } };
        yield return new object?[] { "invalid end day", new[] { new TradingSession(DayOfWeek.Monday, TimeSpan.Zero, (DayOfWeek)(-1), TimeSpan.FromHours(2.0)) } };
        yield return new object?[] { "null session", new TradingSession[] { Gold[0], null! } };
    }

    [Theory]
    [Trait("Req", "REC-016")]
    [MemberData(nameof(ListsWithoutClosedTime))]
    public void OpenMarketTime_ListNotUsable_EqualsTheModelWithoutSessions(string name, TradingSession[]? sessions)
    {
        // An empty, whole-week or invalid list falls back to the weekend and holiday model: more gaps, never fewer.
        TimeSpan expected = BarSeriesCheck.OpenMarketTime(At(1, 0), At(15, 0), FridayClose);

        Assert.True(expected == BarSeriesCheck.OpenMarketTime(At(1, 0), At(15, 0), FridayClose, null, sessions), name);
        Assert.True(BarSeriesCheck.IsGap(TimeFrame.M15, M15(At(3, 20, 45)), M15(At(3, 22)), FridayClose, null, sessions), name);
    }

    // ------------------------------------------------------------------ forex: identical to no sessions

    [Theory]
    [Trait("Req", "REC-016")]
    [InlineData(4, 10, 0, 4, 10, 15)]
    [InlineData(6, 21, 45, 8, 22, 0)]
    [InlineData(6, 19, 0, 9, 1, 0)]
    [InlineData(7, 10, 0, 9, 0, 30)]
    [InlineData(6, 19, 45, 6, 20, 15)]
    [InlineData(4, 10, 15, 4, 10, 0)]
    [InlineData(4, 10, 0, 4, 10, 0)]
    [InlineData(6, 12, 0, 13, 22, 0)]
    public void OpenMarketTime_ForexSessionsOnTheRec003Inputs_IdenticalToNoSessions(int fromDay, int fromHour, int fromMinute, int toDay, int toHour, int toMinute)
    {
        DateTime from = At(fromDay, fromHour, fromMinute);
        DateTime to = At(toDay, toHour, toMinute);

        Assert.Equal(BarSeriesCheck.OpenMarketTime(from, to, FridayClose), BarSeriesCheck.OpenMarketTime(from, to, FridayClose, null, Forex));
    }

    [Theory]
    [Trait("Req", "REC-016")]
    [InlineData(4, 10, 0, 4, 10, 15)]
    [InlineData(4, 10, 0, 4, 10, 30)]
    [InlineData(6, 19, 30, 6, 20, 0)]
    [InlineData(6, 19, 45, 8, 22, 0)]
    [InlineData(6, 21, 45, 8, 21, 0)]
    [InlineData(8, 23, 45, 9, 0, 15)]
    public void IsGap_ForexSessionsOnTheRec003M15Inputs_IdenticalToNoSessions(int fromDay, int fromHour, int fromMinute, int toDay, int toHour, int toMinute)
    {
        Bar previous = M15(At(fromDay, fromHour, fromMinute));
        Bar next = M15(At(toDay, toHour, toMinute));

        Assert.Equal(BarSeriesCheck.IsGap(TimeFrame.M15, previous, next, FridayClose), BarSeriesCheck.IsGap(TimeFrame.M15, previous, next, FridayClose, null, Forex));
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void IsGap_ForexSessionsOnTheRec003H4AndD1Inputs_IdenticalToNoSessions()
    {
        Bar h4 = BarBuilder.One(TimeFrame.H4, At(4, 0), 1.0, 1.0, 1.0, 1.0);
        foreach (int nextHour in new[] { 5, 7, 8 })
        {
            Bar next = BarBuilder.One(TimeFrame.H4, At(4, nextHour), 1.0, 1.0, 1.0, 1.0);
            Assert.Equal(BarSeriesCheck.IsGap(TimeFrame.H4, h4, next, FridayClose), BarSeriesCheck.IsGap(TimeFrame.H4, h4, next, FridayClose, null, Forex));
        }

        foreach ((int previousDay, int nextDay) in new[] { (6, 9), (3, 5), (4, 5) })
        {
            Bar previous = BarBuilder.One(TimeFrame.D1, At(previousDay, 0), 1.0, 1.0, 1.0, 1.0);
            Bar next = BarBuilder.One(TimeFrame.D1, At(nextDay, 0), 1.0, 1.0, 1.0, 1.0);
            Assert.Equal(BarSeriesCheck.IsGap(TimeFrame.D1, previous, next, FridayClose), BarSeriesCheck.IsGap(TimeFrame.D1, previous, next, FridayClose, null, Forex));
        }
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void Problem_ForexSessionsOnTheRec003SeriesInputs_IdenticalToNoSessions()
    {
        IReadOnlyList<Bar> four = M15Series(At(4, 9), 4);
        var withNull = new List<Bar>(M15Series(At(4, 9), 2)) { null! };
        var disorder = new List<Bar> { M15(At(4, 9, 15)), M15(At(4, 9)) };
        var gap = new List<Bar> { M15(At(4, 9)), M15(At(4, 9, 15)), M15(At(4, 9, 45)) };
        IReadOnlyList<Bar> friday = M15Series(At(6, 21), 4);
        var cases = new (IReadOnlyList<Bar>? Bars, int Required, DateTime Now)[]
        {
            (four, 4, At(4, 10)),
            (four, 5, At(4, 10)),
            (null, 1, At(4, 10)),
            (withNull, 1, At(4, 10)),
            (four, 1, At(4, 9, 59, 59)),
            (disorder, 1, At(4, 10)),
            (gap, 1, At(4, 10)),
            (four, 1, At(4, 10, 29, 59)),
            (four, 1, At(4, 10, 30)),
            (friday, 1, At(7, 15)),
            (friday, 1, At(9, 0, 29)),
            (friday, 1, At(9, 0, 30)),
        };

        foreach ((IReadOnlyList<Bar>? bars, int required, DateTime now) in cases)
        {
            Assert.Equal(BarSeriesCheck.Problem(TimeFrame.M15, bars, required, now, FridayClose), BarSeriesCheck.Problem(TimeFrame.M15, bars, required, now, FridayClose, null, Forex));
            Assert.Equal(BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, bars, required, now, FridayClose), BarSeriesCheck.ProblemOtherThanGap(TimeFrame.M15, bars, required, now, FridayClose, null, Forex));
            Assert.Equal(BarSeriesCheck.Gaps(TimeFrame.M15, bars, FridayClose), BarSeriesCheck.Gaps(TimeFrame.M15, bars, FridayClose, null, Forex));
        }
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void OpenMarketTime_ForexSessionsOnTheHolidayInputs_IdenticalToNoSessions()
    {
        var cases = new[]
        {
            (new DateTime(2025, 12, 25, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 12, 26, 0, 0, 0, DateTimeKind.Utc)),
            (new DateTime(2027, 12, 24, 19, 0, 0, DateTimeKind.Utc), new DateTime(2027, 12, 27, 1, 0, 0, DateTimeKind.Utc)),
            (new DateTime(2028, 12, 29, 21, 0, 0, DateTimeKind.Utc), new DateTime(2029, 1, 2, 0, 30, 0, DateTimeKind.Utc)),
        };

        foreach ((DateTime from, DateTime to) in cases)
        {
            Assert.Equal(BarSeriesCheck.OpenMarketTime(from, to, FridayClose, Defaults), BarSeriesCheck.OpenMarketTime(from, to, FridayClose, Defaults, Forex));
        }
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void OpenMarketTime_ForexSessionsSweepOverThreeWeeksWithHolidays_IdenticalToNoSessions()
    {
        // Every 15-minute start from Monday 2025-12-22 to Monday 2026-01-12 (Christmas and New Year),
        // with Friday closes 20:00, 21:00 and 22:00, for durations from 15 minutes to three days.
        var first = new DateTime(2025, 12, 22, 0, 0, 0, DateTimeKind.Utc);
        TimeSpan[] durations = { TimeSpan.FromMinutes(15.0), TimeSpan.FromMinutes(30.0), TimeSpan.FromHours(1.0), TimeSpan.FromHours(4.0), TimeSpan.FromDays(1.0), TimeSpan.FromDays(3.0) };
        foreach (double fridayHour in new[] { 20.0, 21.0, 22.0 })
        {
            TimeSpan fridayClose = TimeSpan.FromHours(fridayHour);
            for (DateTime from = first; from < first.AddDays(21.0); from = from.AddMinutes(15.0))
            {
                foreach (TimeSpan duration in durations)
                {
                    TimeSpan without = BarSeriesCheck.OpenMarketTime(from, from + duration, fridayClose, Defaults);
                    TimeSpan with = BarSeriesCheck.OpenMarketTime(from, from + duration, fridayClose, Defaults, Forex);
                    Assert.True(without == with, "from " + from.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + " for " + duration + ", Friday close " + fridayClose + ": " + without + " vs " + with);
                }
            }
        }
    }

    // ------------------------------------------------------------------ TradingSession texts

    [Fact]
    [Trait("Req", "REC-016")]
    public void Text_GoldSession_IsShortDayAndTime()
    {
        Assert.Equal("Sun 22:00-Mon 21:00", Gold[0].Text);
        Assert.Equal("Sat 24:00-Mon 06:30:15", new TradingSession(DayOfWeek.Saturday, TimeSpan.FromHours(24.0), DayOfWeek.Monday, new TimeSpan(6, 30, 15)).Text);
        Assert.True(Gold[0].IsValid);
    }

    [Fact]
    [Trait("Req", "REC-016")]
    public void ListText_EmptyGoldAndInvalidLists_AreWrittenForTheStartLog()
    {
        Assert.Equal("none reported", TradingSession.ListText(Array.Empty<TradingSession>()));
        Assert.Equal("none reported", TradingSession.ListText(null));
        Assert.Equal("Sun 22:00-Mon 21:00,Mon 22:00-Tue 21:00,Tue 22:00-Wed 21:00,Wed 22:00-Thu 21:00,Thu 22:00-Fri 21:00", TradingSession.ListText(Gold));

        var invalid = new TradingSession((DayOfWeek)7, TimeSpan.FromHours(25.0), DayOfWeek.Monday, TimeSpan.FromHours(-1.0));
        Assert.False(invalid.IsValid);
        Assert.Equal("Sun 22:00-Mon 21:00,7 1.01:00:00-Mon -01:00:00 (invalid, not used)", TradingSession.ListText(new[] { Gold[0], invalid }));
        Assert.Equal("Sun 22:00-Mon 21:00,null (invalid, not used)", TradingSession.ListText(new TradingSession?[] { Gold[0], null }));
    }
}
