using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Impulse leg tests (SIG-007). Layouts and the filler bar F are described in
/// <see cref="ZoneScenarios"/>. ATR(14) as of bar e averages the true ranges of bars e-13 to e;
/// every F has true range 1.0, so a window of fillers gives ATR = 1.0 and threshold 2.0.
/// Mirrored cases reflect every price around 200 (p becomes 200 - p).
/// </summary>
public class ImpulseLegTests
{
    private static readonly SignalParameters Defaults = new();

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_OneBarLegExactlyAtThreshold_ReturnsLeg()
    {
        // Scenario A: leg is bar 30, 100.0 -> 102.0, net move 2.0. ATR as of bar 29 = 14 x 1.0 / 14
        // = 1.0, threshold 2.0 x 1.0 = 2.0: kept. Bar 29 is bearish, so the run is one bar.
        // If the leg bar itself were in the ATR window, ATR would be (13 x 1.0 + 2.0) / 14 = 1.0714
        // and the leg would be rejected, so this also shows that the leg bar is excluded.
        ImpulseLeg? leg = ImpulseLeg.Find(ZoneScenarios.ScenarioA(), Defaults);

        Assert.NotNull(leg);
        Assert.Equal(ZoneType.Demand, leg!.Type);
        Assert.Equal(30, leg.FirstIndex);
        Assert.Equal(30, leg.LastIndex);
        Assert.Equal(1, leg.BarCount);
        Assert.Equal(1.0, leg.Atr, 9);
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_OneBarLegOneTickBelowThreshold_ReturnsNull()
    {
        // Net move 101.999 - 100.0 = 1.999 < 2.0.
        Assert.Null(ImpulseLeg.Find(ZoneScenarios.ScenarioA(legClose: 101.999), Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_OneBarLegOneTickAboveThreshold_ReturnsLeg()
    {
        // Net move 102.001 - 100.0 = 2.001 >= 2.0.
        ImpulseLeg? leg = ImpulseLeg.Find(ZoneScenarios.ScenarioA(legClose: 102.001), Defaults);

        Assert.NotNull(leg);
        Assert.Equal(30, leg!.FirstIndex);
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_ThreeBarLegAndQualifyingOneBarLeg_ReturnsThreeBarLeg()
    {
        // Bars 28, 29, 30 bullish: 100.0->100.4, 100.4->100.8, 100.8->103.0.
        // Three bars: ATR as of bar 27 = 1.0 (bars 14-27 are fillers), net 103.0 - 100.0 = 3.0 >= 2.0.
        // One bar alone would also qualify: true ranges of bars 28 and 29 are 0.4 each, so ATR as of
        // bar 29 = (12 x 1.0 + 0.4 + 0.4) / 14 = 0.9143, threshold 1.8286, net 103.0 - 100.8 = 2.2.
        // The longest qualifying run wins.
        IReadOnlyList<Bar> bars = ThreeBullishBars();

        ImpulseLeg? leg = ImpulseLeg.Find(bars, Defaults);

        Assert.NotNull(leg);
        Assert.Equal(ZoneType.Demand, leg!.Type);
        Assert.Equal(28, leg.FirstIndex);
        Assert.Equal(30, leg.LastIndex);
        Assert.Equal(3, leg.BarCount);
        Assert.Equal(1.0, leg.Atr, 9);
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_DojiInsideRun_EndsTheRun()
    {
        // Bar 28 bullish 100.0->100.4 (true range 0.4), bar 29 a doji (100.4, 100.6, 100.2, 100.4)
        // with true range 0.4, bar 30 bullish 100.4->102.4.
        // Only bar 30 is in the run. ATR as of bar 29 = (12 x 1.0 + 0.4 + 0.4) / 14 = 12.8 / 14 =
        // 0.914285714, threshold 1.828571, net 2.0: kept as a one-bar leg.
        // Without the doji the three bars 28-30 (net 2.4, ATR as of 27 = 1.0) would have won.
        IReadOnlyList<Bar> bars = ZoneScenarios.Prefix(101.0, 21)
            .Bullish(100.0, 100.4)
            .Ohlc(100.4, 100.6, 100.2, 100.4)
            .Bullish(100.4, 102.4)
            .Build();

        ImpulseLeg? leg = ImpulseLeg.Find(bars, Defaults);

        Assert.NotNull(leg);
        Assert.Equal(30, leg!.FirstIndex);
        Assert.Equal(1, leg.BarCount);
        Assert.Equal(0.914285714, leg.Atr, 9);
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_JustClosedBarIsDoji_ReturnsNull()
    {
        // Bar 29 is a big bullish bar, but the just-closed bar 30 has close == open.
        IReadOnlyList<Bar> bars = ZoneScenarios.Prefix()
            .Bullish(100.0, 103.0)
            .Ohlc(103.0, 103.5, 102.5, 103.0)
            .Build();

        Assert.Null(ImpulseLeg.Find(bars, Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_OppositeBarBeforeQualifyingBar_EndsTheRun()
    {
        // Bar 29 bearish (100.5 -> 100.0, true range 0.5), bar 30 bullish 100.0 -> 102.0.
        // ATR as of bar 29 = (13 x 1.0 + 0.5) / 14 = 0.964285714, threshold 1.928571, net 2.0: kept.
        // The run is one bar because bar 29 points the other way.
        IReadOnlyList<Bar> bars = ZoneScenarios.Prefix()
            .Bearish(100.5, 100.0)
            .Bullish(100.0, 102.0)
            .Build();

        ImpulseLeg? leg = ImpulseLeg.Find(bars, Defaults);

        Assert.NotNull(leg);
        Assert.Equal(30, leg!.FirstIndex);
        Assert.Equal(0.964285714, leg.Atr, 9);
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_FourBullishBars_UsesOnlyTheLastThree()
    {
        // Bars 27-30 bullish, one unit each: 100->101, 101->102, 102->103, 103->104.
        // Each has true range 1.0, so every ATR is 1.0 and the threshold is 2.0.
        // Three bars (28-30): net 104 - 101 = 3.0, qualifies. Four bars are never considered.
        IReadOnlyList<Bar> bars = FourBullishBars(1.0);

        ImpulseLeg? leg = ImpulseLeg.Find(bars, Defaults);

        Assert.NotNull(leg);
        Assert.Equal(28, leg!.FirstIndex);
        Assert.Equal(30, leg.LastIndex);
        Assert.Equal(3, leg.BarCount);
        Assert.Equal(1.0, leg.Atr, 9);
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_OnlyAFourBarRunWouldQualify_ReturnsNull()
    {
        // Bars 27-30 bullish, 0.6 each: 100.0->100.6->101.2->101.8->102.4; true range 0.6 each.
        // Three bars (28-30): ATR as of 27 = (13 x 1.0 + 0.6) / 14 = 0.971429, threshold 1.942857,
        //   net 102.4 - 100.6 = 1.8: rejected.
        // Two bars (29-30): ATR as of 28 = (12 + 0.6 + 0.6) / 14 = 0.942857, threshold 1.885714,
        //   net 1.2: rejected.
        // One bar (30): ATR as of 29 = (11 + 1.8) / 14 = 0.914286, threshold 1.828571, net 0.6: rejected.
        // Four bars (27-30) would pass (ATR as of 26 = 1.0, net 2.4) but MaxImpulseBars is 3.
        Assert.Null(ImpulseLeg.Find(FourBullishBars(0.6), Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_MaxImpulseBarsFour_AcceptsTheFourBarRun()
    {
        // Same bars as above; with MaxImpulseBars = 4 the four-bar run 27-30 qualifies:
        // ATR as of bar 26 = 1.0, net 102.4 - 100.0 = 2.4 >= 2.0.
        ImpulseLeg? leg = ImpulseLeg.Find(FourBullishBars(0.6), Defaults with { MaxImpulseBars = 4 });

        Assert.NotNull(leg);
        Assert.Equal(27, leg!.FirstIndex);
        Assert.Equal(4, leg.BarCount);
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_WideBarImmediatelyBeforeLeg_RaisesTheAtrUsed()
    {
        // Bar 29 is the wide doji W (true range 2.4); bar 30 bullish 100.0 -> 102.0.
        // ATR as of bar 29 = (13 x 1.0 + 2.4) / 14 = 1.1, threshold 2.2, net 2.0: rejected.
        // ATR as of bar 28 would be 1.0 and accept the leg, so the bar immediately before the leg
        // must be in the window.
        IReadOnlyList<Bar> bars = ZoneScenarios.Prefix()
            .Ohlc(100.0, 101.2, 98.8, 100.0)
            .Bullish(100.0, 102.0)
            .Build();

        Assert.Null(ImpulseLeg.Find(bars, Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_FifteenBarsBeforeLeg_HasAtrAndReturnsLeg()
    {
        // ATR(14) as of bar 14 needs bars 0-14 (15 bars). Bar 15 is the leg 100.0 -> 102.0.
        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.H4)
            .At(ZoneScenarios.Start)
            .Filler(15)
            .Bullish(100.0, 102.0)
            .Build();

        ImpulseLeg? leg = ImpulseLeg.Find(bars, Defaults);

        Assert.NotNull(leg);
        Assert.Equal(15, leg!.FirstIndex);
        Assert.Equal(1.0, leg.Atr, 9);
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_FourteenBarsBeforeLeg_NoAtrReturnsNull()
    {
        // Only bars 0-13 precede the leg: ATR(14) as of bar 13 is not available, so no leg.
        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.H4)
            .At(ZoneScenarios.Start)
            .Filler(14)
            .Bullish(100.0, 110.0)
            .Build();

        Assert.Null(ImpulseLeg.Find(bars, Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_EmptyBuffer_ReturnsNull()
    {
        Assert.Null(ImpulseLeg.Find(Array.Empty<Bar>(), Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_SingleBullishBarWithNothingBefore_ReturnsNull()
    {
        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.H4).At(ZoneScenarios.Start).Bullish(100.0, 110.0).Build();

        Assert.Null(ImpulseLeg.Find(bars, Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_MirroredOneBarLegAtThreshold_ReturnsBearishLeg()
    {
        // Mirror of scenario A: bar 30 bearish 100.0 -> 98.0, net |98.0 - 100.0| = 2.0 = 2.0 x 1.0.
        ImpulseLeg? leg = ImpulseLeg.Find(ZoneScenarios.ScenarioA().Mirror(200.0), Defaults);

        Assert.NotNull(leg);
        Assert.Equal(ZoneType.Supply, leg!.Type);
        Assert.Equal(30, leg.FirstIndex);
        Assert.Equal(30, leg.LastIndex);
        Assert.Equal(1.0, leg.Atr, 9);
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_MirroredOneBarLegOneTickBelowThreshold_ReturnsNull()
    {
        // Mirror: bearish 100.0 -> 98.001, net 1.999 < 2.0.
        Assert.Null(ImpulseLeg.Find(ZoneScenarios.ScenarioA(legClose: 101.999).Mirror(200.0), Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_MirroredThreeBarLeg_ReturnsThreeBarBearishLeg()
    {
        // Mirror of the three-bar case: bars 28-30 bearish, net 3.0, ATR as of bar 27 = 1.0.
        ImpulseLeg? leg = ImpulseLeg.Find(ThreeBullishBars().Mirror(200.0), Defaults);

        Assert.NotNull(leg);
        Assert.Equal(ZoneType.Supply, leg!.Type);
        Assert.Equal(28, leg.FirstIndex);
        Assert.Equal(3, leg.BarCount);
        Assert.Equal(1.0, leg.Atr, 9);
    }

    [Fact]
    [Trait("Req", "SIG-007")]
    public void Find_MirroredFourBearishBars_UsesOnlyTheLastThree()
    {
        ImpulseLeg? leg = ImpulseLeg.Find(FourBullishBars(1.0).Mirror(200.0), Defaults);

        Assert.NotNull(leg);
        Assert.Equal(ZoneType.Supply, leg!.Type);
        Assert.Equal(28, leg.FirstIndex);
        Assert.Equal(3, leg.BarCount);
    }

    [Fact]
    public void Find_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>("h4Bars", () => ImpulseLeg.Find(null!, Defaults));
        Assert.Throws<ArgumentNullException>("parameters", () => ImpulseLeg.Find(ZoneScenarios.ScenarioA(), null!));
    }

    [Fact]
    public void Find_InvalidParameters_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>("ImpulseAtrMult", () => ImpulseLeg.Find(ZoneScenarios.ScenarioA(), Defaults with { ImpulseAtrMult = 0.0 }));
    }

    private static IReadOnlyList<Bar> ThreeBullishBars()
    {
        return ZoneScenarios.Prefix(101.0, 21)
            .Bullish(100.0, 100.4)
            .Bullish(100.4, 100.8)
            .Bullish(100.8, 103.0)
            .Build();
    }

    private static IReadOnlyList<Bar> FourBullishBars(double step)
    {
        // Bars 0-26 from the prefix (20 fillers after the swing), then four bullish bars 27-30.
        return ZoneScenarios.Prefix(101.0, 20)
            .Bullish(100.0, 100.0 + step)
            .Bullish(100.0 + step, 100.0 + (2.0 * step))
            .Bullish(100.0 + (2.0 * step), 100.0 + (3.0 * step))
            .Bullish(100.0 + (3.0 * step), 100.0 + (4.0 * step))
            .Build();
    }
}
