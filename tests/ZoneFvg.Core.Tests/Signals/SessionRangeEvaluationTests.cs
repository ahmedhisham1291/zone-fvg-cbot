using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;
using static ZoneFvg.Core.Tests.SessionRangeData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The session range and its evaluation (SIG-031): which bars are range bars, which bar evaluates
/// the range, and every reason for no setup.
/// </summary>
public class SessionRangeEvaluationTests
{
    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_WinterDay_RangeIs0000To0800UtcAndTheBarClosingAt0300NewYorkEvaluates()
    {
        List<Bar> bars = DayToEvaluation(Winter);
        Assert.Equal(Utc(2025, 1, 15, 0, 0), bars[8].OpenTimeUtc);
        Assert.Equal(Utc(2025, 1, 15, 7, 45), bars[^1].OpenTimeUtc);

        List<SessionRangeEvent?> answers = Feed(Tracker(), bars, Bias.Long);

        Assert.All(answers.Take(answers.Count - 1), answer => Assert.Null(answer));
        SessionRangeEvent last = Assert.IsType<SessionRangeEvent>(answers[^1]);
        Assert.Equal(SessionRangeEventKind.Setup, last.Kind);
        Assert.Equal(RangeHigh, last.Setup.RangeHigh);
        Assert.Equal(RangeLow, last.Setup.RangeLow);
        Assert.Equal(Winter, last.Setup.TradingDate);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_SummerDay_RangeIs2300To0700UtcAndTheBarClosingAt0300NewYorkEvaluates()
    {
        List<Bar> bars = DayToEvaluation(Summer);
        Assert.Equal(Utc(2025, 7, 15, 23, 0), bars[8].OpenTimeUtc);
        Assert.Equal(Utc(2025, 7, 16, 6, 45), bars[^1].OpenTimeUtc);

        List<SessionRangeEvent?> answers = Feed(Tracker(), bars, Bias.Long);

        Assert.All(answers.Take(answers.Count - 1), answer => Assert.Null(answer));
        SessionRangeSetup setup = Assert.IsType<SessionRangeEvent>(answers[^1]).Setup;
        Assert.Equal(RangeHigh, setup.RangeHigh);
        Assert.Equal(RangeLow, setup.RangeLow);
        Assert.Equal(Summer, setup.TradingDate);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_BarOpeningAt1900_IsARangeBar()
    {
        List<Bar> bars = FullRange(Winter);
        bars[0] = Span(At(Winter, 19, 0), 2020.0, 1995.0);

        SessionRangeSetup? setup = OnlySetup(Feed(Tracker(), bars, Bias.Long));

        Assert.NotNull(setup);
        Assert.Equal(2020.0, setup!.RangeHigh);
        Assert.Equal(1995.0, setup.RangeLow);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_BarOpeningAt1845_IsNotARangeBar()
    {
        var bars = new List<Bar> { Span(At(Winter, 18, 45), 2020.0, 1995.0) };
        bars.AddRange(FullRange(Winter));

        SessionRangeSetup? setup = OnlySetup(Feed(Tracker(), bars, Bias.Long));

        Assert.NotNull(setup);
        Assert.Equal(RangeHigh, setup!.RangeHigh);
        Assert.Equal(RangeLow, setup.RangeLow);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_BarOpeningAt0245_IsARangeBarAndEvaluates()
    {
        List<Bar> bars = FullRange(Winter);
        Assert.Equal(At(Winter, 2, 45), bars[^1].OpenTimeUtc);
        bars[^1] = Span(At(Winter, 2, 45), 2020.0, 1995.0);

        List<SessionRangeEvent?> answers = Feed(Tracker(), bars, Bias.Long);

        SessionRangeSetup setup = Assert.IsType<SessionRangeEvent>(answers[^1]).Setup;
        Assert.Equal(2020.0, setup.RangeHigh);
        Assert.Equal(1995.0, setup.RangeLow);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_BarClosingAt0300Missing_TheBarOpeningAt0300EvaluatesAndIsNotARangeBar()
    {
        List<Bar> bars = RangeBars(At(Winter, 19, 0), 31);
        bars.Add(Span(At(Winter, 3, 0), 2030.0, 2004.0));

        List<SessionRangeEvent?> answers = Feed(Tracker(), bars, Bias.Long);

        Assert.All(answers.Take(31), answer => Assert.Null(answer));
        SessionRangeSetup setup = Assert.IsType<SessionRangeEvent>(answers[^1]).Setup;
        Assert.Equal(RangeHigh, setup.RangeHigh);
        Assert.Equal(RangeLow, setup.RangeLow);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_FirstBarAfterTheRangeClosesBefore1200_Evaluates()
    {
        List<Bar> bars = RangeBars(At(Winter, 19, 0), 31);
        bars.Add(Inside(At(Winter, 11, 30)));

        List<SessionRangeEvent?> answers = Feed(Tracker(), bars, Bias.Long);

        Assert.Equal(SessionRangeEventKind.Setup, Assert.IsType<SessionRangeEvent>(answers[^1]).Kind);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_FirstBarAfterTheRangeClosesAt1200_GivesNoSetupForTheWholeDay()
    {
        List<Bar> bars = RangeBars(At(Winter, 19, 0), 31);
        bars.AddRange(InsideBars(At(Winter, 11, 45), 20));

        SessionRangeTracker tracker = Tracker();
        List<SessionRangeEvent?> answers = Feed(tracker, bars, Bias.Long);

        Assert.All(answers, answer => Assert.Null(answer));
        Assert.Null(tracker.Setup);
        Assert.Equal(Winter, tracker.TradingDate);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_FirstBarAfterTheRangeClosesAfter1200_GivesNoSetup()
    {
        List<Bar> bars = RangeBars(At(Winter, 19, 0), 31);
        bars.Add(Inside(At(Winter, 14, 0)));

        Assert.All(Feed(Tracker(), bars, Bias.Long), answer => Assert.Null(answer));
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_15RangeBars_GivesNoSetup()
    {
        List<Bar> bars = RangeBars(At(Winter, 3, 0) - (15 * M15), 15);

        Assert.All(Feed(Tracker(), bars, Bias.Long), answer => Assert.Null(answer));
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_16RangeBars_GivesASetup()
    {
        List<Bar> bars = RangeBars(At(Winter, 3, 0) - (16 * M15), 16);

        Assert.NotNull(OnlySetup(Feed(Tracker(), bars, Bias.Long)));
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_16RangeBarsThenAGapUntilTheEvaluatingBar_GivesASetup()
    {
        List<Bar> bars = RangeBars(At(Winter, 19, 0), 16);
        bars.Add(Inside(At(Winter, 3, 0)));

        Assert.NotNull(OnlySetup(Feed(Tracker(), bars, Bias.Long)));
    }

    [Theory]
    [Trait("Req", "SIG-031")]
    [InlineData(2025, 1, 18)]
    [InlineData(2025, 1, 19)]
    [InlineData(2025, 7, 19)]
    [InlineData(2025, 7, 20)]
    public void OnM15Closed_TradingDayEndsOnSaturdayOrSunday_GivesNoSetup(int year, int month, int day)
    {
        var tradingDate = new DateTime(year, month, day);

        Assert.All(Feed(Tracker(), DayToEvaluation(tradingDate), Bias.Long), answer => Assert.Null(answer));
    }

    [Theory]
    [Trait("Req", "SIG-031")]
    [InlineData(2025, 1, 17)]
    [InlineData(2025, 1, 20)]
    public void OnM15Closed_TradingDayEndsOnFridayOrMonday_GivesASetup(int year, int month, int day)
    {
        var tradingDate = new DateTime(year, month, day);

        Assert.NotNull(OnlySetup(Feed(Tracker(), DayToEvaluation(tradingDate), Bias.Long)));
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_NeutralBias_GivesNoSetup()
    {
        Assert.All(Feed(Tracker(), DayToEvaluation(Winter), Bias.Neutral), answer => Assert.Null(answer));
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_BiasOfTheEvaluatingBar_Decides()
    {
        List<Bar> bars = DayToEvaluation(Winter);
        SessionRangeTracker tracker = Tracker();
        Feed(tracker, bars.Take(bars.Count - 1), Bias.Neutral);

        SessionRangeEvent? answer = tracker.OnM15Closed(bars[^1], Bias.Short, AtrD1, AtrH4);

        Assert.Equal(Side.Sell, Assert.IsType<SessionRangeEvent>(answer).Setup.Side);
    }

    [Theory]
    [Trait("Req", "SIG-031")]
    [InlineData(2009.99, false)]
    [InlineData(2010.0, true)]
    [InlineData(2010.01, true)]
    public void OnM15Closed_WidthAroundTheThreshold_EqualPasses(double high, bool expectSetup)
    {
        // ATR(D1) 20 and RangeMinWidthAtr 0.5: the threshold is 10, the range low 2000.
        List<Bar> bars = FullRange(Winter);
        bars[1] = Span(bars[1].OpenTimeUtc, high, 2004.0);

        SessionRangeSetup? setup = OnlySetup(Feed(Tracker(), bars, Bias.Long, 20.0, AtrH4));

        Assert.Equal(expectSetup, setup is not null);
    }

    [Theory]
    [Trait("Req", "SIG-031")]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-16.0)]
    public void OnM15Closed_AtrD1MissingOrNotUsable_GivesNoSetup(double? atrD1)
    {
        Assert.All(Feed(Tracker(), DayToEvaluation(Winter), Bias.Long, atrD1, AtrH4), answer => Assert.Null(answer));
    }

    [Theory]
    [Trait("Req", "SIG-031")]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void OnM15Closed_AtrH4MissingOrNotUsable_GivesNoSetup(double? atrH4)
    {
        Assert.All(Feed(Tracker(), DayToEvaluation(Winter), Bias.Long, AtrD1, atrH4), answer => Assert.Null(answer));
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_NoSetupAtTheEvaluation_NoLaterBarOfTheDayEvaluatesAgain()
    {
        SessionRangeTracker tracker = Tracker();
        Feed(tracker, DayToEvaluation(Winter), Bias.Neutral);

        List<SessionRangeEvent?> later = Feed(tracker, InsideBars(At(Winter, 3, 0), 30), Bias.Long);

        Assert.All(later, answer => Assert.Null(answer));
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_BarsBeforeTheRangeOnly_GiveNothing()
    {
        SessionRangeTracker tracker = Tracker();

        List<SessionRangeEvent?> answers = Feed(tracker, InsideBars(At(Winter, 17, 0), 8), Bias.Long);

        Assert.All(answers, answer => Assert.Null(answer));
        Assert.Equal(Winter, tracker.TradingDate);
        Assert.Null(tracker.Setup);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_NewTradingDay_StartsFromNothing()
    {
        // Day 1 ends without a setup; day 2 is evaluated on its own bars only.
        var nextDay = new DateTime(2025, 1, 16);
        SessionRangeTracker tracker = Tracker();
        Feed(tracker, DayToEvaluation(Winter), Bias.Neutral);
        Feed(tracker, InsideBars(At(Winter, 3, 0), 56), Bias.Neutral);
        Assert.Equal(Winter, tracker.TradingDate);

        List<SessionRangeEvent?> answers = Feed(tracker, DayToEvaluation(nextDay), Bias.Long);

        Assert.Equal(nextDay, tracker.TradingDate);
        SessionRangeSetup setup = Assert.IsType<SessionRangeEvent>(answers[^1]).Setup;
        Assert.Equal("RH-20250116", setup.Id);
        Assert.Same(setup, tracker.Setup);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void TradingDate_BeforeTheFirstBar_IsNull()
    {
        SessionRangeTracker tracker = Tracker();

        Assert.Null(tracker.TradingDate);
        Assert.Null(tracker.Setup);
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_NullBar_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Tracker().OnM15Closed(null!, Bias.Long, AtrD1, AtrH4));
    }

    [Fact]
    [Trait("Req", "SIG-031")]
    public void OnM15Closed_BarNotAfterTheLastBar_ThrowsAndChangesNothing()
    {
        List<Bar> bars = DayToEvaluation(Winter);
        SessionRangeTracker tracker = Tracker();
        Feed(tracker, bars.Take(bars.Count - 1), Bias.Long);

        var repeated = Assert.Throws<InvalidOperationException>(() => tracker.OnM15Closed(bars[^2], Bias.Long, AtrD1, AtrH4));
        Assert.Throws<InvalidOperationException>(() => tracker.OnM15Closed(Inside(At(Winter, 17, 0)), Bias.Long, AtrD1, AtrH4));
        SessionRangeEvent? answer = tracker.OnM15Closed(bars[^1], Bias.Long, AtrD1, AtrH4);

        Assert.Equal("An M15 bar opening at 2025-01-15 07:30 does not open after the last bar of 2025-01-15 07:30.", repeated.Message);
        SessionRangeSetup setup = Assert.IsType<SessionRangeEvent>(answer).Setup;
        Assert.Equal(RangeHigh, setup.RangeHigh);
        Assert.Equal(RangeLow, setup.RangeLow);
    }
}
