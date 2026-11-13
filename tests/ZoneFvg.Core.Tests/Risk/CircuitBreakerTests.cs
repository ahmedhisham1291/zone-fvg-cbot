using Xunit;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-010, request and error limits: order requests per instance &lt;= 20 per hour and &lt;=
/// MaxRequestsPerDay (100) per CE(S)T day; unhandled errors &lt;= 5 per hour. Exceeding a limit
/// returns Halt. The hour is a sliding window: an event exactly one hour old has left it. The
/// day is the CE(S)T day of the given server time.
/// </summary>
public class CircuitBreakerTests
{
    // 2026-01-14 10:00 UTC; the CE(S)T day began 2026-01-13 23:00 UTC.
    private static readonly DateTime Ten = RiskData.Now;

    [Fact]
    [Trait("Req", "RSK-010")]
    public void RequestCounter_TwentyRequestsInAnHour_DoNotHalt()
    {
        var counter = new RequestCounter(RiskData.Parameters);
        CircuitBreakerDecision last = counter.Evaluate(Ten);

        for (int i = 0; i < 20; i++)
        {
            last = counter.Record(Ten.AddMinutes(i));
        }

        Assert.False(last.Halt);
        Assert.Equal(20, last.LastHourCount);
        Assert.Equal(20, last.HourLimit);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void RequestCounter_TwentyFirstRequestInAnHour_Halts()
    {
        var counter = new RequestCounter(RiskData.Parameters);
        for (int i = 0; i < 20; i++)
        {
            counter.Record(Ten.AddMinutes(i));
        }

        CircuitBreakerDecision decision = counter.Record(Ten.AddMinutes(20.0));

        Assert.True(decision.Halt);
        Assert.Equal(21, decision.LastHourCount);
        Assert.Equal("requests in the last hour 21 above 20", decision.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void RequestCounter_RequestExactlyOneHourOld_HasLeftTheWindow()
    {
        // 20 requests at 10:00:00. At 11:00:00 they are exactly one hour old and no longer count;
        // at 10:59:59 they still count, so a request then is the 21st.
        var early = new RequestCounter(RiskData.Parameters);
        var late = new RequestCounter(RiskData.Parameters);
        for (int i = 0; i < 20; i++)
        {
            early.Record(Ten);
            late.Record(Ten);
        }

        CircuitBreakerDecision inside = early.Record(Ten.AddHours(1.0).AddSeconds(-1.0));
        CircuitBreakerDecision outside = late.Record(Ten.AddHours(1.0));

        Assert.True(inside.Halt);
        Assert.False(outside.Halt);
        Assert.Equal(1, outside.LastHourCount);
        Assert.Equal(21, outside.TodayCount);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void RequestCounter_HundredRequestsInADay_DoNotHalt_AndTheHundredAndFirstHalts()
    {
        // Ten requests in each of ten hours, 00:00 to 09:54 UTC on 2026-01-14 (all in the CE(S)T day that began
        // 2026-01-13 23:00 UTC). No hour holds more than 10. The 101st, at 10:30, exceeds the daily 100.
        var counter = new RequestCounter(RiskData.Parameters);
        DateTime start = new(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);
        CircuitBreakerDecision last = counter.Evaluate(start);
        for (int hour = 0; hour < 10; hour++)
        {
            for (int k = 0; k < 10; k++)
            {
                last = counter.Record(start.AddHours(hour).AddMinutes(k * 6));
            }
        }

        CircuitBreakerDecision extra = counter.Record(Ten.AddMinutes(30.0));

        Assert.False(last.Halt);
        Assert.Equal(100, last.TodayCount);
        Assert.True(extra.Halt);
        Assert.Equal(101, extra.TodayCount);
        Assert.Equal(100, extra.DayLimit);
        Assert.Equal("requests today 101 above 100", extra.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void RequestCounter_RequestsBeforeTheCestDayStart_DoNotCountForTheDay()
    {
        // 50 requests yesterday evening (2026-01-13 18:00 to 22:54 UTC, before the 23:00 UTC day start),
        // then 100 today: no halt.
        var counter = new RequestCounter(RiskData.Parameters);
        DateTime evening = new(2026, 1, 13, 18, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 50; i++)
        {
            counter.Record(evening.AddMinutes(i * 6));
        }

        CircuitBreakerDecision last = counter.Evaluate(Ten);
        DateTime start = new(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 100; i++)
        {
            last = counter.Record(start.AddMinutes(i * 6));
        }

        Assert.False(last.Halt);
        Assert.Equal(100, last.TodayCount);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void RequestCounter_ServerTimeSteppingBack_DoesNotShrinkTheWindow()
    {
        // 21 requests at 10:00; asking at 09:00 still evaluates at the latest time seen (10:00): halt.
        var counter = new RequestCounter(RiskData.Parameters);
        for (int i = 0; i < 21; i++)
        {
            counter.Record(Ten);
        }

        Assert.True(counter.Evaluate(Ten.AddHours(-1.0)).Halt);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void RequestCounter_OldRequests_StopCountingAsTimeMovesOn()
    {
        var counter = new RequestCounter(RiskData.Parameters);
        for (int i = 0; i < 21; i++)
        {
            counter.Record(Ten);
        }

        CircuitBreakerDecision nextDay = counter.Evaluate(Ten.AddDays(1.0));

        Assert.False(nextDay.Halt);
        Assert.Equal(0, nextDay.LastHourCount);
        Assert.Equal(0, nextDay.TodayCount);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void ErrorCounter_FiveErrorsInAnHour_DoNotHalt_AndTheSixthHalts()
    {
        var counter = new ErrorCounter(RiskData.Parameters);
        CircuitBreakerDecision fifth = counter.Evaluate(Ten);
        for (int i = 0; i < 5; i++)
        {
            fifth = counter.Record(Ten.AddMinutes(i * 10));
        }

        CircuitBreakerDecision sixth = counter.Record(Ten.AddMinutes(50.0));

        Assert.False(fifth.Halt);
        Assert.True(sixth.Halt);
        Assert.Equal(6, sixth.LastHourCount);
        Assert.Equal(5, sixth.HourLimit);
        Assert.Null(sixth.DayLimit);
        Assert.Equal("errors in the last hour 6 above 5", sixth.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void ErrorCounter_ErrorsSpreadOverMoreThanAnHour_DoNotHalt()
    {
        // Six errors 15 minutes apart: at most five fall in any one-hour window (an error exactly 60 minutes old has left).
        var counter = new ErrorCounter(RiskData.Parameters);
        CircuitBreakerDecision last = counter.Evaluate(Ten);
        for (int i = 0; i < 6; i++)
        {
            last = counter.Record(Ten.AddMinutes(i * 15));
        }

        Assert.False(last.Halt);
        Assert.Equal(4, last.LastHourCount);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void ErrorCounter_ManyErrorsInADay_DoNotHaltWithoutAnHourlyExcess()
    {
        var counter = new ErrorCounter(RiskData.Parameters);
        CircuitBreakerDecision last = counter.Evaluate(Ten);
        DateTime start = new(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 50; i++)
        {
            last = counter.Record(start.AddMinutes(i * 13));
        }

        Assert.False(last.Halt);
        Assert.Equal(50, last.TodayCount);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void Counters_CustomLimits_AreUsed()
    {
        RiskParameters strict = RiskData.Parameters with { MaxRequestsPerHour = 1, MaxRequestsPerDay = 2, MaxErrorsPerHour = 1 };
        var requests = new RequestCounter(strict);
        var errors = new ErrorCounter(strict);

        Assert.False(requests.Record(Ten).Halt);
        Assert.True(requests.Record(Ten).Halt);
        Assert.False(errors.Record(Ten).Halt);
        Assert.True(errors.Record(Ten).Halt);
    }

    [Theory]
    [Trait("Req", "RSK-010")]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Counters_TimeThatIsNotUtc_Throws(DateTimeKind kind)
    {
        var requests = new RequestCounter(RiskData.Parameters);
        var errors = new ErrorCounter(RiskData.Parameters);
        DateTime notUtc = DateTime.SpecifyKind(Ten, kind);

        Assert.Throws<ArgumentException>(() => requests.Record(notUtc));
        Assert.Throws<ArgumentException>(() => requests.Evaluate(notUtc));
        Assert.Throws<ArgumentException>(() => errors.Record(notUtc));
        Assert.Throws<ArgumentException>(() => errors.Evaluate(notUtc));
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void Counters_InvalidParameters_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RequestCounter(null!));
        Assert.Throws<ArgumentNullException>(() => new ErrorCounter(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestCounter(new RiskParameters()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ErrorCounter(new RiskParameters()));
    }
}
