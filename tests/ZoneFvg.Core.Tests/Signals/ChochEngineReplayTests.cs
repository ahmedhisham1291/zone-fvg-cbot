using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-041: the Choch model in the signal engine is deterministic, gives the same signals whether
/// it starts at the minimal warm-up or earlier (replay equals live), is mirror symmetric (Swept
/// and the target source included), and has its own M15 warm-up. SIG-035: without Choch
/// parameters the engine is the Fvg model, field by field.
/// </summary>
public class ChochEngineReplayTests
{
    private static IReadOnlyList<FeedBar> Feed((double Open, double High, double Low, double Close)[] tail, bool mirrored = false, int extraD1 = 0, int extraH4 = 0, int extraM15 = 0)
    {
        BiasHistory bias = mirrored ? BiasHistory.Short : BiasHistory.Long;
        IReadOnlyList<Bar> h4 = EngineScenarios.DemandH4(EngineScenarios.H4Warmup + extraH4);
        return ChochScenarios.History(bias, tail, mirrored, h4, ChochScenarios.Warmup + extraM15, extraD1);
    }

    public static IEnumerable<object[]> Tails()
    {
        yield return new object[] { "A" };
        yield return new object[] { "B" };
    }

    private static (double Open, double High, double Low, double Close)[] TailOf(string name)
    {
        return name == "A" ? ChochScenarios.TailA : ChochScenarios.TailB;
    }

    [Theory]
    [Trait("Req", "SIG-041")]
    [MemberData(nameof(Tails))]
    public void SameBarsTwice_SameStepsAndSignals(string tail)
    {
        IReadOnlyList<FeedBar> feed = Feed(TailOf(tail));

        IReadOnlyList<EngineStep> first = EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults, ChochScenarios.Defaults), feed);
        IReadOnlyList<EngineStep> second = EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults, ChochScenarios.Defaults), feed);

        EngineFeed.AssertSameSteps(first, second);
        Assert.Single(EngineFeed.AllSignals(first));
    }

    [Theory]
    [Trait("Req", "SIG-041")]
    [MemberData(nameof(Tails))]
    public void EngineStartedEarlier_SameSignal_ReplayFlagOnly(string tail)
    {
        IReadOnlyList<FeedBar> minimal = Feed(TailOf(tail));
        IReadOnlyList<FeedBar> longer = Feed(TailOf(tail), extraD1: 30, extraH4: 40, extraM15: 400);

        Signal live = Assert.Single(ChochScenarios.Signals(ChochScenarios.Run(minimal)));
        Signal replayed = Assert.Single(ChochScenarios.Signals(ChochScenarios.Run(longer, replay: true)));

        Assert.True(replayed.FromReplay);
        Assert.Equal(live, replayed with { FromReplay = false });
    }

    [Theory]
    [Trait("Req", "SIG-041")]
    [MemberData(nameof(Tails))]
    public void Mirror_SellWithEveryLevelReflected_SweptAndSourceKept(string tail)
    {
        Signal buy = Assert.Single(ChochScenarios.Signals(ChochScenarios.Run(Feed(TailOf(tail)))));
        Signal sell = Assert.Single(ChochScenarios.Signals(ChochScenarios.Run(Feed(TailOf(tail), mirrored: true))));

        Assert.Equal(Side.Buy, buy.Side);
        Assert.Equal(Side.Sell, sell.Side);
        Assert.Equal(EngineScenarios.ZoneId, buy.ZoneId);
        Assert.Equal(EngineScenarios.MirroredZoneId, sell.ZoneId);
        Assert.Equal(EngineScenarios.Pivot - buy.Entry, sell.Entry, 9);
        Assert.Equal(EngineScenarios.Pivot - buy.Stop, sell.Stop, 9);
        Assert.Equal(EngineScenarios.Pivot - buy.Target, sell.Target, 9);
        Assert.Equal(EngineScenarios.Pivot - buy.Level!.Value, sell.Level!.Value, 9);
        Assert.Equal(buy.Swept, sell.Swept);
        Assert.Equal(buy.TargetSource, sell.TargetSource);
        Assert.Equal(buy.ConfirmationCloseUtc, sell.ConfirmationCloseUtc);
        Assert.Equal(buy.AtrH4, sell.AtrH4, 9);
        Assert.Equal(SignalKind.Choch, sell.Kind);
    }

    [Fact]
    [Trait("Req", "SIG-041")]
    [Trait("Req", "SIG-040")]
    public void Mirror_WithAnOpposingSwing_SourceKept()
    {
        IReadOnlyList<Bar> h4 = ChochScenarios.H4WithSwingHigh(103.5);
        Signal buy = Assert.Single(ChochScenarios.Signals(ChochScenarios.Run(ChochScenarios.History(BiasHistory.Long, ChochScenarios.TailA, h4: h4))));
        Signal sell = Assert.Single(ChochScenarios.Signals(ChochScenarios.Run(ChochScenarios.History(BiasHistory.Short, ChochScenarios.TailA, mirrored: true, h4: h4))));

        Assert.StartsWith("swing:", buy.TargetSource, StringComparison.Ordinal);
        Assert.Equal(buy.TargetSource, sell.TargetSource);
        Assert.Equal(EngineScenarios.Pivot - buy.Target, sell.Target, 9);
    }

    [Fact]
    [Trait("Req", "SIG-041")]
    public void Warmup_ChochDefaults_2087M15Bars()
    {
        IReadOnlyDictionary<TimeFrame, int> warmup = WarmupCalculator.For(EngineScenarios.Defaults, null, null, ChochScenarios.Defaults);

        Assert.Equal(new[] { TimeFrame.D1, TimeFrame.H4, TimeFrame.M15 }, warmup.Keys.ToArray());
        Assert.Equal(201, warmup[TimeFrame.D1]);
        Assert.Equal(248, warmup[TimeFrame.H4]);
        Assert.Equal(((120 + 1) * 16) + 32 + 100 + 3 + 14 + 2, warmup[TimeFrame.M15]);
        Assert.Equal(ChochScenarios.Warmup, warmup[TimeFrame.M15]);
        Assert.Equal(2087, new SignalEngine(EngineScenarios.Defaults, ChochScenarios.Defaults).WarmupBars[TimeFrame.M15]);
    }

    [Fact]
    [Trait("Req", "SIG-041")]
    public void Warmup_FollowsTheWindowAndTheSwingStrength_FvgUnchanged()
    {
        var parameters = EngineScenarios.Defaults with { SwingStrength = 5 };

        Assert.Equal(1936 + 96 + 100 + 3 + 14 + 2, WarmupCalculator.For(EngineScenarios.Defaults, null, null, new ChochParameters { ChochWindowBars = 96 })[TimeFrame.M15]);
        Assert.Equal(1936 + 32 + 100 + 5 + 14 + 2, WarmupCalculator.For(parameters, null, null, ChochScenarios.Defaults)[TimeFrame.M15]);
        Assert.Equal(1968, WarmupCalculator.For(EngineScenarios.Defaults, null, null, null)[TimeFrame.M15]);
        Assert.Equal(1968, WarmupCalculator.For(EngineScenarios.Defaults)[TimeFrame.M15]);
    }

    [Fact]
    [Trait("Req", "SIG-041")]
    public void Warmup_ChochWithAnotherMode_Refused()
    {
        Assert.Throws<ArgumentException>(() => WarmupCalculator.For(EngineScenarios.Defaults, new BreakoutParameters(), null, ChochScenarios.Defaults));
        Assert.Throws<ArgumentException>(() => WarmupCalculator.For(EngineScenarios.Defaults, null, new SessionRangeParameters(), ChochScenarios.Defaults));
        Assert.Throws<ArgumentOutOfRangeException>(() => WarmupCalculator.For(EngineScenarios.Defaults, null, null, new ChochParameters { ChochWindowBars = 3 }));
    }

    [Fact]
    [Trait("Req", "SIG-041")]
    public void Constructor_NullOrInvalidChochParameters_Refused()
    {
        Assert.Throws<ArgumentNullException>(() => new SignalEngine(EngineScenarios.Defaults, (ChochParameters)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SignalEngine(EngineScenarios.Defaults, new ChochParameters { MinRewardRisk = 0.5 }));
    }

    [Fact]
    [Trait("Req", "SIG-041")]
    public void NotReadyAtTheBreak_NoSignal_WhenReadyItIsGiven()
    {
        // With Warmup - 13 flat bars m11 is the 2086th M15 bar, one short of the warm-up: the CHoCH at m11
        // consumes the zone without a signal. With Warmup - 12 flat bars the engine is ready at m11.
        IReadOnlyList<ChochStep> steps = ChochScenarios.Run(ChochScenarios.History(BiasHistory.Long, ChochScenarios.TailA, flatBefore: ChochScenarios.Warmup - 13));

        Assert.Empty(ChochScenarios.Signals(steps));
        Assert.Equal(ZoneEndReason.Signalled, Assert.Single(ChochScenarios.At(steps, 11).Activity.Ended).Reason);

        IReadOnlyList<ChochStep> ready = ChochScenarios.Run(ChochScenarios.History(BiasHistory.Long, ChochScenarios.TailA, flatBefore: ChochScenarios.Warmup - 12));
        Assert.Single(ChochScenarios.Signals(ready));
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    public void WithoutChochParameters_TheReferenceFvgSignalIsUnchanged()
    {
        IReadOnlyList<FeedBar> feed = EngineScenarios.History(BiasHistory.Long, 0, 0, 0, false, EngineScenarios.TouchAndGap);

        Signal signal = Assert.Single(EngineFeed.AllSignals(EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed)));

        Assert.Equal(SignalKind.ZoneFvg, signal.Kind);
        Assert.Equal(EngineScenarios.ZoneId, signal.ZoneId);
        Assert.Equal(EngineScenarios.ReferenceEntry, signal.Entry, 9);
        Assert.Equal(EngineScenarios.ReferenceStop, signal.Stop, 9);
        Assert.Equal(EngineScenarios.ReferenceTarget, signal.Target, 9);
        Assert.NotNull(signal.Fvg);
        Assert.Null(signal.Level);
        Assert.Null(signal.Swept);
        Assert.Null(signal.TargetSource);
        Assert.Null(signal.ExpiryUtc);
        Assert.Equal(1968, new SignalEngine(EngineScenarios.Defaults).WarmupBars[TimeFrame.M15]);
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    [Trait("Req", "SIG-037")]
    public void FvgModel_WindowEndStillNoFvg()
    {
        // The Fvg engine on tail A to m8 and flat bars: no gap, so the window of 16 bars after the touch at m7
        // ends at m23 with NoFvg.
        (double, double, double, double)[] tail = ChochScenarios.TailA.Take(9).Concat(Enumerable.Repeat(ChochScenarios.Flat, 20)).ToArray();
        var engine = new SignalEngine(EngineScenarios.Defaults);
        var ends = new List<ZoneEnd>();
        foreach (FeedBar item in EngineScenarios.History(BiasHistory.Long, 0, 0, 30, false, tail))
        {
            engine.OnBarClosed(item.TimeFrame, item.Bar, false);
            ends.AddRange(engine.LastActivity.Ended);
        }

        Assert.Equal(ZoneEndReason.NoFvg, Assert.Single(ends).Reason);
        Assert.DoesNotContain(ends, end => end.Reason == ZoneEndReason.NoChoch);
    }
}
