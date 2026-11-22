using System.Globalization;
using Xunit;
using Xunit.Abstractions;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>What the engine showed right after one bar was fed, in Breakout mode (SIG-026).</summary>
public sealed record BreakoutEngineStep(FeedBar Input, IReadOnlyList<Signal> Signals, BreakoutReference? Reference, bool IsReady, Bias Bias);

/// <summary>
/// Property tests of the engine in Breakout mode over seeded random-walk histories (SIG-026), as
/// the SIG-002, SIG-019 and SIG-021 tests do for zones. Each history has 70 weekdays of M15 bars
/// (6,720 bars) with H4 and D1 bars aggregated from them. The parameters have a short warm-up
/// (BiasEmaPeriod 5, MaxZoneAgeBars 20: 21 D1, 148 H4 and 368 M15 bars), so the engine is ready
/// after about 25 days. A failure names its seed, both in the test case name and in the message.
/// </summary>
public class SignalEngineBreakoutPropertyTests
{
    private const int TradingDays = 70;
    private const int CutPoints = 10;
    private const double MirrorPivot = 2.2;

    private static readonly SignalParameters Short = new() { BiasEmaPeriod = 5, MaxZoneAgeBars = 20 };

    private readonly ITestOutputHelper _output;

    public SignalEngineBreakoutPropertyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>The 20 seeds: 1001, 1038, 1075, ..., 1704.</summary>
    public static IEnumerable<object[]> Seeds => Enumerable.Range(0, 20).Select(k => new object[] { 1001 + (37 * k) });

    /// <summary>The first 5 seeds, the even ones fed as replay.</summary>
    public static IEnumerable<object[]> FiveSeeds => Enumerable.Range(0, 5).Select(k => new object[] { 1001 + (37 * k), k % 2 == 0 });

    private static SignalEngine Breakout()
    {
        return new SignalEngine(Short, new BreakoutParameters());
    }

    private static IReadOnlyList<BreakoutEngineStep> Run(SignalEngine engine, IEnumerable<FeedBar> feed, bool replay = false)
    {
        var steps = new List<BreakoutEngineStep>();
        foreach (FeedBar item in feed)
        {
            IReadOnlyList<Signal> signals = engine.OnBarClosed(item.TimeFrame, item.Bar, replay).ToArray();
            steps.Add(new BreakoutEngineStep(item, signals, engine.Reference, engine.IsReady, engine.Bias));
        }

        return steps;
    }

    /// <summary>
    /// A tracker driven directly, the way SIG-026 says the engine drives it: each D1 bar to
    /// OnD1Closed, each M15 bar to OnM15Closed with the bias of the D1 bars so far and the ATR
    /// values over the M15 and H4 bars so far. One entry per bar: the reference after the bar and
    /// the setup returned (null for D1 and H4 bars).
    /// </summary>
    private static IReadOnlyList<(BreakoutReference? Reference, BreakoutSetup? Setup)> Direct(IEnumerable<FeedBar> feed)
    {
        var tracker = new BreakoutTracker(new BreakoutParameters(), Short.RewardRisk);
        var d1 = new List<Bar>();
        var h4 = new List<Bar>();
        var m15 = new List<Bar>();
        var result = new List<(BreakoutReference?, BreakoutSetup?)>();
        foreach (FeedBar item in feed)
        {
            BreakoutSetup? setup = null;
            switch (item.TimeFrame)
            {
                case TimeFrame.D1:
                    d1.Add(item.Bar);
                    tracker.OnD1Closed(item.Bar);
                    break;
                case TimeFrame.H4:
                    h4.Add(item.Bar);
                    break;
                default:
                    m15.Add(item.Bar);
                    Bias bias = BiasCalculator.Compute(d1, Short.BiasEmaPeriod);
                    double? atrM15 = Atr.Value(m15, m15.Count - 1, Short.AtrPeriod);
                    double? atrH4 = Atr.Value(h4, h4.Count - 1, Short.AtrPeriod);
                    setup = tracker.OnM15Closed(item.Bar, bias, atrM15, atrH4);
                    break;
            }

            result.Add((tracker.Reference, setup));
        }

        return result;
    }

    // ------------------------------------------------------------------ the engine drives the tracker

    [Theory]
    [Trait("Req", "SIG-026")]
    [MemberData(nameof(FiveSeeds))]
    public void OnBarClosed_EveryBar_ReferenceAndSignalsEqualThoseOfTheTrackerDrivenDirectly(int seed, bool replay)
    {
        SeededTestFailure.Run(seed, () =>
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            IReadOnlyList<BreakoutEngineStep> steps = Run(Breakout(), history.Feed, replay);
            IReadOnlyList<(BreakoutReference? Reference, BreakoutSetup? Setup)> direct = Direct(history.Feed);

            int signals = 0;
            int withheld = 0;
            Assert.Equal(direct.Count, steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                BreakoutEngineStep step = steps[i];
                string at = "bar " + step.Input.TimeFrame + " closing " + Stamp(step.Input.CloseUtc) + ": ";
                Assert.True(Equals(direct[i].Reference, step.Reference), at + "reference differs");
                BreakoutSetup? setup = direct[i].Setup;
                if (setup is null || !step.IsReady)
                {
                    Assert.True(step.Signals.Count == 0, at + "unexpected signal");
                    withheld += setup is null ? 0 : 1;
                    continue;
                }

                Signal signal = Assert.Single(step.Signals);
                Signal expected = new Signal(setup.Id, setup.Side, setup.Entry, setup.Stop, setup.Target, null, setup.ConfirmationCloseUtc, replay, setup.AtrH4)
                {
                    Kind = SignalKind.Breakout,
                    Level = setup.Level,
                };
                Assert.Equal(ExactRecords.Key(expected), ExactRecords.Key(signal));
                Assert.Null(signal.Fvg);
                signals++;
            }

            _output.WriteLine("seed " + Text(seed) + ": " + Text(signals) + " signals compared, " + Text(withheld) + " setups during the warm-up");
            Assert.True(signals > 0, "seed " + Text(seed) + ": no signal was compared");
        });
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void OnBarClosed_HistoryOnWhichAZoneFvgEngineHoldsZones_BreakoutEngineShowsNoZonesGapsOrActivity()
    {
        RandomWalkHistory history = RandomWalkHistory.Generate(1001, TradingDays);
        var zoneFvg = new SignalEngine(Short);
        SignalEngine breakout = Breakout();
        int zoneBars = 0;
        int activityBars = 0;
        foreach (FeedBar item in history.Feed)
        {
            zoneFvg.OnBarClosed(item.TimeFrame, item.Bar, false);
            breakout.OnBarClosed(item.TimeFrame, item.Bar, false);
            zoneBars += zoneFvg.Zones.Count > 0 ? 1 : 0;
            activityBars += ReferenceEquals(zoneFvg.LastActivity, ZoneActivity.None) ? 0 : 1;

            Assert.Empty(breakout.Zones);
            Assert.Empty(breakout.RecentFvgs);
            Assert.Same(ZoneActivity.None, breakout.LastActivity);
        }

        _output.WriteLine("ZoneFvg engine: zones at " + Text(zoneBars) + " bars, zone activity at " + Text(activityBars) + " bars");
        Assert.True(zoneBars > 0, "the ZoneFvg engine held no zone on this history");
        Assert.True(activityBars > 0, "the ZoneFvg engine reported no zone activity on this history");
    }

    // ------------------------------------------------------------------ determinism (as SIG-002)

    [Theory]
    [Trait("Req", "SIG-026")]
    [MemberData(nameof(Seeds))]
    public void SameHistoryInTwoFreshEngines_IdenticalReferencesAndSignalsBitForBit(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            // The history is generated twice from the seed, so the engines share no object.
            IReadOnlyList<BreakoutEngineStep> first = Run(Breakout(), RandomWalkHistory.Generate(seed, TradingDays).Feed);
            IReadOnlyList<BreakoutEngineStep> second = Run(Breakout(), RandomWalkHistory.Generate(seed, TradingDays).Feed);

            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Input, second[i].Input);
                Assert.Equal(ReferenceKey(first[i].Reference), ReferenceKey(second[i].Reference));
                Assert.Equal(ExactRecords.Keys(first[i].Signals), ExactRecords.Keys(second[i].Signals));
                Assert.Equal(first[i].IsReady, second[i].IsReady);
                Assert.Equal(first[i].Bias, second[i].Bias);
            }

            _output.WriteLine(Summary(seed, first));
        });
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void Histories_ProduceSignalsInAtLeastTenOfTwenty()
    {
        // Without signals the determinism, replay and mirror tests would prove little.
        int withSignals = 0;
        int buys = 0;
        int sells = 0;
        foreach (object[] row in Seeds)
        {
            int seed = (int)row[0];
            IReadOnlyList<BreakoutEngineStep> steps = Run(Breakout(), RandomWalkHistory.Generate(seed, TradingDays).Feed);
            List<Signal> signals = steps.SelectMany(step => step.Signals).ToList();
            _output.WriteLine(Summary(seed, steps));
            withSignals += signals.Count > 0 ? 1 : 0;
            buys += signals.Count(signal => signal.Side == Side.Buy);
            sells += signals.Count(signal => signal.Side == Side.Sell);
        }

        string totals = Text(withSignals) + " of 20 histories with signals, " + Text(buys) + " buy and " + Text(sells) + " sell signals in all";
        _output.WriteLine(totals);
        Assert.True(withSignals >= 10, totals);
        Assert.True(buys > 0 && sells > 0, totals);
    }

    // ------------------------------------------------------------------ replay equals live (as SIG-019)

    [Theory]
    [Trait("Req", "SIG-026")]
    [MemberData(nameof(Seeds))]
    public void EngineStartedOneWarmupBeforeT_EqualsTheFullRunFromT(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            IReadOnlyList<FeedBar> feed = history.Feed;
            IReadOnlyList<BreakoutEngineStep> full = Run(Breakout(), feed);
            IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(Short);
            int[][] counts = PrefixCounts(feed);

            int compared = 0;
            int signalsCompared = 0;
            foreach (int cut in CutIndices(seed, feed, counts, warmup))
            {
                List<FeedBar> replay = FromWarmupBefore(cut, feed, counts, warmup);
                IReadOnlyList<BreakoutEngineStep> partial = Run(Breakout(), replay);
                int start = replay.Count - (feed.Count - cut);
                Assert.Equal(feed[cut], replay[start]);

                // The partial engine holds exactly the warm-up at T: ready at T, not before.
                Assert.All(partial.Take(start), step => Assert.False(step.IsReady));
                for (int k = start; k < partial.Count; k++)
                {
                    BreakoutEngineStep b = partial[k];
                    BreakoutEngineStep a = full[cut + (k - start)];
                    string at = "cut at " + Stamp(feed[cut].CloseUtc) + ", bar " + a.Input.TimeFrame + " closing " + Stamp(a.Input.CloseUtc) + ": ";
                    Assert.Equal(a.Input, b.Input);
                    Assert.True(b.IsReady, at + "the partial engine is not ready");
                    Assert.True(ReferenceKey(a.Reference) == ReferenceKey(b.Reference), at + "references differ: " + ReferenceKey(a.Reference) + " against " + ReferenceKey(b.Reference));
                    Assert.True(ExactRecords.Keys(a.Signals).SequenceEqual(ExactRecords.Keys(b.Signals)), at + "signals differ");
                    Assert.Equal(a.Bias, b.Bias);
                    compared++;
                    signalsCompared += a.Signals.Count;
                }
            }

            _output.WriteLine("seed " + Text(seed) + ": " + Text(compared) + " bars compared after " + Text(CutPoints) + " cut points, " + Text(signalsCompared) + " signals among them");
        });
    }

    // ------------------------------------------------------------------ mirror symmetry (as SIG-021)

    [Theory]
    [Trait("Req", "SIG-026")]
    [MemberData(nameof(Seeds))]
    public void MirroredHistory_ReferencesAndSignalsTurnIntoTheOtherSideWithReflectedPrices(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            IReadOnlyList<BreakoutEngineStep> original = Run(Breakout(), history.Feed);
            IReadOnlyList<BreakoutEngineStep> mirrored = Run(Breakout(), history.MirroredFeed(MirrorPivot));

            Assert.Equal(original.Count, mirrored.Count);
            for (int i = 0; i < original.Count; i++)
            {
                BreakoutEngineStep a = original[i];
                BreakoutEngineStep b = mirrored[i];
                string at = "bar " + a.Input.TimeFrame + " closing " + Stamp(a.Input.CloseUtc) + ": ";
                Assert.Equal(a.Input.CloseUtc, b.Input.CloseUtc);
                Assert.Equal(a.IsReady, b.IsReady);
                Assert.True(Mirror(a.Bias) == b.Bias, at + "bias " + a.Bias + " mirrored to " + b.Bias);
                AssertMirroredReference(a.Reference, b.Reference, at);
                AssertMirroredSignals(a.Signals, b.Signals, at);
            }

            _output.WriteLine(Summary(seed, original));
        });
    }

    // ------------------------------------------------------------------ without breakout parameters

    [Theory]
    [Trait("Req", "SIG-026")]
    [MemberData(nameof(FiveSeeds))]
    public void WithoutBreakoutParameters_TheEngineIsTheZoneFvgEngineBitForBit(int seed, bool replay)
    {
        SeededTestFailure.Run(seed, () =>
        {
            var withNull = new SignalEngine(Short, null);
            IReadOnlyList<EngineStep> expected = EngineFeed.Run(new SignalEngine(Short), RandomWalkHistory.Generate(seed, TradingDays).Feed, replay);
            IReadOnlyList<FeedBar> feed = RandomWalkHistory.Generate(seed, TradingDays).Feed;

            Assert.Equal(expected.Count, feed.Count);
            for (int i = 0; i < feed.Count; i++)
            {
                EngineStep actual = EngineFeed.Step(withNull, feed[i], replay);
                Assert.Null(withNull.Reference);
                Assert.Equal(expected[i].Input, actual.Input);
                Assert.Equal(ExactRecords.Keys(expected[i].Zones), ExactRecords.Keys(actual.Zones));
                Assert.Equal(ExactRecords.Keys(expected[i].Signals), ExactRecords.Keys(actual.Signals));
                Assert.Equal(expected[i].IsReady, actual.IsReady);
                Assert.Equal(expected[i].Bias, actual.Bias);
                Assert.All(actual.Signals, signal =>
                {
                    Assert.Equal(SignalKind.ZoneFvg, signal.Kind);
                    Assert.Null(signal.Level);
                    Assert.NotNull(signal.Fvg);
                });
            }
        });
    }

    [Fact]
    [Trait("Req", "SIG-026")]
    public void WithoutBreakoutParameters_TheFiveHistoriesProduceZoneFvgSignals()
    {
        // Without signals the test above would only prove the absence of a difference in zones.
        int signals = 0;
        foreach (object[] row in FiveSeeds)
        {
            int seed = (int)row[0];
            IReadOnlyList<EngineStep> steps = EngineFeed.Run(new SignalEngine(Short, null), RandomWalkHistory.Generate(seed, TradingDays).Feed);
            int count = EngineFeed.AllSignals(steps).Count;
            _output.WriteLine("seed " + Text(seed) + ": " + Text(count) + " ZoneFvg signals");
            signals += count;
        }

        Assert.True(signals > 0, "the five histories produced no ZoneFvg signal");
    }

    // ------------------------------------------------------------------ helpers

    private static string ReferenceKey(BreakoutReference? reference)
    {
        if (reference is null)
        {
            return "-";
        }

        return reference.DayOpenUtc.Ticks.ToString(CultureInfo.InvariantCulture) + reference.DayOpenUtc.Kind
            + "|" + Bits(reference.High) + "|" + Bits(reference.Low) + "|" + (reference.Armed ? "armed" : "done");
    }

    private static string Bits(double value)
    {
        return BitConverter.DoubleToInt64Bits(value).ToString(CultureInfo.InvariantCulture);
    }

    private static void AssertMirroredReference(BreakoutReference? a, BreakoutReference? b, string at)
    {
        Assert.True((a is null) == (b is null), at + "one run has a reference and the other has none");
        if (a is null)
        {
            return;
        }

        Assert.True(a.DayOpenUtc == b!.DayOpenUtc, at + "reference day differs");
        Assert.True(a.Armed == b.Armed, at + "reference state differs");
        AssertReflected(a.High, b.Low, at + "reference high");
        AssertReflected(a.Low, b.High, at + "reference low");
    }

    private static void AssertMirroredSignals(IReadOnlyList<Signal> original, IReadOnlyList<Signal> mirrored, string at)
    {
        Assert.True(original.Count == mirrored.Count, at + "signal counts differ");
        for (int i = 0; i < original.Count; i++)
        {
            Signal a = original[i];
            Signal b = mirrored[i];
            string what = at + "signal " + a.ZoneId + " ";
            Assert.True(MirrorId(a.ZoneId) == b.ZoneId, what + "mirrored id " + b.ZoneId);
            Assert.True(b.Side == (a.Side == Side.Buy ? Side.Sell : Side.Buy), what + "side");
            Assert.True(a.Kind == SignalKind.Breakout && b.Kind == SignalKind.Breakout, what + "kind");
            Assert.True(a.Fvg is null && b.Fvg is null, what + "gap");
            AssertReflected(a.Entry, b.Entry, what + "entry");
            AssertReflected(a.Stop, b.Stop, what + "stop");
            AssertReflected(a.Target, b.Target, what + "target");
            Assert.True(a.Level.HasValue && b.Level.HasValue, what + "level missing");
            AssertReflected(a.Level!.Value, b.Level!.Value, what + "level");
            Assert.True(a.ConfirmationCloseUtc == b.ConfirmationCloseUtc, what + "confirmation time");
            Assert.True(a.FromReplay == b.FromReplay, what + "replay flag");
            Assert.True(PriceMath.AlmostEqual(a.AtrH4, b.AtrH4, PriceMath.DefaultTolerance), what + "ATR(H4) differs");
        }
    }

    private static void AssertReflected(double original, double mirrored, string what)
    {
        double expected = MirrorPivot - original;
        Assert.True(PriceMath.AlmostEqual(expected, mirrored, PriceMath.DefaultTolerance), what + ": expected " + expected.ToString("R", CultureInfo.InvariantCulture) + " but was " + mirrored.ToString("R", CultureInfo.InvariantCulture));
    }

    private static Bias Mirror(Bias bias)
    {
        return bias switch
        {
            Bias.Long => Bias.Short,
            Bias.Short => Bias.Long,
            _ => Bias.Neutral,
        };
    }

    private static string MirrorId(string id)
    {
        return (id.StartsWith("BH-", StringComparison.Ordinal) ? "BL-" : "BH-") + id.Substring(3);
    }

    /// <summary>counts[t][i] is the number of bars of timeframe t (0 D1, 1 H4, 2 M15) among feed[0..i].</summary>
    private static int[][] PrefixCounts(IReadOnlyList<FeedBar> feed)
    {
        var counts = new[] { new int[feed.Count], new int[feed.Count], new int[feed.Count] };
        int[] running = new int[3];
        for (int i = 0; i < feed.Count; i++)
        {
            running[feed[i].Rank]++;
            for (int t = 0; t < 3; t++)
            {
                counts[t][i] = running[t];
            }
        }

        return counts;
    }

    /// <summary>
    /// Ten distinct M15 bars, in time order, chosen with a generator seeded from the history seed
    /// among the bars at which every timeframe already holds its warm-up and that leave at least
    /// 400 bars of the feed after them.
    /// </summary>
    private static IEnumerable<int> CutIndices(int seed, IReadOnlyList<FeedBar> feed, int[][] counts, IReadOnlyDictionary<TimeFrame, int> warmup)
    {
        var candidates = new List<int>();
        for (int i = 0; i < feed.Count - 400; i++)
        {
            bool m15 = feed[i].TimeFrame == TimeFrame.M15;
            bool enough = counts[0][i] >= warmup[TimeFrame.D1] && counts[1][i] >= warmup[TimeFrame.H4] && counts[2][i] >= warmup[TimeFrame.M15];
            if (m15 && enough)
            {
                candidates.Add(i);
            }
        }

        Assert.True(candidates.Count >= CutPoints, "Too few candidate cut points.");
        var random = new SeededGenerator(seed + 7919);
        var chosen = new SortedSet<int>();
        while (chosen.Count < CutPoints)
        {
            chosen.Add(candidates[random.Next(candidates.Count)]);
        }

        return chosen;
    }

    /// <summary>
    /// The replay feed for a cut at feed[cut], closing at T: of the bars closing at or before T,
    /// only the last warm-up count of each timeframe; then every bar after T.
    /// </summary>
    private static List<FeedBar> FromWarmupBefore(int cut, IReadOnlyList<FeedBar> feed, int[][] counts, IReadOnlyDictionary<TimeFrame, int> warmup)
    {
        var replay = new List<FeedBar>();
        for (int i = 0; i < feed.Count; i++)
        {
            int t = feed[i].Rank;
            int firstKept = counts[t][cut] - warmup[feed[i].TimeFrame] + 1;
            if (i > cut || counts[t][i] >= firstKept)
            {
                replay.Add(feed[i]);
            }
        }

        return replay;
    }

    private static string Summary(int seed, IReadOnlyList<BreakoutEngineStep> steps)
    {
        List<Signal> signals = steps.SelectMany(step => step.Signals).ToList();
        return "seed " + Text(seed) + ": " + Text(steps.Count(step => step.IsReady)) + " bars ready, "
            + Text(signals.Count(signal => signal.Side == Side.Buy)) + " buy and " + Text(signals.Count(signal => signal.Side == Side.Sell)) + " sell signals";
    }

    private static string Stamp(DateTime timeUtc)
    {
        return timeUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
