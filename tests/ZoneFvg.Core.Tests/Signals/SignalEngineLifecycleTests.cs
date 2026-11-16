using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Zone lifecycle through the engine (SIG-014), one signal per zone and the bias gate (SIG-018),
/// and the levels of the reference signal (SIG-017). Histories from <see cref="EngineScenarios"/>,
/// with spare bars so that the engine is ready before the touch.
/// </summary>
public class SignalEngineLifecycleTests
{
    // ------------------------------------------------------------------ SIG-014

    [Fact]
    [Trait("Req", "SIG-014")]
    public void ConfirmingBar_TouchedZoneIsSignalledAndLeavesTheZones()
    {
        IReadOnlyList<EngineStep> steps = Run(BiasHistory.Long, EngineScenarios.TouchAndGap, EngineScenarios.SecondGap);

        // At the activation the zone is Active, at m0 and m1 Touched (touch bar m0, extreme 100.1).
        Zone active = Assert.Single(StepBefore(steps, 0).Zones);
        Assert.Equal(ZoneState.Active, active.State);
        Zone touched = Assert.Single(StepAt(steps, 1).Zones);
        Assert.Equal(ZoneState.Touched, touched.State);
        Assert.Equal(EngineScenarios.Activation, touched.TouchBarOpenUtc);
        Assert.Equal(100.1, touched.ExtremeSinceTouch);

        // m2 confirms the gap: one signal, and the zone has left the non-terminal zones for good.
        EngineStep m2 = StepAt(steps, 2);
        Assert.Equal(EngineScenarios.ZoneId, Assert.Single(m2.Signals).ZoneId);
        Assert.All(steps.Skip(IndexOf(steps, 2)), step => Assert.Empty(step.Zones));
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    [Trait("Req", "SIG-016")]
    public void GapConfirmedOnTheLastBarOfTheWindow_IsUsed()
    {
        // Touch at m0, flat bars m1 to m14, c2 at m15, c1 at m16 = ConfirmWindowBars after the
        // touch. The window end runs after the gap search, so m16 still signals.
        IReadOnlyList<EngineStep> steps = Run(BiasHistory.Long, WindowTail(16, true));

        Assert.Equal(ZoneState.Touched, Assert.Single(StepAt(steps, 15).Zones).State);
        Signal signal = Assert.Single(EngineFeed.AllSignals(steps));
        Assert.Equal(EngineScenarios.TailOpen(16), signal.Fvg.C1OpenUtc);
        Assert.Equal(EngineScenarios.TailOpen(14), signal.Fvg.C3OpenUtc);
        Assert.Empty(StepAt(steps, 16).Zones);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    [Trait("Req", "SIG-016")]
    public void GapConfirmedOneBarAfterTheWindow_IsNotUsed()
    {
        // The same gap one bar later (c1 at m17): the zone became Used at m16, so nothing is returned.
        IReadOnlyList<EngineStep> steps = Run(BiasHistory.Long, WindowTail(17, true));

        Assert.Empty(EngineFeed.AllSignals(steps));
        Assert.Empty(StepAt(steps, 16).Zones);
        Assert.Empty(StepAt(steps, 17).Zones);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void WindowEndsWithoutGap_ZoneEndsUsedWithoutSignal()
    {
        // Only flat bars after the touch: Touched through m15, gone after m16, never a signal.
        IReadOnlyList<EngineStep> steps = Run(BiasHistory.Long, WindowTail(20, false));

        Assert.Equal(ZoneState.Touched, Assert.Single(StepAt(steps, 15).Zones).State);
        Assert.Empty(StepAt(steps, 16).Zones);
        Assert.Empty(EngineFeed.AllSignals(steps));
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void CloseBelowDistalAfterTouch_ZoneIsInvalidAndNeverSignals()
    {
        // m1 closes at 99.4, below the distal 99.5: the zone is Invalid. m2 to m4 then form a gap
        // that would be valid for a Touched zone; nothing is returned.
        (double, double, double, double)[] tail =
        {
            (100.6, 100.6, 100.1, 100.2),
            (100.2, 100.2, 99.3, 99.4),
            (99.4, 100.3, 99.4, 100.25),
            (100.25, 100.8, 100.25, 100.75),
            (100.75, 100.9, 100.5, 100.85),
        };

        IReadOnlyList<EngineStep> steps = Run(BiasHistory.Long, tail);

        Assert.Empty(StepAt(steps, 1).Zones);
        Assert.Empty(EngineFeed.AllSignals(steps));
    }

    // ------------------------------------------------------------------ SIG-017 through the engine

    [Fact]
    [Trait("Req", "SIG-017")]
    public void ReferenceSignal_LevelsAndTimesAsWorkedByHand()
    {
        IReadOnlyList<EngineStep> steps = Run(BiasHistory.Long, EngineScenarios.TouchAndGap);

        Signal signal = Assert.Single(StepAt(steps, 2).Signals);

        Assert.Equal(EngineScenarios.ZoneId, signal.ZoneId);
        Assert.Equal(Side.Buy, signal.Side);
        AssertNear(EngineScenarios.ReferenceEntry, signal.Entry);
        AssertNear(EngineScenarios.ReferenceStop, signal.Stop);
        AssertNear(EngineScenarios.ReferenceTarget, signal.Target);
        Assert.Equal(new Fvg(Side.Buy, 100.75, 100.6, EngineScenarios.TailOpen(0), EngineScenarios.TailOpen(1), EngineScenarios.TailOpen(2)), signal.Fvg);
        Assert.Equal(EngineScenarios.TailOpen(3), signal.ConfirmationCloseUtc);
        Assert.False(signal.FromReplay);

        // The latest closed H4 bar at m2 is the leg bar: ATR(H4) = (13 x 1.0 + 2.0) / 14 = 15 / 14.
        AssertNear(15.0 / 14.0, signal.AtrH4);
    }

    [Fact]
    [Trait("Req", "SIG-017")]
    public void ReferenceSignal_ReplayFlag_IsCarried()
    {
        IReadOnlyList<FeedBar> feed = EngineScenarios.History(BiasHistory.Long, 5, 5, 40, false, EngineScenarios.TouchAndGap);

        IReadOnlyList<EngineStep> steps = EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed, replay: true);

        Assert.True(Assert.Single(EngineFeed.AllSignals(steps)).FromReplay);
    }

    [Fact]
    [Trait("Req", "SIG-017")]
    public void MirroredReferenceSignal_IsASellWithReflectedLevels()
    {
        IReadOnlyList<FeedBar> feed = EngineScenarios.History(BiasHistory.Short, 5, 5, 40, true, EngineScenarios.TouchAndGap);

        Signal signal = Assert.Single(EngineFeed.AllSignals(EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed)));

        // Entry 200 - 100.675 = 99.325; stop 200 - 99.481785714 = 100.518214286;
        // target 200 - 103.061428571 = 96.938571429.
        Assert.Equal(EngineScenarios.MirroredZoneId, signal.ZoneId);
        Assert.Equal(Side.Sell, signal.Side);
        AssertNear(99.325, signal.Entry);
        AssertNear(100.51821428571429, signal.Stop);
        AssertNear(96.93857142857143, signal.Target);
        AssertNear(99.4, signal.Fvg.Upper);
        AssertNear(99.25, signal.Fvg.Lower);

        // Reflection keeps every bar range, so ATR(H4) is the same 15 / 14 as for the buy.
        AssertNear(15.0 / 14.0, signal.AtrH4);
    }

    // ------------------------------------------------------------------ SIG-018

    [Fact]
    [Trait("Req", "SIG-018")]
    public void SecondValidGapAfterTheFirst_IsIgnored()
    {
        // m2 signals; m6 would confirm another valid gap (see SignalEngineWarmupTests
        // .ZoneStillTouched_SecondGapIsValid) but the zone already produced its one signal.
        IReadOnlyList<EngineStep> steps = Run(BiasHistory.Long, EngineScenarios.TouchAndGap, EngineScenarios.SecondGap);

        Signal signal = Assert.Single(EngineFeed.AllSignals(steps));
        Assert.Equal(EngineScenarios.TailOpen(2), signal.Fvg.C1OpenUtc);
        Assert.Empty(StepAt(steps, 6).Signals);
    }

    [Theory]
    [Trait("Req", "SIG-018")]
    [InlineData(BiasHistory.Short)]
    [InlineData(BiasHistory.Neutral)]
    public void BiasDoesNotMatch_ZoneIsConsumedWithoutSignal(BiasHistory bias)
    {
        IReadOnlyList<EngineStep> steps = Run(bias, EngineScenarios.TouchAndGap, EngineScenarios.SecondGap);

        Assert.Equal(ZoneState.Touched, Assert.Single(StepAt(steps, 1).Zones).State);
        Assert.Empty(EngineFeed.AllSignals(steps));
        Assert.All(steps.Skip(IndexOf(steps, 2)), step => Assert.Empty(step.Zones));
    }

    [Theory]
    [Trait("Req", "SIG-018")]
    [InlineData(BiasHistory.ShortThenLong, Bias.Short, Bias.Long, true)]
    [InlineData(BiasHistory.LongThenShort, Bias.Long, Bias.Short, false)]
    public void BiasChangesOnABarClosingWithTheConfirmationBar_TheNewBiasDecides(BiasHistory history, Bias atM1, Bias atM2, bool signalled)
    {
        // The last D1 bar opens 45 minutes after midnight, so it closes at the same instant as m2
        // and is processed just before it (D1 before M15).
        IReadOnlyList<Bar> d1 = EngineScenarios.D1(history, 206, TimeSpan.FromMinutes(45));
        IReadOnlyList<FeedBar> feed = EngineFeed.Merge(d1, EngineScenarios.DemandH4(253), EngineScenarios.M15(2005, EngineScenarios.TouchAndGap));

        IReadOnlyList<EngineStep> steps = EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed);

        Assert.Equal(atM1, StepAt(steps, 1).Bias);
        int m2 = IndexOf(steps, 2);
        Assert.Equal(TimeFrame.D1, steps[m2 - 1].Input.TimeFrame);
        Assert.Equal(atM2, steps[m2].Bias);
        Assert.Equal(signalled ? 1 : 0, steps[m2].Signals.Count);
        Assert.Empty(steps[m2].Zones);
    }

    [Fact]
    [Trait("Req", "SIG-018")]
    public void TwoZonesConfirmedOnTheSameBar_BothSignalOrderedById()
    {
        // Zone 1 is the reference zone D-20260301-0800 (proximal 100.2, distal 99.5). Zone 2 is
        // built 45 H4 bars later at 0.5 lower: flat bars (99.5, 100.0, 99.0, 99.5), a swing high
        // at 100.5, 22 flat bars, base bar (99.7, 100.0, 99.0, 99.5) and leg 99.5 to 101.5. Its base
        // is the three bars before the leg, so its id is D-20260308-2000; proximal 99.7, distal
        // 99.0, active from 2026-03-09 12:00. The two ranges share 99.5 to 99.7, 0.2, not more than
        // half of zone 1's height 0.7, so zone 1 stays.
        // M15 stays at 100.6 until zone 2 is active, then m0 = (99.9, 99.95, 99.6, 99.8) touches
        // both zones, m1 = (99.8, 100.4, 99.8, 100.35) is bullish and m2 = (100.35, 100.5, 100.2,
        // 100.45) confirms a gap from 99.95 to 100.2 for both. ATR(M15) as of m2 = (11 x 0.1 + 1.0
        // + 0.6 + 0.3) / 14 = 3.0 / 14. The lowest low since the touch is 99.6, so the stops are
        // 99.5 - 0.3 / 14 = 99.478571429 and 99.0 - 0.3 / 14 = 98.978571429. Entry for both:
        // 100.2 - 0.5 x 0.25 = 100.075.
        DateTime activation2 = EngineScenarios.Activation.AddHours(4 * 45);
        IReadOnlyList<Bar> h4 = EngineScenarios.DemandH4(253)
            .Concat(new BarBuilder(TimeFrame.H4)
                .At(EngineScenarios.Activation)
                .Repeat(20, (b, _) => b.Ohlc(99.5, 100.0, 99.0, 99.5))
                .Ohlc(99.5, 100.5, 99.0, 99.5)
                .Repeat(22, (b, _) => b.Ohlc(99.5, 100.0, 99.0, 99.5))
                .Ohlc(99.7, 100.0, 99.0, 99.5)
                .Bullish(99.5, 101.5)
                .Build())
            .ToList();
        IReadOnlyList<Bar> m15 = new BarBuilder(TimeFrame.M15)
            .At(activation2.AddMinutes(-15 * 2800))
            .Repeat(2800, (b, _) => b.Ohlc(100.6, 100.65, 100.55, 100.6))
            .Ohlc(99.9, 99.95, 99.6, 99.8)
            .Ohlc(99.8, 100.4, 99.8, 100.35)
            .Ohlc(100.35, 100.5, 100.2, 100.45)
            .Build();
        IReadOnlyList<FeedBar> feed = EngineFeed.Merge(EngineScenarios.D1(BiasHistory.Long, 206), h4, m15);

        IReadOnlyList<EngineStep> steps = EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed);

        Assert.Equal(activation2, h4[^1].OpenTimeUtc.AddHours(4));
        EngineStep m1 = steps[^2];
        Assert.Equal(new[] { "D-20260301-0800", "D-20260308-2000" }, m1.Zones.Select(zone => zone.Id).ToArray());
        Assert.All(m1.Zones, zone => Assert.Equal(ZoneState.Touched, zone.State));
        EngineStep m2 = steps[^1];
        Assert.Equal(new[] { "D-20260301-0800", "D-20260308-2000" }, m2.Signals.Select(signal => signal.ZoneId).ToArray());
        AssertNear(100.075, m2.Signals[0].Entry);
        AssertNear(100.075, m2.Signals[1].Entry);
        AssertNear(99.5 - (0.3 / 14.0), m2.Signals[0].Stop);
        AssertNear(99.0 - (0.3 / 14.0), m2.Signals[1].Stop);
        Assert.Empty(m2.Zones);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Touch bar m0 of the reference tail, then flat bars (100.3, 100.35, 100.25, 100.3). With
    /// <paramref name="withGap"/>, c2 = (100.3, 100.7, 100.3, 100.65) at bar w - 1 and
    /// c1 = (100.65, 100.8, 100.5, 100.7) at bar w: low(c1) 100.5 above high(c3) 100.35, a gap of
    /// 0.15 within 100.7357. ATR(M15) at c1 is below 0.2, so 0.15 is above 0.25 x ATR. Three more
    /// flat bars follow. Without a gap, flat bars run up to bar w.
    /// </summary>
    private static (double, double, double, double)[] WindowTail(int w, bool withGap)
    {
        var tail = new List<(double, double, double, double)> { (100.6, 100.6, 100.1, 100.2) };
        int flat = withGap ? w - 2 : w;
        for (int i = 0; i < flat; i++)
        {
            tail.Add((100.3, 100.35, 100.25, 100.3));
        }

        if (withGap)
        {
            tail.Add((100.3, 100.7, 100.3, 100.65));
            tail.Add((100.65, 100.8, 100.5, 100.7));
            for (int i = 0; i < 3; i++)
            {
                tail.Add((100.3, 100.35, 100.25, 100.3));
            }
        }

        return tail.ToArray();
    }

    private static IReadOnlyList<EngineStep> Run(BiasHistory bias, params (double Open, double High, double Low, double Close)[][] tails)
    {
        IReadOnlyList<FeedBar> feed = EngineScenarios.History(bias, 5, 5, 40, false, tails);
        return EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed);
    }

    private static int IndexOf(IReadOnlyList<EngineStep> steps, int tailBar)
    {
        DateTime open = EngineScenarios.TailOpen(tailBar);
        int index = steps.ToList().FindIndex(step => step.Input.TimeFrame == TimeFrame.M15 && step.Input.Bar.OpenTimeUtc == open);
        Assert.True(index >= 0, "No step for tail bar " + tailBar.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return index;
    }

    private static EngineStep StepAt(IReadOnlyList<EngineStep> steps, int tailBar)
    {
        return steps[IndexOf(steps, tailBar)];
    }

    private static EngineStep StepBefore(IReadOnlyList<EngineStep> steps, int tailBar)
    {
        return steps[IndexOf(steps, tailBar) - 1];
    }

    private static void AssertNear(double expected, double actual)
    {
        string message = "expected " + expected.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + " but was " + actual.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(PriceMath.AlmostEqual(expected, actual, PriceMath.DefaultTolerance), message);
    }
}
