using Xunit;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-010, check before sending (owner decision of 2026-11-14, prompt WP07): the request limit is
/// checked before a request is sent. <see cref="RequestCounter.CheckBeforeRequest"/> answers
/// whether one more request would exceed the hourly (20) or daily (100) limit, without recording
/// anything. The request that would exceed a limit is never sent.
/// </summary>
public class RequestPreCheckTests
{
    // 2026-01-14 10:00 UTC; the CE(S)T day began 2026-01-13 23:00 UTC.
    private static readonly DateTime Ten = RiskData.Now;

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckBeforeRequest_NineteenSent_AllowsTheTwentieth()
    {
        var counter = new RequestCounter(RiskData.Parameters);
        for (int i = 0; i < 19; i++)
        {
            counter.Record(Ten.AddMinutes(i));
        }

        CircuitBreakerDecision decision = counter.CheckBeforeRequest(Ten.AddMinutes(19.0));

        Assert.False(decision.Halt);
        Assert.Equal(19, decision.LastHourCount);
        Assert.Equal(string.Empty, decision.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckBeforeRequest_TwentySent_RefusesTheTwentyFirstAndRecordsNothing()
    {
        var counter = new RequestCounter(RiskData.Parameters);
        for (int i = 0; i < 20; i++)
        {
            counter.Record(Ten.AddMinutes(i));
        }

        CircuitBreakerDecision decision = counter.CheckBeforeRequest(Ten.AddMinutes(20.0));
        CircuitBreakerDecision after = counter.Evaluate(Ten.AddMinutes(20.0));

        Assert.True(decision.Halt);
        Assert.Equal(20, decision.LastHourCount);
        Assert.Equal(20, decision.HourLimit);
        Assert.Equal("requests in the last hour would be 21 above 20", decision.Detail);

        // Nothing was recorded: 20 requests, at the limit but not above it.
        Assert.False(after.Halt);
        Assert.Equal(20, after.LastHourCount);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckBeforeRequest_TwentySentExactlyOneHourEarlier_AllowsTheNext()
    {
        // 20 requests at 10:00:00. At 11:00:00 they are exactly one hour old and have left the window;
        // at 10:59:59 they still count, so a 21st would exceed the limit.
        var counter = new RequestCounter(RiskData.Parameters);
        for (int i = 0; i < 20; i++)
        {
            counter.Record(Ten);
        }

        CircuitBreakerDecision inside = counter.CheckBeforeRequest(Ten.AddHours(1.0).AddSeconds(-1.0));
        CircuitBreakerDecision outside = counter.CheckBeforeRequest(Ten.AddHours(1.0));

        Assert.True(inside.Halt);
        Assert.False(outside.Halt);
        Assert.Equal(0, outside.LastHourCount);
        Assert.Equal(20, outside.TodayCount);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckBeforeRequest_HundredSentToday_RefusesTheHundredAndFirst()
    {
        // Ten requests in each of ten hours from 00:00 UTC (CE(S)T day started 2026-01-13 23:00 UTC); no hour above 10.
        var counter = new RequestCounter(RiskData.Parameters);
        DateTime start = new(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);
        for (int hour = 0; hour < 10; hour++)
        {
            for (int k = 0; k < 10; k++)
            {
                counter.Record(start.AddHours(hour).AddMinutes(k * 6));
            }
        }

        CircuitBreakerDecision empty = new RequestCounter(RiskData.Parameters).CheckBeforeRequest(Ten);
        CircuitBreakerDecision decision = counter.CheckBeforeRequest(Ten.AddMinutes(30.0));

        Assert.False(empty.Halt);
        Assert.True(decision.Halt);
        Assert.Equal(100, decision.TodayCount);
        Assert.Equal(100, decision.DayLimit);
        Assert.Equal("requests today would be 101 above 100", decision.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckBeforeRequest_NinetyNineSentToday_AllowsTheHundredth()
    {
        var counter = new RequestCounter(RiskData.Parameters);
        DateTime start = new(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 99; i++)
        {
            counter.Record(start.AddMinutes(i * 6));
        }

        CircuitBreakerDecision decision = counter.CheckBeforeRequest(Ten.AddMinutes(30.0));

        Assert.False(decision.Halt);
        Assert.Equal(99, decision.TodayCount);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckBeforeRequest_CustomLimits_AreUsed()
    {
        var counter = new RequestCounter(RiskData.Parameters with { MaxRequestsPerHour = 1, MaxRequestsPerDay = 2 });

        Assert.False(counter.CheckBeforeRequest(Ten).Halt);
        counter.Record(Ten);
        Assert.True(counter.CheckBeforeRequest(Ten).Halt);
        Assert.False(counter.CheckBeforeRequest(Ten.AddHours(1.0)).Halt);
        counter.Record(Ten.AddHours(1.0));
        Assert.Equal("requests today would be 3 above 2", counter.CheckBeforeRequest(Ten.AddHours(2.0)).Detail);
    }

    [Fact]
    [Trait("Req", "RSK-010")]
    public void CheckBeforeRequest_ServerTimeSteppingBack_UsesTheLatestTimeSeen()
    {
        var counter = new RequestCounter(RiskData.Parameters);
        for (int i = 0; i < 20; i++)
        {
            counter.Record(Ten);
        }

        // Asking at 08:00 still evaluates at 10:00, the latest time seen: the window holds 20.
        Assert.True(counter.CheckBeforeRequest(Ten.AddHours(-2.0)).Halt);
    }

    [Theory]
    [Trait("Req", "RSK-010")]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void CheckBeforeRequest_TimeThatIsNotUtc_Throws(DateTimeKind kind)
    {
        var counter = new RequestCounter(RiskData.Parameters);

        Assert.Throws<ArgumentException>(() => counter.CheckBeforeRequest(DateTime.SpecifyKind(Ten, kind)));
    }
}
