using System.Globalization;
using Xunit;
using Xunit.Abstractions;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Property tests over 20 seeded random-walk histories (SIG-002, SIG-019, SIG-021). Each history
/// has 260 weekdays of M15 bars (24,960 bars) with H4 and D1 bars aggregated from them, so the
/// engine (201 D1 bars) becomes ready after about 201 days and runs ready for about 59 days. A
/// failure names its seed, both in the test case name and in the message.
/// </summary>
public class SignalEnginePropertyTests
{
    private const int TradingDays = 260;
    private const int CutPoints = 10;
    private const double MirrorPivot = 2.2;

    private static readonly SignalParameters Defaults = new();

    private readonly ITestOutputHelper _output;

    public SignalEnginePropertyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>The 20 seeds: 1001, 1038, 1075, ..., 1704.</summary>
    public static IEnumerable<object[]> Seeds => Enumerable.Range(0, 20).Select(k => new object[] { 1001 + (37 * k) });

    // ------------------------------------------------------------------ SIG-002

    [Theory]
    [Trait("Req", "SIG-002")]
    [MemberData(nameof(Seeds))]
    public void SameHistoryInTwoFreshEngines_IdenticalZonesAndSignalsBitForBit(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            // The history is generated twice from the seed, so the engines share no object.
            IReadOnlyList<EngineStep> first = EngineFeed.Run(new SignalEngine(Defaults), RandomWalkHistory.Generate(seed, TradingDays).Feed);
            IReadOnlyList<EngineStep> second = EngineFeed.Run(new SignalEngine(Defaults), RandomWalkHistory.Generate(seed, TradingDays).Feed);

            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Input, second[i].Input);
                Assert.Equal(ExactRecords.Keys(first[i].Zones), ExactRecords.Keys(second[i].Zones));
                Assert.Equal(ExactRecords.Keys(first[i].Signals), ExactRecords.Keys(second[i].Signals));
                Assert.Equal(first[i].IsReady, second[i].IsReady);
                Assert.Equal(first[i].Bias, second[i].Bias);
            }

            _output.WriteLine(Summary(seed, first));
        });
    }

    // ------------------------------------------------------------------ SIG-019

    [Theory]
    [Trait("Req", "SIG-019")]
    [MemberData(nameof(Seeds))]
    public void EngineStartedOneWarmupBeforeT_EqualsTheFullRunFromT(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            IReadOnlyList<FeedBar> feed = history.Feed;
            IReadOnlyList<EngineStep> full = EngineFeed.Run(new SignalEngine(Defaults), feed);
            IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(Defaults);
            int[][] counts = PrefixCounts(feed);

            int compared = 0;
            int signalsCompared = 0;
            foreach (int cut in CutIndices(seed, feed, counts, warmup))
            {
                List<FeedBar> replay = FromWarmupBefore(cut, feed, counts, warmup);
                IReadOnlyList<EngineStep> partial = EngineFeed.Run(new SignalEngine(Defaults), replay);
                int start = replay.Count - (feed.Count - cut);
                Assert.Equal(feed[cut], replay[start]);

                // The partial engine holds exactly the warm-up at T: ready at T, not before.
                Assert.All(partial.Take(start), step => Assert.False(step.IsReady));
                for (int k = start; k < partial.Count; k++)
                {
                    EngineStep b = partial[k];
                    EngineStep a = full[cut + (k - start)];
                    string at = "cut at " + Stamp(feed[cut].CloseUtc) + ", bar " + a.Input.TimeFrame + " closing " + Stamp(a.Input.CloseUtc) + ": ";
                    Assert.Equal(a.Input, b.Input);
                    Assert.True(b.IsReady, at + "the partial engine is not ready");
                    Assert.True(ExactRecords.Keys(a.Zones).SequenceEqual(ExactRecords.Keys(b.Zones)), at + "zones differ");
                    Assert.True(ExactRecords.Keys(a.Signals).SequenceEqual(ExactRecords.Keys(b.Signals)), at + "signals differ");
                    Assert.Equal(a.Bias, b.Bias);
                    compared++;
                    signalsCompared += a.Signals.Count;
                }
            }

            _output.WriteLine("seed " + Text(seed) + ": " + Text(compared) + " bars compared after " + Text(CutPoints) + " cut points, " + Text(signalsCompared) + " signals among them");
        });
    }

    // ------------------------------------------------------------------ SIG-021

    [Theory]
    [Trait("Req", "SIG-021")]
    [MemberData(nameof(Seeds))]
    public void MirroredHistory_ZoneTypesAndSidesSwapWithReflectedPrices(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            RandomWalkHistory history = RandomWalkHistory.Generate(seed, TradingDays);
            IReadOnlyList<EngineStep> original = EngineFeed.Run(new SignalEngine(Defaults), history.Feed);
            IReadOnlyList<EngineStep> mirrored = EngineFeed.Run(new SignalEngine(Defaults), history.MirroredFeed(MirrorPivot));

            Assert.Equal(original.Count, mirrored.Count);
            for (int i = 0; i < original.Count; i++)
            {
                EngineStep a = original[i];
                EngineStep b = mirrored[i];
                string at = "bar " + a.Input.TimeFrame + " closing " + Stamp(a.Input.CloseUtc) + ": ";
                Assert.Equal(a.Input.CloseUtc, b.Input.CloseUtc);
                Assert.Equal(a.IsReady, b.IsReady);
                Assert.True(Mirror(a.Bias) == b.Bias, at + "bias " + a.Bias + " mirrored to " + b.Bias);
                AssertMirroredZones(a.Zones, b.Zones, at);
                AssertMirroredSignals(a.Signals, b.Signals, at);
            }
        });
    }

    // ------------------------------------------------------------------ the histories exercise the engine

    [Fact]
    [Trait("Req", "SIG-002")]
    [Trait("Req", "SIG-019")]
    [Trait("Req", "SIG-021")]
    public void Histories_ProduceZonesAndSignalsOfBothSides()
    {
        // Without zones and signals the property tests above would prove little. The histories are
        // fixed by their seeds; when this was written every one showed 41 to 61 zones while ready
        // and 1 to 8 signals, 45 buys and 34 sells in all. The limits below sit just under that.
        int buys = 0;
        int sells = 0;
        foreach (object[] row in Seeds)
        {
            int seed = (int)row[0];
            IReadOnlyList<EngineStep> steps = EngineFeed.Run(new SignalEngine(Defaults), RandomWalkHistory.Generate(seed, TradingDays).Feed);
            IReadOnlyList<Signal> signals = EngineFeed.AllSignals(steps);
            int zones = steps.SelectMany(step => step.Zones).Select(zone => zone.Id).Distinct(StringComparer.Ordinal).Count();
            string summary = Summary(seed, steps);
            _output.WriteLine(summary);
            Assert.True(zones >= 40, summary);
            Assert.True(signals.Count >= 1, summary);
            buys += signals.Count(signal => signal.Side == Side.Buy);
            sells += signals.Count(signal => signal.Side == Side.Sell);
        }

        string totals = Text(buys) + " buy and " + Text(sells) + " sell signals in all";
        _output.WriteLine(totals);
        Assert.True(buys >= 40 && sells >= 30, totals);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// counts[t][i] is the number of bars of timeframe t (0 D1, 1 H4, 2 M15) among feed[0..i].
    /// </summary>
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
        return (id[0] == 'D' ? "S" : "D") + id.Substring(1);
    }

    private static void AssertMirroredZones(IReadOnlyList<Zone> original, IReadOnlyList<Zone> mirrored, string at)
    {
        Assert.True(original.Count == mirrored.Count, at + "zone counts differ");
        Dictionary<string, Zone> byId = mirrored.ToDictionary(zone => zone.Id, StringComparer.Ordinal);
        foreach (Zone a in original)
        {
            Assert.True(byId.TryGetValue(MirrorId(a.Id), out Zone? b), at + "no mirrored zone for " + a.Id);
            string what = at + "zone " + a.Id + " ";
            Assert.True(b!.Type == (a.Type == ZoneType.Demand ? ZoneType.Supply : ZoneType.Demand), what + "type");
            AssertReflected(a.Proximal, b.Proximal, what + "proximal");
            AssertReflected(a.Distal, b.Distal, what + "distal");
            Assert.True(a.ActivationTimeUtc == b.ActivationTimeUtc, what + "activation");
            Assert.True(PriceMath.AlmostEqual(a.AtrAtCreation, b.AtrAtCreation, PriceMath.DefaultTolerance), what + "ATR");
            Assert.True(a.State == b.State, what + "state " + a.State + " against " + b.State);
            Assert.True(a.TouchBarOpenUtc == b.TouchBarOpenUtc, what + "touch bar");
            Assert.True(a.ExtremeSinceTouch.HasValue == b.ExtremeSinceTouch.HasValue, what + "extreme presence");
            if (a.ExtremeSinceTouch.HasValue)
            {
                AssertReflected(a.ExtremeSinceTouch.Value, b.ExtremeSinceTouch!.Value, what + "extreme");
            }
        }
    }

    private static void AssertMirroredSignals(IReadOnlyList<Signal> original, IReadOnlyList<Signal> mirrored, string at)
    {
        Assert.True(original.Count == mirrored.Count, at + "signal counts differ");
        for (int i = 0; i < original.Count; i++)
        {
            Signal a = original[i];
            Signal b = mirrored[i];
            string what = at + "signal of " + a.ZoneId + " ";
            Assert.True(MirrorId(a.ZoneId) == b.ZoneId, what + "zone id " + b.ZoneId);
            Assert.True(b.Side == (a.Side == Side.Buy ? Side.Sell : Side.Buy), what + "side");
            Assert.True(b.Fvg.Side == b.Side, what + "gap side");
            AssertReflected(a.Entry, b.Entry, what + "entry");
            AssertReflected(a.Stop, b.Stop, what + "stop");
            AssertReflected(a.Target, b.Target, what + "target");
            AssertReflected(a.Fvg.Upper, b.Fvg.Lower, what + "gap upper");
            AssertReflected(a.Fvg.Lower, b.Fvg.Upper, what + "gap lower");
            Assert.True(a.Fvg.C3OpenUtc == b.Fvg.C3OpenUtc && a.Fvg.C2OpenUtc == b.Fvg.C2OpenUtc && a.Fvg.C1OpenUtc == b.Fvg.C1OpenUtc, what + "gap bar times");
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

    private static string Summary(int seed, IReadOnlyList<EngineStep> steps)
    {
        IReadOnlyList<Signal> signals = EngineFeed.AllSignals(steps);
        int zones = steps.SelectMany(step => step.Zones).Select(zone => zone.Id).Distinct(StringComparer.Ordinal).Count();
        int readyBars = steps.Count(step => step.IsReady);
        return "seed " + Text(seed) + ": " + Text(steps.Count) + " bars, " + Text(readyBars) + " ready, " + Text(zones) + " zones shown, "
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
