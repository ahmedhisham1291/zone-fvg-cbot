using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Bars and a tracker for the tests of the SessionRange mode (SIG-031 to SIG-033). Bars are built
/// from explicit open times and never from a clock. The range bars of a day have the high
/// <see cref="RangeHigh"/> in their second bar and the low <see cref="RangeLow"/> in their
/// second-to-last bar; every other range bar lies between 2002 and 2008.
/// </summary>
public static class SessionRangeData
{
    public const double RangeHigh = 2010.0;
    public const double RangeLow = 2000.0;

    /// <summary>ATR(D1) of the normal case: the width threshold is 0.5 x 16 = 8, below the width of 10.</summary>
    public const double AtrD1 = 16.0;

    /// <summary>ATR(H4) of the normal case: the stop distance is 5.</summary>
    public const double AtrH4 = 5.0;

    public const double RewardRisk = 2.0;

    /// <summary>One tick of gold.</summary>
    public const double Tick = 0.01;

    /// <summary>A Wednesday in winter time: range 00:00 to 08:00 UTC on 15 January 2025, last entry 17:00 UTC.</summary>
    public static readonly DateTime Winter = new(2025, 1, 15);

    /// <summary>A Wednesday in summer time: range 23:00 UTC on 15 July to 07:00 UTC on 16 July 2025, last entry 16:00 UTC.</summary>
    public static readonly DateTime Summer = new(2025, 7, 16);

    /// <summary>The Friday cap used unless a test sets its own: later than 12:00 New York in both seasons.</summary>
    public static readonly TimeSpan LateFridayNoEntryUtc = TimeSpan.FromHours(20.0);

    public static readonly TimeSpan M15 = TimeSpan.FromMinutes(15.0);

    public static SessionRangeTracker Tracker()
    {
        return new SessionRangeTracker(new SessionRangeParameters(), RewardRisk, LateFridayNoEntryUtc);
    }

    public static SessionRangeTracker Tracker(TimeSpan fridayNoEntryUtc)
    {
        return new SessionRangeTracker(new SessionRangeParameters(), RewardRisk, fridayNoEntryUtc);
    }

    public static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    /// <summary>The UTC instant of a New York time of day in the trading day.</summary>
    public static DateTime At(DateTime tradingDate, int hour, int minute)
    {
        return NewYorkClock.UtcInTradingDay(tradingDate, new TimeSpan(hour, minute, 0));
    }

    /// <summary>A bar between 2002 and 2008, inside the range.</summary>
    public static Bar Inside(DateTime openUtc)
    {
        return new Bar(openUtc, 2005.0, 2008.0, 2002.0, 2005.0);
    }

    /// <summary>A bar with the given high and low that opens at the low and closes at the high.</summary>
    public static Bar Span(DateTime openUtc, double high, double low)
    {
        return new Bar(openUtc, low, high, low, high);
    }

    /// <summary><paramref name="count"/> consecutive range bars from <paramref name="fromUtc"/> (at least 3).</summary>
    public static List<Bar> RangeBars(DateTime fromUtc, int count)
    {
        var bars = new List<Bar>(count);
        for (int k = 0; k < count; k++)
        {
            DateTime openUtc = fromUtc + (k * M15);
            if (k == 1)
            {
                bars.Add(Span(openUtc, RangeHigh, 2004.0));
            }
            else if (k == count - 2)
            {
                bars.Add(Span(openUtc, 2006.0, RangeLow));
            }
            else
            {
                bars.Add(Inside(openUtc));
            }
        }

        return bars;
    }

    /// <summary>The 32 range bars of the trading day, 19:00 to 02:45 New York.</summary>
    public static List<Bar> FullRange(DateTime tradingDate)
    {
        return RangeBars(At(tradingDate, 19, 0), 32);
    }

    /// <summary><paramref name="count"/> consecutive bars inside the range from <paramref name="fromUtc"/>.</summary>
    public static List<Bar> InsideBars(DateTime fromUtc, int count)
    {
        return Enumerable.Range(0, count).Select(k => Inside(fromUtc + (k * M15))).ToList();
    }

    /// <summary>Feeds the bars with one bias and the two ATR values and returns one answer per bar.</summary>
    public static List<SessionRangeEvent?> Feed(SessionRangeTracker tracker, IEnumerable<Bar> bars, Bias bias, double? atrD1 = AtrD1, double? atrH4 = AtrH4)
    {
        return bars.Select(bar => tracker.OnM15Closed(bar, bias, atrD1, atrH4)).ToList();
    }

    /// <summary>The single setup among the answers, or null when there is none; fails when there are several or any void.</summary>
    public static SessionRangeSetup? OnlySetup(IReadOnlyList<SessionRangeEvent?> answers)
    {
        List<SessionRangeEvent> found = answers.Where(answer => answer is not null).Select(answer => answer!).ToList();
        Assert.True(found.Count <= 1, "expected at most one answer but got " + found.Count.ToString(CultureInfo.InvariantCulture));
        if (found.Count == 0)
        {
            return null;
        }

        Assert.Equal(SessionRangeEventKind.Setup, found[0].Kind);
        return found[0].Setup;
    }

    /// <summary>A full trading day up to the evaluation: 17:00 to 18:45 outside the range, then the 32 range bars.</summary>
    public static List<Bar> DayToEvaluation(DateTime tradingDate)
    {
        var bars = new List<Bar>();
        for (int k = 0; k < 8; k++)
        {
            // Before RangeStartNy: far outside the range, so that counting one of them would show.
            bars.Add(Span(At(tradingDate, 17, 0) + (k * M15), 2050.0, 1950.0));
        }

        bars.AddRange(FullRange(tradingDate));
        return bars;
    }
}
