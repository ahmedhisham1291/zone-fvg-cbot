using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The rules of <see cref="ChochDetector"/> on bar lists (SIG-037, SIG-038, SIG-039), the zone book's
/// window of the Choch model (SIG-036), and the zone count of the Choch model's window end in the
/// daily summary, which shares the slot of NoFvg (SIG-037; orchestrating session's answer of 2026-11-23).
/// </summary>
public class ChochDetectorTests
{
    private static IReadOnlyList<Bar> M15((double Open, double High, double Low, double Close)[] tail, int flatBefore = 20)
    {
        return EngineScenarios.M15(flatBefore, tail);
    }

    [Fact]
    [Trait("Req", "SIG-037")]
    public void BrokenSwing_TailA_AtM11TheSwingHighOfM3_EarlierNothing()
    {
        IReadOnlyList<Bar> bars = M15(ChochScenarios.TailA);

        Swing? level = ChochDetector.BrokenSwing(bars, ZoneType.Demand, 3);

        Assert.NotNull(level);
        Assert.Equal(new Swing(SwingKind.High, EngineScenarios.TailOpen(3), 101.0), level);
        for (int k = 0; k < 11; k++)
        {
            Assert.Null(ChochDetector.BrokenSwing(bars.FirstBars(20 + k + 1), ZoneType.Demand, 3));
        }

        Assert.Null(ChochDetector.BrokenSwing(bars, ZoneType.Supply, 3));
    }

    [Fact]
    [Trait("Req", "SIG-037")]
    public void BrokenSwing_MirroredTailA_TheSwingLowAt99()
    {
        IReadOnlyList<Bar> bars = M15(ChochScenarios.TailA).Mirror(EngineScenarios.Pivot);

        Swing? level = ChochDetector.BrokenSwing(bars, ZoneType.Supply, 3);

        Assert.NotNull(level);
        Assert.Equal(SwingKind.Low, level!.Kind);
        Assert.Equal(99.0, level.Price, 9);
        Assert.Null(ChochDetector.BrokenSwing(bars, ZoneType.Demand, 3));
    }

    [Fact]
    [Trait("Req", "SIG-037")]
    public void BrokenSwing_NoBarsOrNoSwing_Null()
    {
        Assert.Null(ChochDetector.BrokenSwing(Array.Empty<Bar>(), ZoneType.Demand, 3));
        Assert.Null(ChochDetector.BrokenSwing(M15(Array.Empty<(double, double, double, double)>()), ZoneType.Demand, 3));
    }

    [Fact]
    [Trait("Req", "SIG-037")]
    public void MostRecentSwing_OnlyTheLookbackEndingAtTheBarIsRead()
    {
        // The swing high of m3 is found from m6 on; 100 bars after m3 it has left the lookback.
        (double, double, double, double)[] tail = ChochScenarios.TailA.Take(7).Concat(Enumerable.Repeat((100.3, 100.35, 100.25, 100.3), 120)).ToArray();
        IReadOnlyList<Bar> bars = M15(tail);
        int m3 = 20 + 3;

        Assert.Null(ChochDetector.MostRecentSwing(bars, m3 + 2, 3, SwingKind.High));
        Assert.Equal(101.0, ChochDetector.MostRecentSwing(bars, m3 + 3, 3, SwingKind.High)!.Price);
        Assert.Equal(101.0, ChochDetector.MostRecentSwing(bars, m3 + 99, 3, SwingKind.High)!.Price);
        Assert.Null(ChochDetector.MostRecentSwing(bars, m3 + 100, 3, SwingKind.High));

        // The answer does not depend on older bars: the same bars without the first 15 give the same swings.
        IReadOnlyList<Bar> shorter = bars.Skip(15).ToList();
        Assert.Equal(ChochDetector.MostRecentSwing(bars, m3 + 50, 3, SwingKind.High), ChochDetector.MostRecentSwing(shorter, m3 + 35, 3, SwingKind.High));
    }

    [Fact]
    [Trait("Req", "SIG-037")]
    public void MostRecentSwing_EndIndexOutsideTheBars_Throws()
    {
        IReadOnlyList<Bar> bars = M15(ChochScenarios.TailA);

        Assert.Throws<ArgumentOutOfRangeException>(() => ChochDetector.MostRecentSwing(bars, bars.Count, 3, SwingKind.High));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChochDetector.MostRecentSwing(bars, -1, 3, SwingKind.High));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChochDetector.MostRecentSwing(bars, 5, 0, SwingKind.High));
    }

    [Fact]
    [Trait("Req", "SIG-038")]
    public void Swept_TailAAndTailB()
    {
        Assert.False(ChochDetector.Swept(M15(ChochScenarios.TailA), ZoneType.Demand, EngineScenarios.TailOpen(7), 3));
        Assert.True(ChochDetector.Swept(M15(ChochScenarios.TailB), ZoneType.Demand, EngineScenarios.TailOpen(7), 3));

        // Up to m11 of tail B no bar has swept yet; m12 sweeps.
        Assert.False(ChochDetector.Swept(M15(ChochScenarios.TailB).FirstBars(20 + 12), ZoneType.Demand, EngineScenarios.TailOpen(7), 3));
        Assert.True(ChochDetector.Swept(M15(ChochScenarios.TailB).FirstBars(20 + 13), ZoneType.Demand, EngineScenarios.TailOpen(7), 3));
    }

    [Fact]
    [Trait("Req", "SIG-038")]
    public void Swept_MirroredTailB_HighAboveTheSwingHigh()
    {
        IReadOnlyList<Bar> bars = M15(ChochScenarios.TailB).Mirror(EngineScenarios.Pivot);

        Assert.True(ChochDetector.Swept(bars, ZoneType.Supply, EngineScenarios.TailOpen(7), 3));
        Assert.False(ChochDetector.Swept(M15(ChochScenarios.TailA).Mirror(EngineScenarios.Pivot), ZoneType.Supply, EngineScenarios.TailOpen(7), 3));
    }

    [Fact]
    [Trait("Req", "SIG-038")]
    public void Swept_TouchBarNotAmongTheBars_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChochDetector.Swept(M15(ChochScenarios.TailA), ZoneType.Demand, EngineScenarios.TailOpen(40), 3));
        Assert.Throws<ArgumentException>(() => ChochDetector.Swept(Array.Empty<Bar>(), ZoneType.Demand, EngineScenarios.TailOpen(0), 3));
    }

    [Fact]
    [Trait("Req", "SIG-039")]
    public void Stop_BuyAndSell_WithAndWithoutTheDistal()
    {
        var demand = new Zone("D-1", ZoneType.Demand, 100.2, 99.5, EngineScenarios.Activation, 1.0, ZoneState.Touched, EngineScenarios.Activation, 99.9);
        var supply = new Zone("S-1", ZoneType.Supply, 99.8, 100.5, EngineScenarios.Activation, 1.0, ZoneState.Touched, EngineScenarios.Activation, 100.1);
        var atDistal = new ChochParameters { ChochStopAtDistal = true };
        const double atr = 3.75 / 14.0;

        Assert.Equal(99.9 - (0.1 * atr), ChochDetector.Stop(demand, atr, EngineScenarios.Defaults, ChochScenarios.Defaults), 12);
        Assert.Equal(99.5 - (0.1 * atr), ChochDetector.Stop(demand, atr, EngineScenarios.Defaults, atDistal), 12);
        Assert.Equal(100.1 + (0.1 * atr), ChochDetector.Stop(supply, atr, EngineScenarios.Defaults, ChochScenarios.Defaults), 12);
        Assert.Equal(100.5 + (0.1 * atr), ChochDetector.Stop(supply, atr, EngineScenarios.Defaults, atDistal), 12);

        // With the extreme beyond the distal the extreme stays.
        Assert.Equal(99.4 - (0.1 * atr), ChochDetector.Stop(demand with { ExtremeSinceTouch = 99.4 }, atr, EngineScenarios.Defaults, atDistal), 12);
        Assert.Throws<ArgumentException>(() => ChochDetector.Stop(demand with { ExtremeSinceTouch = null }, atr, EngineScenarios.Defaults, ChochScenarios.Defaults));
    }

    [Fact]
    [Trait("Req", "SIG-036")]
    public void ZoneBook_ChochWindow_EndsAfterItsLastBar_DefaultUnchanged()
    {
        var zone = new Zone("D-1", ZoneType.Demand, 100.2, 99.5, EngineScenarios.Activation, 1.0, ZoneState.Active, null, null);
        var choch = new ZoneBook(EngineScenarios.Defaults, 32);
        var fvg = new ZoneBook(EngineScenarios.Defaults);
        choch.Add(zone);
        fvg.Add(zone);
        IReadOnlyList<Bar> bars = new BarBuilder(TimeFrame.M15).At(EngineScenarios.Activation).Ohlc(100.3, 100.4, 100.1, 100.3).Repeat(40, (b, _) => b.Ohlc(100.3, 100.4, 100.25, 100.3)).Build();

        int chochEnd = -1;
        int fvgEnd = -1;
        for (int i = 0; i < bars.Count; i++)
        {
            choch.OnM15Closed(bars[i]);
            fvg.OnM15Closed(bars[i]);
            chochEnd = chochEnd < 0 && choch.EndConfirmationWindows().Count == 1 ? i : chochEnd;
            fvgEnd = fvgEnd < 0 && fvg.EndConfirmationWindows().Count == 1 ? i : fvgEnd;
        }

        Assert.Equal(32, chochEnd);
        Assert.Equal(16, fvgEnd);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZoneBook(EngineScenarios.Defaults, 0));
        Assert.Throws<ArgumentNullException>(() => new ZoneBook(null!, 32));
        Assert.Throws<ArgumentNullException>(() => new ZoneBook(null!));
    }

    [Fact]
    [Trait("Req", "SIG-037")]
    public void DailyCounts_NoChochCountsInTheSlotOfNoFvg()
    {
        var counts = new ZoneOutcomeCounts();
        DateTime close = new(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        counts.Add(close, new ZoneActivity(0, new[] { new ZoneEnd("D-1", ZoneEndReason.NoChoch), new ZoneEnd("D-2", ZoneEndReason.NoChoch) }));

        (string Key, string? Value)[] line = Assert.Single(counts.TakeDaysBefore(new DateTime(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(
            new (string, string?)[] { ("day", "2026-07-01"), ("created", "0"), ("signalled", "0"), ("no-fvg", "2"), ("bias", "0"), ("invalid", "0"), ("expired", "0"), ("superseded", "0") },
            line);
    }
}
