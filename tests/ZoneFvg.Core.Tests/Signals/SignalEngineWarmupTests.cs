using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Warm-up gate of the engine (SIG-020; owner decision "Warm-up gate" of 2026-11-13). The engine is
/// ready once every timeframe holds its warm-up requirement. Until then it keeps its state up to
/// date but shows no zones and returns no signals; a zone confirmed meanwhile is consumed.
/// </summary>
/// <remarks>
/// The histories are those of <see cref="EngineScenarios"/> under Long bias. With extra counts
/// (0, 0, 0) every timeframe reaches its requirement exactly: D1 (201) and H4 (248) at the
/// activation, M15 (1968) at m2, the bar that confirms the gap.
/// </remarks>
public class SignalEngineWarmupTests
{
    [Fact]
    [Trait("Req", "SIG-020")]
    public void WarmupBars_DefaultParameters_EqualTheCalculatorInD1H4M15Order()
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);

        IReadOnlyDictionary<TimeFrame, int> expected = WarmupCalculator.For(EngineScenarios.Defaults);
        Assert.Equal(expected.ToArray(), engine.WarmupBars.ToArray());
        Assert.Equal(new[] { TimeFrame.D1, TimeFrame.H4, TimeFrame.M15 }, engine.WarmupBars.Keys.ToArray());
        Assert.Equal(201, engine.WarmupBars[TimeFrame.D1]);
        Assert.Equal(248, engine.WarmupBars[TimeFrame.H4]);
        Assert.Equal(1968, engine.WarmupBars[TimeFrame.M15]);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void WarmupBars_CustomParameters_EqualTheCalculator()
    {
        SignalParameters parameters = EngineScenarios.Defaults with { BiasEmaPeriod = 200, MaxZoneAgeBars = 60, ConfirmWindowBars = 8 };

        var engine = new SignalEngine(parameters);

        Assert.Equal(WarmupCalculator.For(parameters).ToArray(), engine.WarmupBars.ToArray());
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void NewEngine_IsNotReadyAndShowsNothing()
    {
        var engine = new SignalEngine(EngineScenarios.Defaults);

        Assert.False(engine.IsReady);
        Assert.Empty(engine.Zones);
        Assert.Equal(Bias.Neutral, engine.Bias);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void Constructor_InvalidOrNullParameters_Throws()
    {
        Assert.Throws<ArgumentNullException>("parameters", () => new SignalEngine(null!));
        Assert.Throws<ArgumentOutOfRangeException>("ConfirmWindowBars", () => new SignalEngine(EngineScenarios.Defaults with { ConfirmWindowBars = 0 }));
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void ReadyEngine_HoldsTheZoneAndReturnsTheSignalAtM2()
    {
        // The reference for the one-short cases below: with spare bars on every timeframe the
        // engine is ready before the touch, shows the zone and returns the signal at m2.
        IReadOnlyList<EngineStep> steps = Run(5, 5, 40);

        EngineStep m1 = StepAt(steps, 1);
        Assert.True(m1.IsReady);
        Assert.Equal(EngineScenarios.ZoneId, Assert.Single(m1.Zones).Id);
        Signal signal = Assert.Single(StepAt(steps, 2).Signals);
        Assert.Equal(EngineScenarios.ZoneId, signal.ZoneId);
    }

    [Theory]
    [Trait("Req", "SIG-020")]
    [InlineData(-1, 0, 0)] // 200 D1 bars: the bias EMA can already be computed, but the engine is not ready
    [InlineData(0, -1, 0)] // 247 H4 bars
    [InlineData(0, 0, -1)] // 1967 M15 bars at m2
    public void OneTimeFrameOneBarShort_NotReadyNoZonesNoSignal(int extraD1, int extraH4, int extraM15)
    {
        IReadOnlyList<EngineStep> steps = Run(extraD1, extraH4, extraM15);

        foreach (EngineStep step in steps)
        {
            Assert.False(step.IsReady);
            Assert.Empty(step.Zones);
            Assert.Empty(step.Signals);
        }

        // The state is still kept: the bias is already Long.
        Assert.Equal(Bias.Long, steps[^1].Bias);
    }

    [Theory]
    [Trait("Req", "SIG-020")]
    [InlineData(0, 1, 4, TimeFrame.D1)] // H4 complete 4 h and M15 complete 15 min before; the D1 bar closing at the activation completes D1 last
    [InlineData(1, 0, 4, TimeFrame.H4)] // the leg bar, closing at the activation, completes H4 last
    [InlineData(1, 1, 0, TimeFrame.M15)] // m2 completes M15 last
    public void BarThatCompletesTheLastTimeFrame_MakesTheEngineReady(int extraD1, int extraH4, int extraM15, TimeFrame last)
    {
        IReadOnlyList<FeedBar> feed = EngineScenarios.History(BiasHistory.Long, extraD1, extraH4, extraM15, false, EngineScenarios.TouchAndGap);
        IReadOnlyList<EngineStep> steps = EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed);

        int firstReady = steps.ToList().FindIndex(step => step.IsReady);

        Assert.True(firstReady > 0);
        Assert.Equal(last, steps[firstReady].Input.TimeFrame);
        DateTime expectedClose = last == TimeFrame.M15 ? EngineScenarios.TailOpen(3) : EngineScenarios.Activation;
        Assert.Equal(expectedClose, steps[firstReady].Input.CloseUtc);
        Assert.All(steps.Take(firstReady), step => Assert.False(step.IsReady));
        Assert.All(steps.Skip(firstReady), step => Assert.True(step.IsReady));
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void FirstReadyBar_ReturnsItsSignal()
    {
        // Extra (0, 0, 0): m2 completes M15 and is also the confirming bar.
        IReadOnlyList<EngineStep> steps = Run(0, 0, 0);

        EngineStep m1 = StepAt(steps, 1);
        EngineStep m2 = StepAt(steps, 2);
        Assert.False(m1.IsReady);
        Assert.Empty(m1.Zones);
        Assert.True(m2.IsReady);
        Assert.Equal(EngineScenarios.ZoneId, Assert.Single(m2.Signals).ZoneId);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void FromTheReadyBarOn_EqualsAnEngineFedALongerHistory()
    {
        // Engine S is fed exactly the warm-up (ready at m2); engine L is fed 30 more D1 and H4 bars
        // and 300 more M15 bars that end with the same bars. From m2 on, both return the same
        // signals and show the same zones and bias, bar for bar, compared field by field.
        IReadOnlyList<EngineStep> shortRun = Run(0, 0, 0, EngineScenarios.TouchAndGap, EngineScenarios.SecondGap);
        IReadOnlyList<EngineStep> longRun = Run(30, 30, 300, EngineScenarios.TouchAndGap, EngineScenarios.SecondGap);

        int fromS = IndexOf(shortRun, 2);
        int fromL = IndexOf(longRun, 2);
        Assert.Equal(shortRun.Count - fromS, longRun.Count - fromL);
        for (int k = 0; k < shortRun.Count - fromS; k++)
        {
            EngineStep s = shortRun[fromS + k];
            EngineStep l = longRun[fromL + k];
            Assert.Equal(l.Input, s.Input);
            Assert.True(s.IsReady);
            Assert.Equal(l.Signals, s.Signals);
            Assert.Equal(l.Zones, s.Zones);
            Assert.Equal(l.Bias, s.Bias);
        }

        Assert.Single(EngineFeed.AllSignals(shortRun));
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void FromTheReadyBarOn_ZonesEqualALongerHistory_WhenTheZoneIsStillOpen()
    {
        // Touch without a gap at m2, then the first valid gap at m6. Engine S is ready at m2 with
        // the zone Touched; engine L shows the same zone from m2 on and both signal at m6.
        IReadOnlyList<EngineStep> shortRun = Run(0, 0, 0, EngineScenarios.TouchWithoutGap, EngineScenarios.SecondGap);
        IReadOnlyList<EngineStep> longRun = Run(30, 30, 300, EngineScenarios.TouchWithoutGap, EngineScenarios.SecondGap);

        int fromS = IndexOf(shortRun, 2);
        int fromL = IndexOf(longRun, 2);
        Assert.Equal(ZoneState.Touched, Assert.Single(shortRun[fromS].Zones).State);
        for (int k = 0; k < shortRun.Count - fromS; k++)
        {
            Assert.Equal(longRun[fromL + k].Signals, shortRun[fromS + k].Signals);
            Assert.Equal(longRun[fromL + k].Zones, shortRun[fromS + k].Zones);
        }

        Signal signal = Assert.Single(EngineFeed.AllSignals(shortRun));
        Assert.Equal(EngineScenarios.TailOpen(6), signal.Fvg.C1OpenUtc);
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void ZoneConfirmedDuringWarmup_IsNeverSignalledAfterwards()
    {
        // One M15 bar short: m2 confirms the gap while the engine is not ready, so the zone is
        // consumed without a signal. m3 makes the engine ready. m6 would confirm a second valid gap
        // for a zone that is still Touched (see ZoneStillTouched_SecondGapIsValid below), but the
        // zone is gone: no signal at all, and no zone shown.
        IReadOnlyList<EngineStep> steps = Run(0, 0, -1, EngineScenarios.TouchAndGap, EngineScenarios.SecondGap);

        Assert.False(StepAt(steps, 2).IsReady);
        Assert.True(StepAt(steps, 3).IsReady);
        Assert.Empty(EngineFeed.AllSignals(steps));
        Assert.All(steps.Skip(IndexOf(steps, 3)), step => Assert.Empty(step.Zones));
    }

    [Fact]
    [Trait("Req", "SIG-020")]
    public void ZoneStillTouched_SecondGapIsValid()
    {
        // Counterpart of the test above: the bars m4, m5, m6 confirm a valid gap for the zone as it
        // stands at m6 when m2 confirmed none, so it is only the earlier consumption that stops it.
        IReadOnlyList<EngineStep> steps = Run(5, 5, 40, EngineScenarios.TouchWithoutGap, EngineScenarios.SecondGap);

        Signal signal = Assert.Single(StepAt(steps, 6).Signals);
        Assert.Equal(EngineScenarios.ZoneId, signal.ZoneId);
    }

    private static IReadOnlyList<EngineStep> Run(int extraD1, int extraH4, int extraM15, params (double Open, double High, double Low, double Close)[][] tails)
    {
        if (tails.Length == 0)
        {
            tails = new[] { EngineScenarios.TouchAndGap };
        }

        IReadOnlyList<FeedBar> feed = EngineScenarios.History(BiasHistory.Long, extraD1, extraH4, extraM15, false, tails);
        return EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed);
    }

    private static int IndexOf(IReadOnlyList<EngineStep> steps, int tailBar)
    {
        DateTime open = EngineScenarios.TailOpen(tailBar);
        return steps.ToList().FindIndex(step => step.Input.TimeFrame == TimeFrame.M15 && step.Input.Bar.OpenTimeUtc == open);
    }

    private static EngineStep StepAt(IReadOnlyList<EngineStep> steps, int tailBar)
    {
        int index = IndexOf(steps, tailBar);
        Assert.True(index >= 0, "No step for tail bar " + tailBar.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return steps[index];
    }
}
