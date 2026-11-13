using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Zone detection tests (SIG-008 to SIG-011, SIG-013). Layouts, the filler bar F, scenario A and
/// the isolated-base layout are described in <see cref="ZoneScenarios"/>. Unless a test says
/// otherwise the swings are those <see cref="SwingDetector"/> confirms on the same bars; in the
/// prefix that is one swing high at bar 6 (101.0 by default). Mirrored cases reflect every price
/// around 200 (p becomes 200 - p), which turns the swing high into a swing low at 99.0.
/// </summary>
public class ZoneDetectorTests
{
    private static readonly SignalParameters Defaults = new();

    // ------------------------------------------------------------------ SIG-008 break of structure

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_CloseAboveSwingHigh_CreatesZone()
    {
        // Scenario A: leg close 102.0 > swing high 101.0 at bar 6.
        ZoneDetection result = DetectWithSwings(ZoneScenarios.ScenarioA());

        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, result.Outcome);
        Assert.NotNull(result.Zone);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_CloseEqualToSwingHigh_NoBreakOfStructure()
    {
        // Swing high 102.0, leg close 102.0: not above.
        ZoneDetection result = DetectWithSwings(ZoneScenarios.ScenarioA(swingHigh: 102.0));

        Assert.Equal(ZoneDetectionOutcome.NoBreakOfStructure, result.Outcome);
        Assert.Null(result.Zone);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_CloseOneTickAboveSwingHigh_CreatesZone()
    {
        // Swing high 101.999, leg close 102.0.
        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, DetectWithSwings(ZoneScenarios.ScenarioA(swingHigh: 101.999)).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_CloseOneTickBelowSwingHigh_NoBreakOfStructure()
    {
        // Swing high 102.001, leg close 102.0.
        Assert.Equal(ZoneDetectionOutcome.NoBreakOfStructure, DetectWithSwings(ZoneScenarios.ScenarioA(swingHigh: 102.001)).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_NoSwingHigh_NoBreakOfStructure()
    {
        // A swing "high" of 100.5 equals the filler highs, so no swing exists at all.
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA(swingHigh: 100.5);
        Assert.Empty(SwingDetector.Find(bars, ZoneScenarios.Strength));

        Assert.Equal(ZoneDetectionOutcome.NoBreakOfStructure, DetectWithSwings(bars).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_SwingStillRetained_CreatesZone()
    {
        // 106 bars: F x 6, swing at bar 6, F x 97 (bars 7-103), base B at 104, leg at 105.
        // The detector retains swings from bar 106 - 100 = 6 onward, so bar 6 counts.
        IReadOnlyList<Bar> bars = LongScenario(fillerAfterSwing: 97);
        Assert.Equal(106, bars.Count);

        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, DetectWithSwings(bars).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_SwingOutsideRetainedWindow_NoBreakOfStructure()
    {
        // 107 bars: the oldest retained bar is 107 - 100 = 7, so the swing at bar 6 is gone and
        // no retained swing remains.
        IReadOnlyList<Bar> bars = LongScenario(fillerAfterSwing: 98);
        Assert.Equal(107, bars.Count);
        Assert.Empty(SwingDetector.Find(bars, ZoneScenarios.Strength));

        Assert.Equal(ZoneDetectionOutcome.NoBreakOfStructure, DetectWithSwings(bars).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_RequireBosFalseAndNoSwing_CreatesZone()
    {
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA(swingHigh: 100.5);

        ZoneDetection result = ZoneDetector.Detect(bars, Array.Empty<Swing>(), Defaults with { RequireBos = false });

        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, result.Outcome);
        Assert.NotNull(result.Zone);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_RequireBosFalseAndCloseBelowSwing_CreatesZone()
    {
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA(swingHigh: 105.0);

        ZoneDetection result = ZoneDetector.Detect(bars, SwingDetector.Find(bars, ZoneScenarios.Strength), Defaults with { RequireBos = false });

        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, result.Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_SwingAtLegFirstBar_IsIgnored()
    {
        // Swings given by hand: 101.0 at bar 6 and 105.0 at bar 30, the leg's first bar.
        // Only swings whose bar is before the leg count, so the leg is compared with 101.0.
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA();
        Swing[] swings =
        {
            new(SwingKind.High, ZoneScenarios.OpenOf(6), 101.0),
            new(SwingKind.High, ZoneScenarios.OpenOf(30), 105.0),
        };

        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, ZoneDetector.Detect(bars, swings, Defaults).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_SwingInsideThreeBarLeg_IsIgnored()
    {
        // Three-bar leg 28-30 closing at 103.0; a swing at bar 29 (inside the leg) at 110.0 is not
        // before the leg's first bar, so the leg is compared with 101.0 at bar 6.
        IReadOnlyList<Bar> bars = ThreeBarLegScenario();
        Swing[] swings =
        {
            new(SwingKind.High, ZoneScenarios.OpenOf(6), 101.0),
            new(SwingKind.High, ZoneScenarios.OpenOf(29), 110.0),
        };

        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, ZoneDetector.Detect(bars, swings, Defaults).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_SwingAtBarImmediatelyBeforeLeg_IsTheOneCompared()
    {
        // A swing at bar 29 (just before the leg at bar 30) at 102.5 is the most recent one; the leg
        // closes at 102.0, below it.
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA();
        Swing[] swings =
        {
            new(SwingKind.High, ZoneScenarios.OpenOf(6), 101.0),
            new(SwingKind.High, ZoneScenarios.OpenOf(29), 102.5),
        };

        Assert.Equal(ZoneDetectionOutcome.NoBreakOfStructure, ZoneDetector.Detect(bars, swings, Defaults).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_MostRecentSwingIsLower_UsesTheMostRecent()
    {
        // Bar 20 at 101.0 is more recent than bar 6 at 103.0. The leg (close 102.0) breaks the
        // most recent one. The list order is newest first to show that time decides, not position.
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA();
        Swing[] swings =
        {
            new(SwingKind.High, ZoneScenarios.OpenOf(20), 101.0),
            new(SwingKind.High, ZoneScenarios.OpenOf(6), 103.0),
        };

        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, ZoneDetector.Detect(bars, swings, Defaults).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_MostRecentSwingIsHigher_NoBreakOfStructure()
    {
        // Bar 20 at 103.0 is the most recent; the older 101.0 at bar 6 does not count.
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA();
        Swing[] swings =
        {
            new(SwingKind.High, ZoneScenarios.OpenOf(6), 101.0),
            new(SwingKind.High, ZoneScenarios.OpenOf(20), 103.0),
        };

        Assert.Equal(ZoneDetectionOutcome.NoBreakOfStructure, ZoneDetector.Detect(bars, swings, Defaults).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_OnlySwingLowsForDemand_NoBreakOfStructure()
    {
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA();
        Swing[] swings = { new(SwingKind.Low, ZoneScenarios.OpenOf(20), 99.0) };

        Assert.Equal(ZoneDetectionOutcome.NoBreakOfStructure, ZoneDetector.Detect(bars, swings, Defaults).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_MirroredCloseBelowSwingLow_CreatesSupplyZone()
    {
        // Mirror of scenario A: leg close 98.0 < swing low 99.0.
        ZoneDetection result = DetectWithSwings(ZoneScenarios.ScenarioA().Mirror(200.0));

        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, result.Outcome);
        Assert.Equal(ZoneType.Supply, result.Zone!.Type);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_MirroredCloseEqualToSwingLow_NoBreakOfStructure()
    {
        // Mirror of swing high 102.0: swing low 98.0, leg close 98.0.
        Assert.Equal(ZoneDetectionOutcome.NoBreakOfStructure, DetectWithSwings(ZoneScenarios.ScenarioA(swingHigh: 102.0).Mirror(200.0)).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_MirroredCloseOneTickBelowSwingLow_CreatesSupplyZone()
    {
        // Mirror of swing high 101.999: swing low 98.001, leg close 98.0.
        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, DetectWithSwings(ZoneScenarios.ScenarioA(swingHigh: 101.999).Mirror(200.0)).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_OnlySwingHighsForSupply_NoBreakOfStructure()
    {
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA().Mirror(200.0);
        Swing[] swings = { new(SwingKind.High, ZoneScenarios.OpenOf(20), 101.0) };

        Assert.Equal(ZoneDetectionOutcome.NoBreakOfStructure, ZoneDetector.Detect(bars, swings, Defaults).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-008")]
    public void Detect_NoImpulseLeg_ReportsNoImpulseLeg()
    {
        // Leg close 101.999: net 1.999 < 2.0, so detection stops before the break of structure.
        ZoneDetection result = DetectWithSwings(ZoneScenarios.ScenarioA(legClose: 101.999));

        Assert.Equal(ZoneDetectionOutcome.NoImpulseLeg, result.Outcome);
        Assert.Null(result.Zone);
    }

    // ------------------------------------------------------------------ SIG-009 base

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_AllNarrowBars_TakesThreeBaseBars()
    {
        // Scenario A, ATR 1.0. Bar 29 is always taken; bars 28 and 27 are fillers with range 1.0,
        // exactly 1.0 x ATR, so they are taken too; three bars is the maximum.
        // The first base bar is bar 27, opening 2026-01-09 12:00.
        Zone zone = CreatedZone(ZoneScenarios.ScenarioA());

        Assert.Equal("D-20260109-1200", zone.Id);
    }

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_MaxBaseBarsTwo_TakesTwoBaseBars()
    {
        // Same bars, MaxBaseBars = 2: base is bars 28-29; bar 28 opens 2026-01-09 16:00.
        Zone zone = CreatedZone(ZoneScenarios.ScenarioA(), Defaults with { MaxBaseBars = 2 });

        Assert.Equal("D-20260109-1600", zone.Id);
    }

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_WideBarTwoBack_StopsAtTwoBaseBars()
    {
        // Bars 7-26 F, 27 W (range 2.4), 28 F, 29 B, 30 bullish 100.0 -> 102.2.
        // ATR as of 29 = (11 x 1.0 + 2.4 + 1.0 + 1.0) / 14 = 15.4 / 14 = 1.1; leg net 2.2 = 2.2.
        // Base: 29, then 28 (range 1.0 <= 1.1), then 27 is wider (2.4 > 1.1): stop. Base 28-29.
        // Distal = min(99.5, 99.5, leg low 100.0) = 99.5; W's low 98.8 is not used.
        // Proximal = max(100.0, 100.2) = 100.2.
        IReadOnlyList<Bar> bars = ZoneScenarios.Prefix(101.0, 20)
            .Ohlc(100.0, 101.2, 98.8, 100.0)
            .Filler(1)
            .Ohlc(100.2, 100.5, 99.5, 100.0)
            .Bullish(100.0, 102.2)
            .Build();

        Zone zone = CreatedZone(bars);

        Assert.Equal("D-20260109-1600", zone.Id);
        Assert.Equal(99.5, zone.Distal);
        Assert.Equal(100.2, zone.Proximal);
        Assert.Equal(1.1, zone.AtrAtCreation, 9);
    }

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_WideBarOneBack_StopsAtOneBaseBar()
    {
        // Isolated-base layout: W at 28, B = (100.2, 100.5, 99.5, 100.0) at 29, leg 100.0 -> 102.2.
        // ATR 1.1, base is bar 29 only (opens 2026-01-09 20:00).
        Zone zone = CreatedZone(ZoneScenarios.IsolatedBase(
            ZoneScenarios.Prices(100.2, 100.5, 99.5, 100.0),
            ZoneScenarios.Prices(100.0, 102.2, 100.0, 102.2)));

        Assert.Equal("D-20260109-2000", zone.Id);
        Assert.Equal(99.5, zone.Distal);
        Assert.Equal(100.2, zone.Proximal);
    }

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_FirstBarBackIsWide_TakesItAndStops()
    {
        // Bars 7-28 F, 29 Wb = (100.2, 101.2, 98.8, 100.0) with range 2.4, leg 100.0 -> 102.2.
        // ATR as of 29 = (13 x 1.0 + 2.4) / 14 = 1.1; leg net 2.2 = 2.2.
        // Bar 29 is wider than 1.1 but is always taken. The backward collection stops there, so the
        // narrow fillers 28 and 27 are not taken: the id comes from bar 29 (2026-01-09 20:00).
        // Distal = min(98.8, leg low 100.0) = 98.8, proximal = 100.2, height 1.4 <= 1.65.
        IReadOnlyList<Bar> bars = ZoneScenarios.Prefix()
            .Ohlc(100.2, 101.2, 98.8, 100.0)
            .Bullish(100.0, 102.2)
            .Build();

        Zone zone = CreatedZone(bars);

        Assert.Equal("D-20260109-2000", zone.Id);
        Assert.Equal(98.8, zone.Distal);
        Assert.Equal(100.2, zone.Proximal);
    }

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_BarRangeExactlyAtAtr_IsTaken()
    {
        // Bars 7-26 F, 27 W3 = (100.0, 101.8, 98.2, 100.0) range 3.6, 28 T = (100.0, 100.6, 99.4, 100.0)
        // range 1.2, 29 B, 30 bullish 100.0 -> 102.5.
        // ATR as of 29 = (11 x 1.0 + 3.6 + 1.2 + 1.0) / 14 = 16.8 / 14 = 1.2; leg net 2.5 >= 2.4.
        // T's range 1.2 equals 1.0 x ATR: taken. W3 (3.6) stops the collection. Base 28-29.
        // Distal = min(99.4, 99.5, 100.0) = 99.4, proximal = max(100.0, 100.2) = 100.2.
        Zone zone = CreatedZone(BaseBoundaryScenario(100.6, 99.4));

        Assert.Equal("D-20260109-1600", zone.Id);
        Assert.Equal(99.4, zone.Distal);
        Assert.Equal(100.2, zone.Proximal);
        Assert.Equal(1.2, zone.AtrAtCreation, 9);
    }

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_BarRangeOneTickAboveTheInclusionBoundary_IsNotTaken()
    {
        // T = (100.0, 100.6005, 99.3995, 100.0), range 1.201 (one tick wider than above).
        // ATR as of 29 = (11 + 3.6 + 1.201 + 1.0) / 14 = 16.801 / 14 = 1.200071; 1.201 > 1.200071,
        // so T is too wide. Base is bar 29 only (2026-01-09 20:00).
        // Leg net 2.5 >= 2.400143. Distal = min(99.5, 100.0) = 99.5.
        Zone zone = CreatedZone(BaseBoundaryScenario(100.6005, 99.3995));

        Assert.Equal("D-20260109-2000", zone.Id);
        Assert.Equal(99.5, zone.Distal);
        Assert.Equal(1.200071429, zone.AtrAtCreation, 9);
    }

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_BarRangeOneTickBelowTheInclusionBoundary_IsTaken()
    {
        // T = (100.0, 100.5995, 99.4005, 100.0), range 1.199.
        // ATR = (11 + 3.6 + 1.199 + 1.0) / 14 = 16.799 / 14 = 1.199929; 1.199 <= 1.199929: taken.
        Zone zone = CreatedZone(BaseBoundaryScenario(100.5995, 99.4005));

        Assert.Equal("D-20260109-1600", zone.Id);
        Assert.Equal(99.4005, zone.Distal);
    }

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_BaseReachesStartOfBuffer_StopsThere()
    {
        // AtrPeriod 1 and no break-of-structure check. Bars: 0 F, 1 B, 2 bullish 100.0 -> 102.0.
        // ATR(1) as of bar 1 = true range of bar 1 = 1.0; net 2.0 >= 2.0.
        // Base: bar 1, then bar 0 (range 1.0 <= 1.0); there is no bar before 0. Id from bar 0.
        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.H4)
            .At(ZoneScenarios.Start)
            .Filler(1)
            .Ohlc(100.2, 100.5, 99.5, 100.0)
            .Bullish(100.0, 102.0)
            .Build();

        Zone zone = CreatedZone(bars, Defaults with { AtrPeriod = 1, RequireBos = false });

        Assert.Equal("D-20260105-0000", zone.Id);
        Assert.Equal(99.5, zone.Distal);
        Assert.Equal(100.2, zone.Proximal);
    }

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_MirroredFirstBarBackIsWide_TakesItAndStops()
    {
        // Mirror of the wide-first-bar case: supply, base is bar 29 only.
        // Distal = max(high of bar 29 = 200 - 98.8 = 101.2, leg high 100.0) = 101.2;
        // proximal = min body bottom of bar 29 = min(99.8, 100.0) = 99.8.
        IReadOnlyList<Bar> bars = ZoneScenarios.Prefix()
            .Ohlc(100.2, 101.2, 98.8, 100.0)
            .Bullish(100.0, 102.2)
            .Build()
            .Mirror(200.0);

        Zone zone = CreatedZone(bars);

        Assert.Equal("S-20260109-2000", zone.Id);
        Assert.Equal(101.2, zone.Distal, 9);
        Assert.Equal(99.8, zone.Proximal, 9);
    }

    [Fact]
    [Trait("Req", "SIG-009")]
    public void Detect_MirroredWideBarTwoBack_StopsAtTwoBaseBars()
    {
        IReadOnlyList<Bar> bars = ZoneScenarios.Prefix(101.0, 20)
            .Ohlc(100.0, 101.2, 98.8, 100.0)
            .Filler(1)
            .Ohlc(100.2, 100.5, 99.5, 100.0)
            .Bullish(100.0, 102.2)
            .Build()
            .Mirror(200.0);

        Zone zone = CreatedZone(bars);

        Assert.Equal("S-20260109-1600", zone.Id);
        Assert.Equal(100.5, zone.Distal, 9);
    }

    // ------------------------------------------------------------------ SIG-010 edges

    [Fact]
    [Trait("Req", "SIG-010")]
    public void Detect_DemandWorkedExample_EdgesFromBaseBodiesAndLows()
    {
        // Scenario A with bar 28 replaced by a bullish bar (99.6, 100.5, 99.5, 100.3): range 1.0,
        // true range 1.0, so ATR stays 1.0. Base bars 27-29:
        //   27 F  (100.0, 100.5, 99.5, 100.0): body top 100.0, low 99.5
        //   28    (99.6, 100.5, 99.5, 100.3):  body top 100.3, low 99.5
        //   29 B  (100.2, 100.5, 99.5, 100.0): body top 100.2, low 99.5
        // First leg bar (100.0, 102.0, 100.0, 102.0): low 100.0.
        // Proximal = highest body top = 100.3 (not the highs of 100.5).
        // Distal = lowest low of base and first leg bar = 99.5.
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA().ReplaceAt(28, 99.6, 100.5, 99.5, 100.3);

        Zone zone = CreatedZone(bars);

        Assert.Equal(ZoneType.Demand, zone.Type);
        Assert.Equal(100.3, zone.Proximal);
        Assert.Equal(99.5, zone.Distal);
    }

    [Fact]
    [Trait("Req", "SIG-010")]
    public void Detect_DemandFirstLegBarLowestLow_IsTheDistal()
    {
        // Scenario A with leg (100.0, 102.0, 99.3, 102.0): net 2.0, lower wick to 99.3.
        // Distal = min(99.5, 99.5, 99.5, 99.3) = 99.3; proximal = 100.2; height 0.9.
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA().ReplaceAt(30, 100.0, 102.0, 99.3, 102.0);

        Zone zone = CreatedZone(bars);

        Assert.Equal(99.3, zone.Distal);
        Assert.Equal(100.2, zone.Proximal);
    }

    [Fact]
    [Trait("Req", "SIG-010")]
    public void Detect_SupplyWorkedExample_EdgesFromBaseBodiesAndHighs()
    {
        // Mirror of the demand worked example. Base bars 27-29 become
        //   27 (100.0, 100.5, 99.5, 100.0): body bottom 100.0, high 100.5
        //   28 (100.4, 100.5, 99.5, 99.7):  body bottom 99.7,  high 100.5
        //   29 (99.8, 100.5, 99.5, 100.0):  body bottom 99.8,  high 100.5
        // First leg bar (100.0, 100.0, 98.0, 98.0): high 100.0.
        // Proximal = lowest body bottom = 99.7; distal = highest high = 100.5.
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA().ReplaceAt(28, 99.6, 100.5, 99.5, 100.3).Mirror(200.0);

        Zone zone = CreatedZone(bars);

        Assert.Equal(ZoneType.Supply, zone.Type);
        Assert.Equal(99.7, zone.Proximal, 9);
        Assert.Equal(100.5, zone.Distal, 9);
    }

    [Fact]
    [Trait("Req", "SIG-010")]
    public void Detect_SupplyFirstLegBarHighestHigh_IsTheDistal()
    {
        // Mirror of the lower-wick case: leg high 200 - 99.3 = 100.7 is the distal.
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA().ReplaceAt(30, 100.0, 102.0, 99.3, 102.0).Mirror(200.0);

        Zone zone = CreatedZone(bars);

        Assert.Equal(100.7, zone.Distal, 9);
        Assert.Equal(99.8, zone.Proximal, 9);
    }

    [Fact]
    [Trait("Req", "SIG-010")]
    public void Detect_ProximalEqualToDistal_DiscardsDegenerateZone()
    {
        // Isolated-base layout with base bar 29 = doji (100.0, 100.0, 100.0, 100.0), true range 0.
        // ATR as of 29 = (12 x 1.0 + 2.4 + 0.0) / 14 = 14.4 / 14 = 1.028571; leg 100.0 -> 102.1,
        // net 2.1 >= 2.057143. Base is bar 29 only (bar 28 has range 2.4 > 1.028571).
        // Proximal = 100.0 and distal = min(100.0, leg low 100.0) = 100.0: not strictly on the
        // trade side, so the zone is discarded.
        IReadOnlyList<Bar> bars = ZoneScenarios.IsolatedBase(
            ZoneScenarios.Prices(100.0, 100.0, 100.0, 100.0),
            ZoneScenarios.Prices(100.0, 102.1, 100.0, 102.1));

        ZoneDetection result = DetectWithSwings(bars);

        Assert.Equal(ZoneDetectionOutcome.DegenerateZone, result.Outcome);
        Assert.Null(result.Zone);
    }

    [Fact]
    [Trait("Req", "SIG-010")]
    public void Detect_MirroredProximalEqualToDistal_DiscardsDegenerateZone()
    {
        IReadOnlyList<Bar> bars = ZoneScenarios.IsolatedBase(
            ZoneScenarios.Prices(100.0, 100.0, 100.0, 100.0),
            ZoneScenarios.Prices(100.0, 102.1, 100.0, 102.1)).Mirror(200.0);

        Assert.Equal(ZoneDetectionOutcome.DegenerateZone, DetectWithSwings(bars).Outcome);
    }

    // ------------------------------------------------------------------ SIG-011 width

    [Theory]
    [Trait("Req", "SIG-011")]
    [InlineData(100.98, 99.98, ZoneDetectionOutcome.ZoneCreated)] // height 100.2 - 99.98 = 0.220 = 0.2 x 1.1
    [InlineData(100.979, 99.979, ZoneDetectionOutcome.ZoneCreated)] // height 0.221
    [InlineData(100.981, 99.981, ZoneDetectionOutcome.WidthOutOfRange)] // height 0.219 < 0.22
    public void Detect_HeightAroundMinimum_KeptOnlyFromMinimum(double baseHigh, double baseLow, ZoneDetectionOutcome expected)
    {
        // Isolated-base layout, ATR 1.1 (base bar range 1.0, true range 1.0 from a previous close
        // of 100.0). Base bar (100.2, baseHigh, baseLow, 100.0) is bearish with body top 100.2.
        // Leg 100.0 -> 102.2 (net 2.2 = 2.2), low 100.0 >= baseLow, so distal = baseLow.
        IReadOnlyList<Bar> bars = ZoneScenarios.IsolatedBase(
            ZoneScenarios.Prices(100.2, baseHigh, baseLow, 100.0),
            ZoneScenarios.Prices(100.0, 102.2, 100.0, 102.2));

        Assert.Equal(expected, DetectWithSwings(bars).Outcome);
    }

    [Theory]
    [Trait("Req", "SIG-011")]
    [InlineData(98.55, ZoneDetectionOutcome.ZoneCreated)] // height 100.2 - 98.55 = 1.650 = 1.5 x 1.1
    [InlineData(98.551, ZoneDetectionOutcome.ZoneCreated)] // height 1.649
    [InlineData(98.549, ZoneDetectionOutcome.WidthOutOfRange)] // height 1.651 > 1.65
    public void Detect_HeightAroundMaximum_KeptOnlyUpToMaximum(double legLow, ZoneDetectionOutcome expected)
    {
        // Isolated-base layout, ATR 1.1. Base B = (100.2, 100.5, 99.5, 100.0), proximal 100.2.
        // Leg (100.0, 102.2, legLow, 102.2): net 2.2; its lower wick makes legLow the distal.
        IReadOnlyList<Bar> bars = ZoneScenarios.IsolatedBase(
            ZoneScenarios.Prices(100.2, 100.5, 99.5, 100.0),
            ZoneScenarios.Prices(100.0, 102.2, legLow, 102.2));

        Assert.Equal(expected, DetectWithSwings(bars).Outcome);
    }

    [Theory]
    [Trait("Req", "SIG-011")]
    [InlineData(100.98, 99.98, ZoneDetectionOutcome.ZoneCreated)]
    [InlineData(100.981, 99.981, ZoneDetectionOutcome.WidthOutOfRange)]
    public void Detect_MirroredHeightAroundMinimum_KeptOnlyFromMinimum(double baseHigh, double baseLow, ZoneDetectionOutcome expected)
    {
        // Mirror: supply, proximal 99.8, distal 200 - baseLow; heights 0.220 and 0.219.
        IReadOnlyList<Bar> bars = ZoneScenarios.IsolatedBase(
            ZoneScenarios.Prices(100.2, baseHigh, baseLow, 100.0),
            ZoneScenarios.Prices(100.0, 102.2, 100.0, 102.2)).Mirror(200.0);

        Assert.Equal(expected, DetectWithSwings(bars).Outcome);
    }

    [Theory]
    [Trait("Req", "SIG-011")]
    [InlineData(98.55, ZoneDetectionOutcome.ZoneCreated)]
    [InlineData(98.549, ZoneDetectionOutcome.WidthOutOfRange)]
    public void Detect_MirroredHeightAroundMaximum_KeptOnlyUpToMaximum(double legLow, ZoneDetectionOutcome expected)
    {
        // Mirror: supply, proximal 99.8, distal 200 - legLow; heights 1.650 and 1.651.
        IReadOnlyList<Bar> bars = ZoneScenarios.IsolatedBase(
            ZoneScenarios.Prices(100.2, 100.5, 99.5, 100.0),
            ZoneScenarios.Prices(100.0, 102.2, legLow, 102.2)).Mirror(200.0);

        Assert.Equal(expected, DetectWithSwings(bars).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-011")]
    public void Detect_WidthUsesTheLegAtr_NotTheAtrIncludingTheLeg()
    {
        // Isolated-base layout with leg low 98.549: height 100.2 - 98.549 = 1.651.
        // With the leg's ATR (as of bar 29) = 1.1 the limit is 1.65: rejected.
        // ATR as of the leg bar 30 would be (11 x 1.0 + 2.4 + 1.0 + 3.651) / 14 = 18.051 / 14 =
        // 1.289357 (the leg's true range is 102.2 - 98.549 = 3.651), whose limit 1.934 would keep it.
        IReadOnlyList<Bar> bars = ZoneScenarios.IsolatedBase(
            ZoneScenarios.Prices(100.2, 100.5, 99.5, 100.0),
            ZoneScenarios.Prices(100.0, 102.2, 98.549, 102.2));

        Assert.Equal(ZoneDetectionOutcome.WidthOutOfRange, DetectWithSwings(bars).Outcome);
    }

    [Fact]
    [Trait("Req", "SIG-011")]
    public void Detect_ZoneAtMaximumWidth_StoresTheLegAtr()
    {
        // Height exactly 1.65 = 1.5 x 1.1: kept, with AtrAtCreation = 1.1 (ATR as of bar 29).
        IReadOnlyList<Bar> bars = ZoneScenarios.IsolatedBase(
            ZoneScenarios.Prices(100.2, 100.5, 99.5, 100.0),
            ZoneScenarios.Prices(100.0, 102.2, 98.55, 102.2));

        Zone zone = CreatedZone(bars);

        Assert.Equal(1.1, zone.AtrAtCreation, 9);
        Assert.Equal(98.55, zone.Distal);
        Assert.Equal(100.2, zone.Proximal);
    }

    [Fact]
    [Trait("Req", "SIG-011")]
    public void Detect_CustomWidthLimits_AreApplied()
    {
        // Scenario A height 0.7 with ATR 1.0: MaxZoneAtr 0.69 rejects it, MinZoneAtr 0.71 rejects it,
        // limits 0.7 / 0.7 keep it.
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA();

        Assert.Equal(ZoneDetectionOutcome.WidthOutOfRange, Detect(bars, Defaults with { MaxZoneAtr = 0.69 }).Outcome);
        Assert.Equal(ZoneDetectionOutcome.WidthOutOfRange, Detect(bars, Defaults with { MinZoneAtr = 0.71, MaxZoneAtr = 1.5 }).Outcome);
        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, Detect(bars, Defaults with { MinZoneAtr = 0.7, MaxZoneAtr = 0.7 }).Outcome);
    }

    // ------------------------------------------------------------------ SIG-013 activation and id

    [Fact]
    [Trait("Req", "SIG-013")]
    [Trait("Req", "SIG-010")]
    public void Detect_ScenarioA_CreatesTheExpectedZone()
    {
        // Leg bar 30 opens 2026-01-10 00:00, so the next H4 bar opens 2026-01-10 04:00.
        // First base bar 27 opens 2026-01-09 12:00. Proximal 100.2, distal 99.5, ATR 1.0.
        Zone zone = CreatedZone(ZoneScenarios.ScenarioA());

        Assert.Equal("D-20260109-1200", zone.Id);
        Assert.Equal(ZoneType.Demand, zone.Type);
        Assert.Equal(100.2, zone.Proximal);
        Assert.Equal(99.5, zone.Distal);
        Assert.Equal(new DateTime(2026, 1, 10, 4, 0, 0, DateTimeKind.Utc), zone.ActivationTimeUtc);
        Assert.Equal(DateTimeKind.Utc, zone.ActivationTimeUtc.Kind);
        Assert.Equal(1.0, zone.AtrAtCreation, 9);
        Assert.Equal(ZoneState.Active, zone.State);
        Assert.Null(zone.TouchBarOpenUtc);
        Assert.Null(zone.ExtremeSinceTouch);
    }

    [Fact]
    [Trait("Req", "SIG-013")]
    [Trait("Req", "SIG-010")]
    public void Detect_MirroredScenarioA_CreatesTheExpectedSupplyZone()
    {
        // Mirror: base bodies bottoms 100.0, 100.0, 99.8 -> proximal 99.8; highs 100.5 -> distal 100.5.
        Zone zone = CreatedZone(ZoneScenarios.ScenarioA().Mirror(200.0));

        Assert.Equal("S-20260109-1200", zone.Id);
        Assert.Equal(ZoneType.Supply, zone.Type);
        Assert.Equal(99.8, zone.Proximal, 9);
        Assert.Equal(100.5, zone.Distal, 9);
        Assert.Equal(new DateTime(2026, 1, 10, 4, 0, 0, DateTimeKind.Utc), zone.ActivationTimeUtc);
        Assert.Equal(ZoneState.Active, zone.State);
    }

    [Fact]
    [Trait("Req", "SIG-013")]
    public void Detect_ThreeBarLeg_ActivatesAfterTheLastLegBar()
    {
        // Leg 28-30; the last leg bar 30 opens 2026-01-10 00:00, so activation is 04:00 the same day,
        // not four hours after the first leg bar. Base 25-27 (fillers), id from bar 25 = 2026-01-09 04:00.
        Zone zone = CreatedZone(ThreeBarLegScenario());

        Assert.Equal(new DateTime(2026, 1, 10, 4, 0, 0, DateTimeKind.Utc), zone.ActivationTimeUtc);
        Assert.Equal("D-20260109-0400", zone.Id);
    }

    [Fact]
    [Trait("Req", "SIG-013")]
    public void Detect_SingleDigitDateParts_IdIsZeroPadded()
    {
        // Start 2026-02-03 20:00: the first base bar 27 opens 108 hours later, 2026-02-08 08:00.
        // The leg bar 30 opens 120 hours after the start, 2026-02-08 20:00; activation is
        // four hours later, 2026-02-09 00:00.
        DateTime start = new(2026, 2, 3, 20, 0, 0, DateTimeKind.Utc);

        Zone zone = CreatedZone(ZoneScenarios.ScenarioA(start: start));

        Assert.Equal("D-20260208-0800", zone.Id);
        Assert.Equal(new DateTime(2026, 2, 9, 0, 0, 0, DateTimeKind.Utc), zone.ActivationTimeUtc);
    }

    [Fact]
    [Trait("Req", "SIG-013")]
    public void Detect_UnderThaiCulture_IdUsesInvariantGregorianFormat()
    {
        // The Thai culture uses the Buddhist calendar (2026 would format as 2569) and the id must
        // not depend on the current culture.
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            Zone zone = CreatedZone(ZoneScenarios.ScenarioA());

            Assert.Equal("D-20260109-1200", zone.Id);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Req", "SIG-013")]
    public void Detect_IdenticalHistoryTwice_GivesIdenticalZones()
    {
        Zone first = CreatedZone(ZoneScenarios.ScenarioA());
        Zone second = CreatedZone(ZoneScenarios.ScenarioA());

        Assert.Equal(first, second);
        Assert.Equal("D-20260109-1200", second.Id);
    }

    [Fact]
    [Trait("Req", "SIG-013")]
    public void Detect_DifferentOlderHistory_GivesTheSameZone()
    {
        // The same last 31 bars preceded by ten extra, different bars give the same zone: nothing
        // outside the leg, base, ATR and swing windows matters.
        IReadOnlyList<Bar> recent = ZoneScenarios.ScenarioA();
        var longer = new List<Bar>();
        DateTime earlier = ZoneScenarios.Start.AddHours(-40.0);
        longer.AddRange(new BarBuilder(TimeFrame.H4).At(earlier).Repeat(10, (b, i) => b.Ohlc(90.0 + i, 91.0 + i, 89.0 + i, 90.5 + i)).Build());
        longer.AddRange(recent);

        Assert.Equal(CreatedZone(recent), CreatedZone(longer));
    }

    [Fact]
    [Trait("Req", "SIG-013")]
    public void Detect_LegClosingBeforeWeekend_ActivatesFourHoursAfterTheLegBarOpen()
    {
        // Start Sunday 2026-01-04 20:00, so the leg bar 30 opens Friday 2026-01-09 20:00. The zone
        // activates at the leg bar's close time, Saturday 2026-01-10 00:00 (open time plus four
        // hours). See "Decisions for the human" in the pull request: across a market gap the next
        // H4 bar opens later, and that open time is not known when the zone is detected.
        DateTime start = new(2026, 1, 4, 20, 0, 0, DateTimeKind.Utc);

        Zone zone = CreatedZone(ZoneScenarios.ScenarioA(start: start));

        Assert.Equal(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), zone.ActivationTimeUtc);
        Assert.Equal("D-20260109-0800", zone.Id);
    }

    // ------------------------------------------------------------------ argument checks

    [Fact]
    public void Detect_NullArguments_Throw()
    {
        IReadOnlyList<Bar> bars = ZoneScenarios.ScenarioA();

        Assert.Throws<ArgumentNullException>("h4Bars", () => ZoneDetector.Detect(null!, Array.Empty<Swing>(), Defaults));
        Assert.Throws<ArgumentNullException>("swings", () => ZoneDetector.Detect(bars, null!, Defaults));
        Assert.Throws<ArgumentNullException>("parameters", () => ZoneDetector.Detect(bars, Array.Empty<Swing>(), null!));
    }

    [Fact]
    public void Detect_InvalidParameters_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>("MaxBaseBars", () => ZoneDetector.Detect(ZoneScenarios.ScenarioA(), Array.Empty<Swing>(), Defaults with { MaxBaseBars = 0 }));
    }

    [Fact]
    public void Detect_EmptyBuffer_NoImpulseLeg()
    {
        Assert.Equal(ZoneDetectionOutcome.NoImpulseLeg, ZoneDetector.Detect(Array.Empty<Bar>(), Array.Empty<Swing>(), Defaults).Outcome);
    }

    // ------------------------------------------------------------------ helpers

    private static ZoneDetection DetectWithSwings(IReadOnlyList<Bar> bars)
    {
        return Detect(bars, Defaults);
    }

    private static ZoneDetection Detect(IReadOnlyList<Bar> bars, SignalParameters parameters)
    {
        return ZoneDetector.Detect(bars, SwingDetector.Find(bars, parameters.SwingStrength), parameters);
    }

    private static Zone CreatedZone(IReadOnlyList<Bar> bars)
    {
        return CreatedZone(bars, Defaults);
    }

    private static Zone CreatedZone(IReadOnlyList<Bar> bars, SignalParameters parameters)
    {
        ZoneDetection result = Detect(bars, parameters);
        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, result.Outcome);
        Assert.NotNull(result.Zone);
        return result.Zone!;
    }

    private static IReadOnlyList<Bar> LongScenario(int fillerAfterSwing)
    {
        return ZoneScenarios.Prefix(101.0, fillerAfterSwing)
            .Ohlc(100.2, 100.5, 99.5, 100.0)
            .Bullish(100.0, 102.0)
            .Build();
    }

    private static IReadOnlyList<Bar> ThreeBarLegScenario()
    {
        // Bars 7-27 F, then bullish 28 (100.0 -> 100.4), 29 (100.4 -> 100.8), 30 (100.8 -> 103.0).
        // Leg 28-30 with ATR as of 27 = 1.0. Base 25-27: distal 99.5, proximal 100.0 (height 0.5).
        return ZoneScenarios.Prefix(101.0, 21)
            .Bullish(100.0, 100.4)
            .Bullish(100.4, 100.8)
            .Bullish(100.8, 103.0)
            .Build();
    }

    private static IReadOnlyList<Bar> BaseBoundaryScenario(double testBarHigh, double testBarLow)
    {
        // Bars 7-26 F, 27 W3 (100.0, 101.8, 98.2, 100.0), 28 T (100.0, testBarHigh, testBarLow, 100.0),
        // 29 B (100.2, 100.5, 99.5, 100.0), 30 bullish 100.0 -> 102.5.
        return ZoneScenarios.Prefix(101.0, 20)
            .Ohlc(100.0, 101.8, 98.2, 100.0)
            .Ohlc(100.0, testBarHigh, testBarLow, 100.0)
            .Ohlc(100.2, 100.5, 99.5, 100.0)
            .Bullish(100.0, 102.5)
            .Build();
    }
}
