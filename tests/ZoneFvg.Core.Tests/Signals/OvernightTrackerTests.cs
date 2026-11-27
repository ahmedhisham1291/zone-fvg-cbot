using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-043: the Overnight tracker. Bars are M15 bars built with <see cref="BarBuilder"/>; the ATRs
/// are passed in. New York times were converted by hand: on Wednesday 2026-03-04 (winter time,
/// UTC-5) the trading day starts at 22:00 UTC on 2026-03-03, 18:15 New York is 23:15 UTC and
/// 19:00 New York is 00:00 UTC; in summer time (UTC-4) the same times are one hour earlier.
/// </summary>
public class OvernightTrackerTests
{
    private static readonly DateTime Wednesday = new(2026, 3, 4);

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    private static Bar M15(DateTime openUtc, double close = 2650.0)
    {
        return BarBuilder.One(TimeFrame.M15, openUtc, close, close + 1.5, close - 1.5, close);
    }

    // Every M15 bar from the first open to the last open, both included, except the skipped opens.
    private static List<OvernightSetup?> Feed(OvernightTracker tracker, DateTime firstOpen, DateTime lastOpen, DateTime[]? skipped = null, double? atrD1 = 40.0, double? atrM15 = 3.0)
    {
        var answers = new List<OvernightSetup?>();
        for (DateTime open = firstOpen; open <= lastOpen; open = open.AddMinutes(15.0))
        {
            if (skipped is null || !skipped.Contains(open))
            {
                answers.Add(tracker.OnM15Closed(M15(open), atrD1, atrM15));
            }
        }

        return answers;
    }

    private static OvernightSetup Only(IEnumerable<OvernightSetup?> answers)
    {
        return Assert.Single(answers, answer => answer is not null)!;
    }

    // ------------------------------------------------------------------ the evaluation bar and the levels

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Levels_Close2650AtrM15ThreeAtrD1Forty_ExactNumbersAndExpiry60MinutesAfter1815()
    {
        var tracker = new OvernightTracker(new OvernightParameters());

        OvernightSetup setup = Only(Feed(tracker, Utc(2026, 3, 3, 22, 0), Utc(2026, 3, 4, 2, 0)));

        // Entry 2650 + 0.03 x 3; stop entry - 1 x 40; target entry + 5 x 40. The spread is added at placement (EXE-033).
        Assert.Equal(2650.09, setup.Entry, 9);
        Assert.Equal(2610.09, setup.Stop, 9);
        Assert.Equal(2850.09, setup.Target, 9);
        Assert.Equal(Utc(2026, 3, 3, 23, 15), setup.EvaluationCloseUtc);
        Assert.Equal(Utc(2026, 3, 4, 0, 15), setup.ExpiryUtc);
        Assert.Equal(DateTimeKind.Utc, setup.ExpiryUtc.Kind);
        Assert.Equal(2650.0, setup.Close);
        Assert.Equal(Wednesday, setup.TradingDate);
        Assert.Equal("ON-20260304", setup.Id);
        Assert.Same(setup, tracker.Setup);
        Assert.Equal(Wednesday, tracker.TradingDate);
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Evaluation_BarClosingExactlyAt1815_IsUsed()
    {
        var tracker = new OvernightTracker(new OvernightParameters());

        Assert.Null(tracker.OnM15Closed(M15(Utc(2026, 3, 3, 22, 45)), 40.0, 3.0));
        OvernightSetup? setup = tracker.OnM15Closed(M15(Utc(2026, 3, 3, 23, 0), 2700.0), 40.0, 3.0);

        Assert.NotNull(setup);
        Assert.Equal(Utc(2026, 3, 3, 23, 15), setup!.EvaluationCloseUtc);
        Assert.Equal(2700.09, setup.Entry, 9);
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Evaluation_The1815BarMissing_TheBarClosingAt1830IsUsed()
    {
        var tracker = new OvernightTracker(new OvernightParameters());

        OvernightSetup setup = Only(Feed(tracker, Utc(2026, 3, 3, 22, 0), Utc(2026, 3, 4, 2, 0), skipped: new[] { Utc(2026, 3, 3, 23, 0) }));

        Assert.Equal(Utc(2026, 3, 3, 23, 30), setup.EvaluationCloseUtc);
        Assert.Equal(Utc(2026, 3, 4, 0, 30), setup.ExpiryUtc);
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Evaluation_NoBarClosesBefore1900_NoSetupForTheDay()
    {
        var tracker = new OvernightTracker(new OvernightParameters());
        DateTime[] missing = { Utc(2026, 3, 3, 23, 0), Utc(2026, 3, 3, 23, 15), Utc(2026, 3, 3, 23, 30), Utc(2026, 3, 3, 23, 45) };

        List<OvernightSetup?> answers = Feed(tracker, Utc(2026, 3, 3, 22, 0), Utc(2026, 3, 4, 20, 0), skipped: missing);

        Assert.All(answers, answer => Assert.Null(answer));
        Assert.Null(tracker.Setup);
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Evaluation_TheFirstBarClosesExactlyAt1900_NotUsedAndNoLaterBarIs()
    {
        var tracker = new OvernightTracker(new OvernightParameters());
        DateTime[] missing = { Utc(2026, 3, 3, 23, 0), Utc(2026, 3, 3, 23, 15), Utc(2026, 3, 3, 23, 30) };

        // The bar opening 23:45 UTC closes at 00:00 UTC, 19:00 New York.
        List<OvernightSetup?> answers = Feed(tracker, Utc(2026, 3, 3, 22, 0), Utc(2026, 3, 4, 3, 0), skipped: missing);

        Assert.All(answers, answer => Assert.Null(answer));
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Evaluation_TheLastBarBefore1900_IsUsedWhenTheEarlierOnesAreMissing()
    {
        var tracker = new OvernightTracker(new OvernightParameters());
        DateTime[] missing = { Utc(2026, 3, 3, 23, 0), Utc(2026, 3, 3, 23, 15) };

        OvernightSetup setup = Only(Feed(tracker, Utc(2026, 3, 3, 22, 0), Utc(2026, 3, 4, 3, 0), skipped: missing));

        Assert.Equal(Utc(2026, 3, 3, 23, 45), setup.EvaluationCloseUtc);
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void OneSetupPerTradingDay_LaterBarsOfTheDayGiveNone_TheNextDayGivesItsOwn()
    {
        var tracker = new OvernightTracker(new OvernightParameters());

        List<OvernightSetup?> answers = Feed(tracker, Utc(2026, 3, 3, 22, 0), Utc(2026, 3, 5, 0, 0));
        List<OvernightSetup> setups = answers.Where(answer => answer is not null).Select(answer => answer!).ToList();

        Assert.Equal(new[] { "ON-20260304", "ON-20260305" }, setups.Select(setup => setup.Id).ToArray());
        Assert.Equal(Utc(2026, 3, 4, 23, 15), setups[1].EvaluationCloseUtc);
    }

    // ------------------------------------------------------------------ days

    public static IEnumerable<object[]> TradingDays()
    {
        // Trading date, opening of its evaluation bar (UTC), whether it trades. The date names the day on which the trading day ends.
        yield return new object[] { new DateTime(2026, 3, 2), Utc(2026, 3, 1, 23, 0), false };  // starts Sunday evening
        yield return new object[] { new DateTime(2026, 3, 3), Utc(2026, 3, 2, 23, 0), true };   // Monday evening
        yield return new object[] { new DateTime(2026, 3, 4), Utc(2026, 3, 3, 23, 0), true };   // Tuesday evening
        yield return new object[] { new DateTime(2026, 3, 5), Utc(2026, 3, 4, 23, 0), true };   // Wednesday evening
        yield return new object[] { new DateTime(2026, 3, 6), Utc(2026, 3, 5, 23, 0), true };   // Thursday evening
        yield return new object[] { new DateTime(2026, 3, 7), Utc(2026, 3, 6, 23, 0), false };  // starts Friday evening
        yield return new object[] { new DateTime(2026, 3, 8), Utc(2026, 3, 7, 23, 0), false };  // starts Saturday evening
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [MemberData(nameof(TradingDays))]
    public void Days_MondayToThursdayEveningsOnly_OneSetupEach(DateTime tradingDate, DateTime evaluationOpen, bool trades)
    {
        var tracker = new OvernightTracker(new OvernightParameters());

        OvernightSetup? setup = tracker.OnM15Closed(M15(evaluationOpen), 40.0, 3.0);

        Assert.Equal(trades, setup is not null);
        Assert.Equal(trades, OvernightTracker.TradesOn(tradingDate));
        Assert.Equal(tradingDate, tracker.TradingDate);
        if (trades)
        {
            Assert.Equal(OvernightId.For(tradingDate), setup!.Id);
        }
    }

    public static IEnumerable<object[]> DaylightSavingDays()
    {
        // March 2026: summer time from Sunday 2026-03-08 07:00 UTC. November 2026: winter time from Sunday 2026-11-01 06:00 UTC.
        yield return new object[] { new DateTime(2026, 3, 6), Utc(2026, 3, 5, 22, 0), Utc(2026, 3, 5, 23, 15) };   // Thursday evening, winter
        yield return new object[] { new DateTime(2026, 3, 10), Utc(2026, 3, 9, 21, 0), Utc(2026, 3, 9, 22, 15) };  // Monday evening, summer
        yield return new object[] { new DateTime(2026, 10, 30), Utc(2026, 10, 29, 21, 0), Utc(2026, 10, 29, 22, 15) }; // Thursday evening, summer
        yield return new object[] { new DateTime(2026, 11, 3), Utc(2026, 11, 2, 22, 0), Utc(2026, 11, 2, 23, 15) };  // Monday evening, winter
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [MemberData(nameof(DaylightSavingDays))]
    public void Days_AcrossTheDaylightSavingChanges_TheEvaluationBarClosesAt1815NewYork(DateTime tradingDate, DateTime dayStartUtc, DateTime expectedCloseUtc)
    {
        var tracker = new OvernightTracker(new OvernightParameters());

        OvernightSetup setup = Only(Feed(tracker, dayStartUtc, dayStartUtc.AddHours(6.0)));

        Assert.Equal(expectedCloseUtc, setup.EvaluationCloseUtc);
        Assert.Equal(tradingDate, setup.TradingDate);
        Assert.Equal(new TimeSpan(18, 15, 0), NewYorkClock.ToNewYork(setup.EvaluationCloseUtc).TimeOfDay);
    }

    // ------------------------------------------------------------------ fail closed

    public static IEnumerable<object?[]> MissingAtr()
    {
        foreach (double? bad in new double?[] { null, 0.0, -1.0, double.NaN, double.PositiveInfinity })
        {
            yield return new object?[] { bad, 3.0 };
            yield return new object?[] { 40.0, bad };
        }
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [MemberData(nameof(MissingAtr))]
    public void Atr_MissingZeroOrNotFinite_NoSetupAndTheDayStaysWithout(double? atrD1, double? atrM15)
    {
        var tracker = new OvernightTracker(new OvernightParameters());

        Assert.Null(tracker.OnM15Closed(M15(Utc(2026, 3, 3, 23, 0)), atrD1, atrM15));
        Assert.Null(tracker.OnM15Closed(M15(Utc(2026, 3, 3, 23, 15)), 40.0, 3.0));
        Assert.Null(tracker.Setup);
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Levels_StopNotPositive_NoSetup()
    {
        var tracker = new OvernightTracker(new OvernightParameters());

        Assert.Null(tracker.OnM15Closed(M15(Utc(2026, 3, 3, 23, 0), 30.0), 40.0, 3.0));
        Assert.Null(tracker.Setup);
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void OnM15Closed_BarNotAfterTheLast_ThrowsAndChangesNothing()
    {
        var tracker = new OvernightTracker(new OvernightParameters());
        tracker.OnM15Closed(M15(Utc(2026, 3, 3, 22, 45)), 40.0, 3.0);

        Assert.Throws<InvalidOperationException>(() => tracker.OnM15Closed(M15(Utc(2026, 3, 3, 22, 45)), 40.0, 3.0));
        Assert.Throws<InvalidOperationException>(() => tracker.OnM15Closed(M15(Utc(2026, 3, 3, 22, 30)), 40.0, 3.0));
        Assert.Throws<ArgumentNullException>(() => tracker.OnM15Closed(null!, 40.0, 3.0));
        Assert.NotNull(tracker.OnM15Closed(M15(Utc(2026, 3, 3, 23, 0)), 40.0, 3.0));
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Parameters_OtherTimesAndValues_AreUsed()
    {
        var parameters = new OvernightParameters { NightEntryNy = new TimeSpan(18, 30, 0), NightLatestNy = new TimeSpan(18, 45, 0), NightEntryBufferAtr = 0.5, NightStopAtr = 2.0, NightTargetR = 1.5, NightOrderMinutes = 15 };
        var tracker = new OvernightTracker(parameters);

        OvernightSetup setup = Only(Feed(tracker, Utc(2026, 3, 3, 22, 0), Utc(2026, 3, 4, 2, 0)));

        Assert.Equal(Utc(2026, 3, 3, 23, 30), setup.EvaluationCloseUtc);
        Assert.Equal(2651.5, setup.Entry, 9);
        Assert.Equal(2571.5, setup.Stop, 9);
        Assert.Equal(2771.5, setup.Target, 9);
        Assert.Equal(Utc(2026, 3, 3, 23, 45), setup.ExpiryUtc);
    }

    // ------------------------------------------------------------------ the id

    [Theory]
    [Trait("Req", "SIG-043")]
    [InlineData("ON-20260304", 2026, 3, 4)]
    [InlineData("ON-20261103", 2026, 11, 3)]
    public void Id_FromTheDateThatNamesTheTradingDay_ReadBack(string id, int year, int month, int day)
    {
        var date = new DateTime(year, month, day);

        Assert.Equal(id, OvernightId.For(date));
        Assert.True(OvernightId.TryTradingDate(id, out DateTime read));
        Assert.Equal(date, read);
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("RH-20260304")]
    [InlineData("RL-20260304")]
    [InlineData("BH-20260304-0000")]
    [InlineData("D-20260303-1600")]
    [InlineData("ON-2026030")]
    [InlineData("ON-202603041")]
    [InlineData("ON-2026O304")]
    [InlineData("ON-20260230")]
    [InlineData("ON-00010101")]
    [InlineData("ON-99991231")]
    [InlineData("on-20260304")]
    public void Id_AnyOtherText_IsNotAnOvernightId(string? id)
    {
        Assert.False(OvernightId.TryTradingDate(id, out DateTime read));
        Assert.Equal(DateTime.MinValue, read);
    }
}
