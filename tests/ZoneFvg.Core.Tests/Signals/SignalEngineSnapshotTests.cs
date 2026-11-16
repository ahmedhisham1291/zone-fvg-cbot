using System.Collections.ObjectModel;
using Xunit;
using Xunit.Abstractions;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The read-only gap snapshot of the engine for chart drawing (PLT-005, owner decision of
/// 2026-11-16): a copy that never changes engine state, lists the gaps confirmed in the last 24
/// hours of bar time, and is the same in replay and live. Histories from
/// <see cref="EngineScenarios"/> (the reference gap from 100.6 to 100.75, confirmed by m2 at the
/// activation + 45 minutes) and from <see cref="RandomWalkHistory"/>.
/// </summary>
public class SignalEngineSnapshotTests
{
    private const int TradingDays = 260;

    private static readonly (double Open, double High, double Low, double Close) Flat = (100.95, 101.0, 100.9, 100.95);

    private readonly ITestOutputHelper _output;

    public SignalEngineSnapshotTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Seeds of the random-walk runs.</summary>
    public static IEnumerable<object[]> Seeds => new[] { new object[] { 1001 }, new object[] { 1038 }, new object[] { 1075 } };

    [Fact]
    [Trait("Req", "PLT-005")]
    public void RecentFvgs_FreshEngine_Empty()
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);

        Assert.Empty(engine.RecentFvgs);
    }

    [Fact]
    [Trait("Req", "PLT-005")]
    public void RecentFvgs_AfterTheConfirmingBar_ListsTheSignalsGap()
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);
        IReadOnlyList<EngineStep> steps = Feed(engine, ReferenceFeed(BiasHistory.Long, 10), stopAfterTailBar: 2);

        Signal signal = Assert.Single(EngineFeed.AllSignals(steps));
        Fvg fvg = Assert.Single(engine.RecentFvgs);
        Assert.Equal(signal.Fvg, fvg);
        Assert.Equal(Side.Buy, fvg.Side);
        Assert.Equal(EngineScenarios.TailOpen(2), fvg.C1OpenUtc);
    }

    [Fact]
    [Trait("Req", "PLT-005")]
    public void RecentFvgs_BeforeTheConfirmingBar_Empty()
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);
        Feed(engine, ReferenceFeed(BiasHistory.Long, 10), stopAfterTailBar: 1);

        Assert.True(engine.IsReady);
        Assert.Empty(engine.RecentFvgs);
    }

    [Theory]
    [Trait("Req", "PLT-005")]
    [InlineData(BiasHistory.Neutral)]
    [InlineData(BiasHistory.Short)]
    public void RecentFvgs_GapThatConsumesTheZoneAgainstTheBias_IsListedWithoutASignal(BiasHistory bias)
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);
        IReadOnlyList<EngineStep> steps = Feed(engine, ReferenceFeed(bias, 10), stopAfterTailBar: 2);

        Assert.Empty(EngineFeed.AllSignals(steps));
        Fvg fvg = Assert.Single(engine.RecentFvgs);
        Assert.Equal(EngineScenarios.TailOpen(2), fvg.C1OpenUtc);
        Assert.Equal(100.75, fvg.Upper);
        Assert.Equal(100.6, fvg.Lower);
    }

    [Fact]
    [Trait("Req", "PLT-005")]
    public void RecentFvgs_GapConfirmedBeforeTheEngineIsReady_NotListed()
    {
        // M15 completes its warm-up one bar after m2 (SIG-020): m2's gap consumes the zone unseen.
        IReadOnlyList<FeedBar> feed = EngineScenarios.History(BiasHistory.Long, 5, 5, -1, false, EngineScenarios.TouchAndGap, Tail(10));
        var engine = new SignalEngine(EngineScenarios.Defaults);

        IReadOnlyList<EngineStep> steps = EngineFeed.Run(engine, feed);

        Assert.True(engine.IsReady);
        Assert.Empty(EngineFeed.AllSignals(steps));
        Assert.Empty(engine.RecentFvgs);
    }

    [Theory]
    [Trait("Req", "PLT-005")]
    [InlineData(97, true)]
    [InlineData(98, true)]
    [InlineData(99, false)]
    [InlineData(105, false)]
    public void RecentFvgs_GapAge_ListedUpToExactly24Hours(int lastTailBar, bool listed)
    {
        // m2 closes at the activation + 45 minutes. Tail bar k closes at the activation + 15 x (k + 1)
        // minutes: m97 23 h 45 min, m98 exactly 24 h and m99 24 h 15 min after the confirmation.
        var engine = new SignalEngine(EngineScenarios.Defaults);
        Feed(engine, ReferenceFeed(BiasHistory.Long, 110), stopAfterTailBar: lastTailBar);

        if (listed)
        {
            Assert.Equal(EngineScenarios.TailOpen(2), Assert.Single(engine.RecentFvgs).C1OpenUtc);
        }
        else
        {
            Assert.Empty(engine.RecentFvgs);
        }
    }

    [Fact]
    [Trait("Req", "PLT-005")]
    public void RecentFvgs_SnapshotTakenEarlier_DoesNotChangeWhenTheGapDropsOut()
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);
        IReadOnlyList<FeedBar> feed = ReferenceFeed(BiasHistory.Long, 110);
        int confirm = EngineScenarios.IndexOfM15(feed, EngineScenarios.TailOpen(2));
        EngineFeed.Run(engine, feed.Take(confirm + 1));
        IReadOnlyList<Fvg> early = engine.RecentFvgs;
        Fvg gap = Assert.Single(early);

        EngineFeed.Run(engine, feed.Skip(confirm + 1));

        Assert.Empty(engine.RecentFvgs);
        Assert.Equal(gap, Assert.Single(early));
    }

    [Fact]
    [Trait("Req", "PLT-005")]
    public void RecentFvgs_Snapshot_IsAReadOnlyCopy()
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);
        Feed(engine, ReferenceFeed(BiasHistory.Long, 10), stopAfterTailBar: 2);

        IReadOnlyList<Fvg> first = engine.RecentFvgs;
        IReadOnlyList<Fvg> second = engine.RecentFvgs;

        Assert.IsType<ReadOnlyCollection<Fvg>>(first);
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        var list = (IList<Fvg>)first;
        Assert.Throws<NotSupportedException>(() => list[0] = list[0] with { Upper = 0.0 });
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Single(engine.RecentFvgs);
    }

    [Theory]
    [Trait("Req", "PLT-005")]
    [InlineData(BiasHistory.Long)]
    [InlineData(BiasHistory.Neutral)]
    public void ReadingSnapshotsAfterEveryBar_SameSignalsZonesAndBiasAsNotReading(BiasHistory bias)
    {
        IReadOnlyList<FeedBar> feed = ReferenceFeed(bias, 110);

        IReadOnlyList<EngineStep> unread = EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed);
        IReadOnlyList<EngineStep> read = RunReading(new SignalEngine(EngineScenarios.Defaults), feed, false, new List<IReadOnlyList<Fvg>>());

        EngineFeed.AssertSameSteps(unread, read);
    }

    [Fact]
    [Trait("Req", "PLT-005")]
    public void ReplayAndLive_SameSnapshotsAfterEveryBar()
    {
        IReadOnlyList<FeedBar> feed = ReferenceFeed(BiasHistory.Long, 110);
        var live = new List<IReadOnlyList<Fvg>>();
        var replay = new List<IReadOnlyList<Fvg>>();

        IReadOnlyList<EngineStep> liveSteps = RunReading(new SignalEngine(EngineScenarios.Defaults), feed, false, live);
        IReadOnlyList<EngineStep> replaySteps = RunReading(new SignalEngine(EngineScenarios.Defaults), feed, true, replay);

        Assert.Equal(live, replay);
        Assert.Equal(liveSteps.Select(step => step.Zones), replaySteps.Select(step => step.Zones));
        Assert.Contains(live, fvgs => fvgs.Count == 1);
    }

    [Theory]
    [Trait("Req", "PLT-005")]
    [MemberData(nameof(Seeds))]
    public void RandomWalk_ReadingSnapshotsInReplayOrLive_ChangesNothingAndGivesEqualSnapshots(int seed)
    {
        SeededTestFailure.Run(seed, () =>
        {
            IReadOnlyList<FeedBar> feed = RandomWalkHistory.Generate(seed, TradingDays).Feed;
            var live = new List<IReadOnlyList<Fvg>>();
            var replay = new List<IReadOnlyList<Fvg>>();

            IReadOnlyList<EngineStep> unread = EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed);
            IReadOnlyList<EngineStep> liveSteps = RunReading(new SignalEngine(EngineScenarios.Defaults), feed, false, live);
            IReadOnlyList<EngineStep> replaySteps = RunReading(new SignalEngine(EngineScenarios.Defaults), feed, true, replay);

            EngineFeed.AssertSameSteps(unread, liveSteps);
            Assert.Equal(live, replay);
            Assert.Equal(liveSteps.Select(step => step.Zones), replaySteps.Select(step => step.Zones));
            Assert.Equal(
                EngineFeed.AllSignals(unread).Select(signal => signal.ZoneId),
                EngineFeed.AllSignals(replaySteps).Select(signal => signal.ZoneId));
            AssertWithin24Hours(feed, live);
            _output.WriteLine("seed " + seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ": steps with a gap listed " + live.Count(fvgs => fvgs.Count > 0).ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Reference history: TouchAndGap, then flat bars, so the tail has 3 + flat bars (m0 to m(2 + flat)).</summary>
    private static IReadOnlyList<FeedBar> ReferenceFeed(BiasHistory bias, int flat)
    {
        return EngineScenarios.History(bias, 5, 5, 40, false, EngineScenarios.TouchAndGap, Tail(flat));
    }

    private static (double Open, double High, double Low, double Close)[] Tail(int count)
    {
        return Enumerable.Repeat(Flat, count).ToArray();
    }

    private static IReadOnlyList<EngineStep> Feed(SignalEngine engine, IReadOnlyList<FeedBar> feed, int stopAfterTailBar)
    {
        int last = EngineScenarios.IndexOfM15(feed, EngineScenarios.TailOpen(stopAfterTailBar));
        return EngineFeed.Run(engine, feed.Take(last + 1));
    }

    /// <summary>Feeds every bar and reads both snapshots twice after each one, recording the gap snapshot.</summary>
    private static IReadOnlyList<EngineStep> RunReading(SignalEngine engine, IReadOnlyList<FeedBar> feed, bool replay, List<IReadOnlyList<Fvg>> fvgs)
    {
        var steps = new List<EngineStep>(feed.Count);
        foreach (FeedBar item in feed)
        {
            steps.Add(EngineFeed.Step(engine, item, replay));
            _ = engine.Zones;
            _ = engine.RecentFvgs;
            fvgs.Add(engine.RecentFvgs);
        }

        return steps;
    }

    private static void AssertWithin24Hours(IReadOnlyList<FeedBar> feed, IReadOnlyList<IReadOnlyList<Fvg>> snapshots)
    {
        for (int i = 0; i < feed.Count; i++)
        {
            DateTime close = feed[i].CloseUtc;
            foreach (Fvg fvg in snapshots[i])
            {
                TimeSpan age = close - fvg.C1OpenUtc.AddMinutes(15.0);
                Assert.True(age >= TimeSpan.Zero && age <= TimeSpan.FromHours(24.0));
            }
        }
    }
}
