using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;
using Data = ZoneFvg.Core.Tests.SessionRangeBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-042 through the signal engine: with RangeTrendFastEma above 0 the side of the SessionRange
/// setup comes from D1 EMA(RangeTrendFastEma) against D1 EMA(BiasEmaPeriod) of the last D1 bar
/// closed at the evaluation; with 0 it is the bias of spec 3.1 as before. H4 and M15 bars are those
/// of <see cref="SessionRangeBotData"/> (range 100.0 to 100.4 on the planned days, ATR(H4) 0.4);
/// the D1 bars are built here, open = close, high = close + 0.3, low = close - 0.3, so ATR(D1) stays
/// about 0.6. The EMAs the expectations rest on are computed in this class with the formula of
/// spec 3.1 / SIG-003 (window of 4 x period closes, seeded with the simple average of the first
/// period closes), independently of the code under test.
/// </summary>
/// <remarks>
/// <para>Rising D1 series: close 100.0 - 0.001 x (bars after this one), from 2025-06-01. On the
/// bar of 2026-03-03, the last one closed at the 03:00 New York evaluation of 2026-03-04
/// (08:00 UTC), the close is lowered by 0.05 to 99.94: then EMA(20) is about 99.9757 and EMA(50)
/// about 99.9635, so EMA(20) is above EMA(50) while the close is below EMA(50). The rule of 3.1
/// gives Short (a sell stop at the range low 100.0), the rule of SIG-042 Long (a buy stop at the
/// range high 100.4).</para>
/// <para>Mirror: every price p becomes 200 - p, with the mirrored M15 and H4 bars of
/// <see cref="SessionRangeBotData"/> (range 99.6 to 100.0).</para>
/// </remarks>
public class SignalEngineRangeTrendTests
{
    private static readonly DateTime D1Start = Data.Utc(2025, 6, 1, 0, 0);

    /// <summary>The D1 bar that opens on 2026-03-03, the last one closed at the evaluation of 2026-03-04.</summary>
    private static readonly DateTime LastBeforeWinter = Data.Utc(2026, 3, 3, 0, 0);

    private static SignalEngine Engine(int fast, SignalParameters? signal = null)
    {
        return new SignalEngine(signal ?? new SignalParameters(), new SessionRangeParameters { RangeTrendFastEma = fast }, new ExecutionParameters().FridayNoEntryUtc);
    }

    /// <summary>EMA of the last 4 x period closes ending at <paramref name="end"/>; null with too few closes.</summary>
    internal static double? ReferenceEma(IReadOnlyList<double> closes, int end, int period)
    {
        int window = 4 * period;
        if (end + 1 < window)
        {
            return null;
        }

        int start = end - window + 1;
        double ema = closes.Skip(start).Take(period).Sum() / period;
        double alpha = 2.0 / (period + 1.0);
        for (int i = start + period; i <= end; i++)
        {
            ema = (alpha * closes[i]) + ((1.0 - alpha) * ema);
        }

        return ema;
    }

    /// <summary>D1 bars from <paramref name="firstOpenUtc"/>, one a day, up to 2026-03-14, with the closes of <paramref name="close"/> (by bar open time).</summary>
    internal static List<Bar> D1(DateTime firstOpenUtc, Func<DateTime, double> close, bool sell)
    {
        var bars = new List<Bar>();
        for (DateTime open = firstOpenUtc; open < Data.End; open = open.AddDays(1.0))
        {
            double c = close(open);
            bars.Add(sell
                ? BarBuilder.One(TimeFrame.D1, open, 200.0 - c, 200.0 - c + 0.3, 200.0 - c - 0.3, 200.0 - c)
                : BarBuilder.One(TimeFrame.D1, open, c, c + 0.3, c - 0.3, c));
        }

        return bars;
    }

    /// <summary>The rising closes of the remarks, with <paramref name="change"/> added to the close of the bar opening at <paramref name="at"/>.</summary>
    internal static Func<DateTime, double> Rising(DateTime at, double change)
    {
        int count = (int)(Data.End - D1Start).TotalDays;
        return open =>
        {
            int i = (int)(open.Date - D1Start.Date).TotalDays;
            double c = 100.0 - (0.001 * (count - 1 - i));
            return open == at ? c + change : c;
        };
    }

    /// <summary>The bars of <see cref="SessionRangeBotData"/> for the days, with the D1 bars replaced.</summary>
    internal static BarSet WithD1(IReadOnlyList<Bar> d1, bool sell, params DateTime[] days)
    {
        BarSet other = Data.Bars(sell, null, days);
        return new BarSet(d1, other.H4, other.M15);
    }

    /// <summary>The D1 closes (of the unmirrored series) of the bars closed by <paramref name="atUtc"/>.</summary>
    private static List<double> ClosedCloses(IReadOnlyList<Bar> d1, DateTime atUtc, bool sell)
    {
        return d1.Where(bar => bar.OpenTimeUtc.AddDays(1.0) <= atUtc).Select(bar => sell ? 200.0 - bar.Close : bar.Close).ToList();
    }

    private static List<(FeedBar Input, IReadOnlyList<Signal> Signals)> Run(SignalEngine engine, IEnumerable<FeedBar> feed)
    {
        var steps = new List<(FeedBar, IReadOnlyList<Signal>)>();
        foreach (FeedBar item in feed)
        {
            steps.Add((item, engine.OnBarClosed(item.TimeFrame, item.Bar, false).ToArray()));
        }

        return steps;
    }

    private static Signal OnlySignal(IEnumerable<(FeedBar Input, IReadOnlyList<Signal> Signals)> steps)
    {
        var with = steps.Where(step => step.Signals.Count > 0).ToList();
        var step = Assert.Single(with);
        Assert.Equal(Data.Evaluation(Data.Winter), step.Input.CloseUtc);
        return Assert.Single(step.Signals);
    }

    // ------------------------------------------------------------------ the two rules disagree

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Data_EmaFastAboveSlowWhileTheCloseIsBelowTheSlow_AtTheWinterEvaluation()
    {
        List<Bar> d1 = D1(D1Start, Rising(LastBeforeWinter, -0.05), false);
        List<double> closes = ClosedCloses(d1, Data.Evaluation(Data.Winter), false);
        int last = closes.Count - 1;
        double fast = ReferenceEma(closes, last, 20)!.Value;
        double slow = ReferenceEma(closes, last, 50)!.Value;

        Assert.Equal(LastBeforeWinter, d1[last].OpenTimeUtc);
        Assert.Equal(99.94, closes[last], 9);
        Assert.True(fast > slow + 0.01, "EMA(20) " + Invariant.Text(fast) + " not above EMA(50) " + Invariant.Text(slow));
        Assert.True(closes[last] < slow - 0.01, "close not below EMA(50) " + Invariant.Text(slow));
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void OnBarClosed_Fast20AboveSlow50_CloseBelowSlow_BuyStopAtTheRangeHigh()
    {
        SignalEngine engine = Engine(20);

        Signal signal = OnlySignal(Run(engine, WithD1(D1(D1Start, Rising(LastBeforeWinter, -0.05), false), false, Data.Winter).Feed.Where(item => item.CloseUtc <= Data.Utc(2026, 3, 4, 16, 0))));

        Assert.Equal(Data.BuyId(Data.Winter), signal.ZoneId);
        Assert.Equal(Side.Buy, signal.Side);
        Assert.Equal(100.4, signal.Entry, 9);
        Assert.Equal(100.0, signal.Stop, 9);
        Assert.Equal(101.2, signal.Target, 9);
        Assert.Equal(SignalKind.SessionRange, signal.Kind);

        // The bias of 3.1 (the close against EMA(50)) is Short on the same bars, and stays the engine's Bias.
        Assert.Equal(Bias.Short, engine.Bias);
        Assert.Equal(Bias.Long, engine.RangeDirection);
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void OnBarClosed_SameBarsWithZero_TheOldRuleSellsAtTheRangeLow()
    {
        SignalEngine engine = Engine(0);

        Signal signal = OnlySignal(Run(engine, WithD1(D1(D1Start, Rising(LastBeforeWinter, -0.05), false), false, Data.Winter).Feed.Where(item => item.CloseUtc <= Data.Utc(2026, 3, 4, 16, 0))));

        Assert.Equal(Data.SellId(Data.Winter), signal.ZoneId);
        Assert.Equal(Side.Sell, signal.Side);
        Assert.Equal(100.0, signal.Entry, 9);
        Assert.Equal(100.4, signal.Stop, 9);
        Assert.Equal(99.2, signal.Target, 9);
        Assert.Equal(Bias.Short, engine.Bias);

        // With 0 no direction is computed.
        Assert.Equal(Bias.Neutral, engine.RangeDirection);
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void OnBarClosed_Mirror_Fast20BelowSlow50_CloseAboveSlow_SellStopAtTheRangeLow()
    {
        SignalEngine engine = Engine(20);

        Signal signal = OnlySignal(Run(engine, WithD1(D1(D1Start, Rising(LastBeforeWinter, -0.05), true), true, Data.Winter).Feed.Where(item => item.CloseUtc <= Data.Utc(2026, 3, 4, 16, 0))));

        Assert.Equal(Data.SellId(Data.Winter), signal.ZoneId);
        Assert.Equal(Side.Sell, signal.Side);
        Assert.Equal(Data.SellEntry, signal.Entry, 9);
        Assert.Equal(Data.SellStop, signal.Stop, 9);
        Assert.Equal(Data.SellTarget, signal.Target, 9);
        Assert.Equal(Bias.Long, engine.Bias);
        Assert.Equal(Bias.Short, engine.RangeDirection);
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void OnBarClosed_MirrorWithZero_TheOldRuleBuysAtTheRangeHigh()
    {
        SignalEngine engine = Engine(0);

        Signal signal = OnlySignal(Run(engine, WithD1(D1(D1Start, Rising(LastBeforeWinter, -0.05), true), true, Data.Winter).Feed.Where(item => item.CloseUtc <= Data.Utc(2026, 3, 4, 16, 0))));

        Assert.Equal(Data.BuyId(Data.Winter), signal.ZoneId);
        Assert.Equal(Side.Buy, signal.Side);
        Assert.Equal(100.0, signal.Entry, 9);
        Assert.Equal(99.6, signal.Stop, 9);
        Assert.Equal(100.8, signal.Target, 9);
    }

    // ------------------------------------------------------------------ equal, and one tolerance either side

    [Fact]
    [Trait("Req", "SIG-042")]
    public void OnBarClosed_FlatD1_TheTwoEmasEqual_NoSetupThatTradingDay()
    {
        List<Bar> d1 = D1(D1Start, _ => 100.0, false);
        List<double> closes = ClosedCloses(d1, Data.Evaluation(Data.Winter), false);
        double fast = ReferenceEma(closes, closes.Count - 1, 20)!.Value;
        double slow = ReferenceEma(closes, closes.Count - 1, 50)!.Value;
        Assert.True(Math.Abs(fast - slow) <= PriceMath.DefaultTolerance);
        SignalEngine engine = Engine(20);

        var steps = Run(engine, WithD1(d1, false, Data.Winter).Feed.Where(item => item.CloseUtc <= Data.Utc(2026, 3, 4, 16, 0)));

        Assert.All(steps, step => Assert.Empty(step.Signals));
        Assert.True(engine.IsReady);
        Assert.Null(engine.RangeSetup);
        Assert.Equal(Bias.Neutral, engine.RangeDirection);
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(4e-8, 1.1e-9, 1e-8, Side.Buy)] // EMA(20) above EMA(50) by about 2.2e-9: more than the tolerance 1e-9
    [InlineData(-4e-8, -1e-8, -1.1e-9, Side.Sell)] // below by about 2.2e-9
    public void OnBarClosed_OneToleranceEitherSideOfEqual_LongOrShort(double change, double minDifference, double maxDifference, Side expected)
    {
        List<Bar> d1 = D1(D1Start, open => open == LastBeforeWinter ? 100.0 + change : 100.0, false);
        List<double> closes = ClosedCloses(d1, Data.Evaluation(Data.Winter), false);
        double difference = ReferenceEma(closes, closes.Count - 1, 20)!.Value - ReferenceEma(closes, closes.Count - 1, 50)!.Value;
        Assert.InRange(difference, minDifference, maxDifference);
        Assert.True(Math.Abs(difference) > PriceMath.DefaultTolerance);

        Signal signal = OnlySignal(Run(Engine(20), WithD1(d1, false, Data.Winter).Feed.Where(item => item.CloseUtc <= Data.Utc(2026, 3, 4, 16, 0))));

        Assert.Equal(expected, signal.Side);
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(1e-8)] // EMA(20) above EMA(50) by about 5.6e-10: within the tolerance
    [InlineData(-1e-8)]
    public void OnBarClosed_WithinOneToleranceOfEqual_NoSetup(double change)
    {
        List<Bar> d1 = D1(D1Start, open => open == LastBeforeWinter ? 100.0 + change : 100.0, false);
        List<double> closes = ClosedCloses(d1, Data.Evaluation(Data.Winter), false);
        double difference = ReferenceEma(closes, closes.Count - 1, 20)!.Value - ReferenceEma(closes, closes.Count - 1, 50)!.Value;
        Assert.InRange(Math.Abs(difference), 1e-10, 0.9e-9);
        SignalEngine engine = Engine(20);

        var steps = Run(engine, WithD1(d1, false, Data.Winter).Feed.Where(item => item.CloseUtc <= Data.Utc(2026, 3, 4, 16, 0)));

        Assert.All(steps, step => Assert.Empty(step.Signals));
        Assert.Null(engine.RangeSetup);
    }

    // ------------------------------------------------------------------ the last D1 bar closed at the evaluation decides

    [Fact]
    [Trait("Req", "SIG-042")]
    public void OnBarClosed_D1BarThatFlipsTheEmasClosesAfterTheEvaluation_TheDaysSetupStays_TheNextDayFollowsTheFlip()
    {
        // D1 bars open at 12:00 UTC here, so one closes at 12:00 UTC on 2026-03-04: after the 08:00 UTC
        // evaluation and inside the trading day, which ends at 17:00 New York (22:00 UTC). That bar's close
        // 99.0 puts EMA(20) below EMA(50); the bar before it (closed 2026-03-03 12:00) has EMA(20) above.
        DateTime first = Data.Utc(2025, 6, 1, 12, 0);
        DateTime flipOpen = Data.Utc(2026, 3, 3, 12, 0);
        DateTime flipClose = Data.Utc(2026, 3, 4, 12, 0);
        DateTime dayEnd = Data.Utc(2026, 3, 4, 22, 0);
        var nextDay = new DateTime(2026, 3, 5);
        List<Bar> flipped = D1(first, open => Rising(LastBeforeWinter, 0.0)(open) - (open == flipOpen ? 1.0 : 0.0), false);
        List<Bar> control = D1(first, Rising(LastBeforeWinter, 0.0), false);
        List<double> before = ClosedCloses(flipped, Data.Evaluation(Data.Winter), false);
        List<double> after = ClosedCloses(flipped, flipClose, false);
        Assert.True(ReferenceEma(before, before.Count - 1, 20) > ReferenceEma(before, before.Count - 1, 50));
        Assert.True(ReferenceEma(after, after.Count - 1, 20) < ReferenceEma(after, after.Count - 1, 50));

        SignalEngine engine = Engine(20);
        SignalEngine controlEngine = Engine(20);
        var steps = new List<(FeedBar Input, IReadOnlyList<Signal> Signals, SessionRangeSetup? Setup, SessionRangeSetup? Void)>();
        foreach (FeedBar item in WithD1(flipped, false, Data.Winter, nextDay).Feed)
        {
            IReadOnlyList<Signal> signals = engine.OnBarClosed(item.TimeFrame, item.Bar, false).ToArray();
            steps.Add((item, signals, engine.RangeSetup, engine.LastRangeVoid));
        }

        var controlSteps = Run(controlEngine, WithD1(control, false, Data.Winter, nextDay).Feed);

        // The trading day of 2026-03-04: one buy at the evaluation, and the flip at 12:00 UTC changes nothing that day.
        var day = steps.Where(step => step.Input.CloseUtc <= dayEnd).ToList();
        Signal buy = OnlySignal(day.Select(step => (step.Input, step.Signals)));
        Assert.Equal(Side.Buy, buy.Side);
        Assert.Equal(Data.BuyId(Data.Winter), buy.ZoneId);
        Assert.Contains(day, step => step.Input.TimeFrame == TimeFrame.D1 && step.Input.CloseUtc == flipClose);
        Assert.All(day.Where(step => step.Input.CloseUtc > Data.Evaluation(Data.Winter) && step.Input.CloseUtc < dayEnd), step => Assert.Equal(Data.BuyId(Data.Winter), step.Setup?.Id));
        Assert.All(day, step => Assert.Null(step.Void));
        Assert.Equal(
            controlSteps.Where(step => step.Input.CloseUtc <= dayEnd).SelectMany(step => step.Signals).Select(ExactRecords.Key).ToArray(),
            day.SelectMany(step => step.Signals).Select(ExactRecords.Key).ToArray());

        // The next trading day is decided by the flipped bar: a sell, where the control buys.
        Signal next = Assert.Single(steps.Where(step => step.Input.CloseUtc > dayEnd).SelectMany(step => step.Signals));
        Signal controlNext = Assert.Single(controlSteps.Where(step => step.Input.CloseUtc > dayEnd).SelectMany(step => step.Signals));
        Assert.Equal(Data.SellId(nextDay), next.ZoneId);
        Assert.Equal(Side.Sell, next.Side);
        Assert.Equal(Data.BuyId(nextDay), controlNext.ZoneId);
        Assert.Equal(Data.Evaluation(nextDay), next.ConfirmationCloseUtc);
    }

    // ------------------------------------------------------------------ too few D1 bars

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(79, false)] // too few for EMA(20) (80 closes) and for EMA(50) (200 closes)
    [InlineData(80, false)] // EMA(20) only
    [InlineData(199, false)] // one close short of EMA(50)
    [InlineData(200, true)] // both
    public void OnBarClosed_TooFewD1BarsForAnEma_NoSetup(int closedAtEvaluation, bool setup)
    {
        // The D1 series starts so that exactly this many D1 bars have closed at the evaluation. The engine is
        // not ready before 201 D1 bars, so the answer is read from RangeSetup, which is shown also before.
        DateTime first = LastBeforeWinter.AddDays(-(closedAtEvaluation - 1));
        List<Bar> d1 = D1(first, Rising(LastBeforeWinter, -0.05), false);
        Assert.Equal(closedAtEvaluation, ClosedCloses(d1, Data.Evaluation(Data.Winter), false).Count);
        SignalEngine engine = Engine(20);

        Run(engine, WithD1(d1, false, Data.Winter).Feed.Where(item => item.CloseUtc <= Data.Evaluation(Data.Winter)));

        Assert.Equal(setup, engine.RangeSetup is not null);
        Assert.Equal(setup ? Bias.Long : Bias.Neutral, engine.RangeDirection);
        if (setup)
        {
            Assert.Equal(Data.BuyId(Data.Winter), engine.RangeSetup!.Id);
        }
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(240)]
    [InlineData(276)]
    public void OnBarClosed_FastPeriodAboveTheBiasPeriodInTheEngine_TheFastEmaIsNeverThere_NoSetup(int closedAtEvaluation)
    {
        // BotParameters refuses RangeTrendFastEma 60 with BiasEmaPeriod 50; the engine alone does not. It keeps
        // the D1 bars of its warm-up plus a margin, 4 x 50 + 1 + 16 = 217, fewer than the 240 closes of
        // EMA(60), so EMA(60) is missing while EMA(50) is there, and the engine fails closed on every day.
        DateTime first = LastBeforeWinter.AddDays(-(closedAtEvaluation - 1));
        List<Bar> d1 = D1(first, Rising(LastBeforeWinter, 0.0), false);
        SignalEngine engine = Engine(60);
        SignalEngine control = Engine(20);

        var steps = Run(engine, WithD1(d1, false, Data.Winter).Feed);
        Run(control, WithD1(d1, false, Data.Winter).Feed.Where(item => item.CloseUtc <= Data.Evaluation(Data.Winter)));

        Assert.All(steps, step => Assert.Empty(step.Signals));
        Assert.Null(engine.RangeSetup);
        Assert.Equal(Bias.Neutral, engine.RangeDirection);
        Assert.Equal(217, new BarStore(new SignalParameters(), null, new SessionRangeParameters { RangeTrendFastEma = 60 }, null).Capacity(TimeFrame.D1));
        Assert.Equal(Data.BuyId(Data.Winter), control.RangeSetup!.Id);
    }
}
