using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The worked example of the breakout tests (SIG-022 to SIG-025): the reference D1 bar opens
/// 2026-03-02 22:00 UTC (a Monday) with open 1.0800, high 1.0850, low 1.0780, close 1.0840;
/// EntryBufferAtr 0.1, StopAtrMult 1.0, window 07:00 to 17:00, RewardRisk 2.0; ATR(M15) 0.0010
/// and ATR(H4) 0.0040. The reference bar closes 2026-03-03 22:00.
/// </summary>
public static class BreakoutData
{
    /// <summary>Reward-to-risk ratio of the worked example.</summary>
    public const double RewardRisk = 2.0;

    /// <summary>ATR(M15) of the worked example.</summary>
    public const double AtrM15 = 0.0010;

    /// <summary>ATR(H4) of the worked example.</summary>
    public const double AtrH4 = 0.0040;

    /// <summary>Reference high of the worked example.</summary>
    public const double High = 1.0850;

    /// <summary>Reference low of the worked example.</summary>
    public const double Low = 1.0780;

    /// <summary>Open time of the reference D1 bar: 2026-03-02 22:00 UTC.</summary>
    public static readonly DateTime DayOpen = Utc(2026, 3, 2, 22, 0);

    /// <summary>The reference D1 bar of the worked example.</summary>
    public static Bar ReferenceBar()
    {
        return BarBuilder.One(TimeFrame.D1, DayOpen, 1.0800, High, Low, 1.0840);
    }

    /// <summary>A tracker with the given parameters (defaults when null) that has been fed the reference bar.</summary>
    public static BreakoutTracker Armed(BreakoutParameters? parameters = null, double rewardRisk = RewardRisk)
    {
        var tracker = new BreakoutTracker(parameters ?? new BreakoutParameters(), rewardRisk);
        tracker.OnD1Closed(ReferenceBar());
        return tracker;
    }

    /// <summary>A UTC time to the minute.</summary>
    public static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    /// <summary>An M15 bar with all four prices equal to the close.</summary>
    public static Bar Flat(DateTime openUtc, double close)
    {
        return BarBuilder.One(TimeFrame.M15, openUtc, close, close, close, close);
    }

    /// <summary>An M15 bar from its open time and four prices.</summary>
    public static Bar M15(DateTime openUtc, double open, double high, double low, double close)
    {
        return BarBuilder.One(TimeFrame.M15, openUtc, open, high, low, close);
    }

    /// <summary>Feeds an M15 bar with the worked-example ATR values.</summary>
    public static BreakoutSetup? Feed(BreakoutTracker tracker, Bar bar, Bias bias)
    {
        return tracker.OnM15Closed(bar, bias, AtrM15, AtrH4);
    }

    /// <summary>Fails the test when no setup was returned; otherwise returns it.</summary>
    public static BreakoutSetup Require(BreakoutSetup? setup)
    {
        Assert.True(setup is not null, "A breakout setup was expected but the tracker returned null.");
        return setup!;
    }

    /// <summary>Asserts that the tracker has a reference and that it is still Armed.</summary>
    public static void AssertArmed(BreakoutTracker tracker)
    {
        Assert.True(tracker.Reference is not null, "The tracker has no reference.");
        Assert.True(tracker.Reference!.Armed, "The reference is Done but should be Armed.");
    }

    /// <summary>Asserts that the tracker has a reference and that it is Done.</summary>
    public static void AssertDone(BreakoutTracker tracker)
    {
        Assert.True(tracker.Reference is not null, "The tracker has no reference.");
        Assert.False(tracker.Reference!.Armed, "The reference is Armed but should be Done.");
    }

    /// <summary>Asserts that two prices are equal within the default tolerance of <see cref="PriceMath"/>.</summary>
    public static void AssertPrice(double expected, double actual, string what)
    {
        Assert.True(
            PriceMath.AlmostEqual(expected, actual, PriceMath.DefaultTolerance),
            what + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture) + " but was " + actual.ToString("R", CultureInfo.InvariantCulture));
    }
}
