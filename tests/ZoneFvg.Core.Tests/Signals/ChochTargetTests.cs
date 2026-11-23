using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-040: the target of a Choch signal, before the nearest opposing level (the proximal line of
/// an opposing zone in the book, or an H4 swing that no later H4 bar has closed beyond: answer of
/// the orchestrating session of 2026-11-23), each only when strictly beyond the entry, minus
/// ChochTargetBufferAtr x ATR(H4); otherwise RewardRisk x the stop distance ("default"). Ties
/// (answer of 2026-11-23): a zone wins over a swing, then the lowest zone id, then the oldest swing.
/// </summary>
public class ChochTargetTests
{
    private const int Digits = 9;
    private const double Atr = 15.0 / 14.0;

    // Tail A's buy: entry 101.0, stop 99.9 - 0.1 x 3.75 / 14.
    private const double Entry = 101.0;
    private static readonly double StopA = 99.9 - (0.1 * 3.75 / 14.0);
    private static readonly DateTime H4Start = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

    private static Zone ZoneOf(string id, ZoneType type, double proximal, double distal, ZoneState state = ZoneState.Active)
    {
        return new Zone(id, type, proximal, distal, H4Start, 1.0, state, null, null);
    }

    /// <summary>H4 bars: fillers (100.0, 100.5, 99.5, 100.0) with the given bars at the given positions (index, high or low, close).</summary>
    private static IReadOnlyList<Bar> H4(int count, params (int Index, double Extreme, double Close)[] special)
    {
        var builder = new BarBuilder(TimeFrame.H4).At(H4Start);
        for (int i = 0; i < count; i++)
        {
            (double open, double high, double low, double close) bar = (100.0, 100.5, 99.5, 100.0);
            foreach ((int index, double extreme, double close) in special)
            {
                if (index == i)
                {
                    bar = extreme > 100.0 ? (100.0, Math.Max(extreme, close), 99.5, close) : (100.0, 100.5, Math.Min(extreme, close), close);
                }
            }

            builder.Ohlc(bar.open, bar.high, bar.low, bar.close);
        }

        return builder.Build();
    }

    private static DateTime OpenOf(int index)
    {
        return H4Start.AddHours(4.0 * index);
    }

    private static string Swing(int index)
    {
        return "swing:" + OpenOf(index).ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ChochTarget Buy(IReadOnlyList<Zone> zones, IReadOnlyList<Bar> h4, double stop = double.NaN)
    {
        return ChochDetector.Target(Side.Buy, Entry, double.IsNaN(stop) ? StopA : stop, zones, h4, Atr, EngineScenarios.Defaults, ChochScenarios.Defaults);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void NoOpposingLevel_DefaultTwoR()
    {
        ChochTarget target = Buy(Array.Empty<Zone>(), H4(10));

        Assert.Equal("default", target.Source);
        Assert.Equal(Entry + (2.0 * (Entry - StopA)), target.Price, Digits);
        Assert.Equal(103.25357143, target.Price, 8);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void SupplyZoneAt103_5_TargetBeforeItsProximal()
    {
        Zone supply = ZoneOf("S-20260301-0400", ZoneType.Supply, 103.5, 104.0);

        ChochTarget target = Buy(new[] { supply }, H4(10));

        Assert.Equal("zone:S-20260301-0400", target.Source);
        Assert.Equal(103.5 - (0.1 * Atr), target.Price, Digits);
        Assert.Equal(103.39285714, target.Price, 8);
        Assert.Equal(2.12361, (target.Price - Entry) / (Entry - StopA), 5);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void UnbrokenH4SwingHighAt103_5_TargetBeforeIt()
    {
        ChochTarget target = Buy(Array.Empty<Zone>(), H4(10, (4, 103.5, 100.0)));

        Assert.Equal(Swing(4), target.Source);
        Assert.Equal(103.39285714, target.Price, 8);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void BrokenH4SwingHigh_Ignored_UnbrokenOneAboveTheEntryUsed()
    {
        // A swing high at 102.0 (bar 3) that the last bar 13 closes above, and a higher unbroken swing high at 104.0 (bar 7).
        IReadOnlyList<Bar> h4 = H4(14, (3, 102.0, 100.0), (7, 104.0, 100.0), (13, 102.5, 102.2));

        ChochTarget target = Buy(Array.Empty<Zone>(), h4);

        Assert.Equal(Swing(7), target.Source);
        Assert.Equal(104.0 - (0.1 * Atr), target.Price, Digits);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void SwingHighClosedAtItsPriceOnly_IsNotBroken()
    {
        // The last bar 8 closes exactly at the swing high 102.0: not above it, so the swing still counts.
        IReadOnlyList<Bar> h4 = H4(9, (3, 102.0, 100.0), (8, 102.0, 102.0));

        Assert.Equal(Swing(3), Buy(Array.Empty<Zone>(), h4).Source);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void ReferenceHistory_TheBrokenSwingAt101_IsNotAnOpposingLevelForTailB()
    {
        // Tail B's entry 100.65 lies below the broken swing high 101.0 of the reference H4 history.
        double stop = 99.8 - (0.1 * 4.7 / 14.0);

        ChochTarget target = ChochDetector.Target(Side.Buy, 100.65, stop, Array.Empty<Zone>(), ChochScenarios.H4(), Atr, EngineScenarios.Defaults, ChochScenarios.Defaults);

        Assert.Equal("default", target.Source);
        Assert.Equal(102.41714286, target.Price, 8);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void EngineWithAnUnbrokenH4SwingHighAt103_5_TargetBeforeIt()
    {
        IReadOnlyList<FeedBar> feed = ChochScenarios.History(BiasHistory.Long, ChochScenarios.TailA, h4: ChochScenarios.H4WithSwingHigh(103.5));

        Signal signal = Assert.Single(ChochScenarios.Signals(ChochScenarios.Run(feed)));

        Assert.Equal("swing:" + ChochScenarios.AddedSwingOpen.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture), signal.TargetSource);
        Assert.Equal(103.39285714, signal.Target, 8);
        Assert.Equal(101.0, signal.Entry);
        Assert.Equal(99.87321429, signal.Stop, 8);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void LevelGivingExactlyTwoR_AndOneTickLower()
    {
        double exact = Entry + (2.0 * (Entry - StopA)) + (0.1 * Atr);
        Assert.Equal(103.36071429, exact, 8);

        ChochTarget atTwo = Buy(new[] { ZoneOf("S-1", ZoneType.Supply, exact, 104.0) }, H4(10));
        ChochTarget lower = Buy(new[] { ZoneOf("S-1", ZoneType.Supply, exact - 0.00001, 104.0) }, H4(10));

        Assert.True(PriceMath.AlmostEqual((atTwo.Price - Entry) / (Entry - StopA), 2.0, PriceMath.DefaultTolerance));
        Assert.True(PriceMath.LessThan((lower.Price - Entry) / (Entry - StopA), 2.0, PriceMath.DefaultTolerance));
        Assert.Equal("zone:S-1", lower.Source);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void LevelExactlyAtTheEntry_NotCounted()
    {
        ChochTarget target = Buy(new[] { ZoneOf("S-1", ZoneType.Supply, 101.0, 101.5) }, H4(10, (4, 101.0, 100.0)));

        Assert.Equal("default", target.Source);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void ZoneAndSwingAtTheSamePrice_TheZoneIsTheSource()
    {
        ChochTarget target = Buy(new[] { ZoneOf("S-1", ZoneType.Supply, 103.5, 104.0) }, H4(10, (4, 103.5, 100.0)));

        Assert.Equal("zone:S-1", target.Source);
        Assert.Equal(103.39285714, target.Price, 8);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void SeveralLevels_TheNearestIsUsed()
    {
        Zone far = ZoneOf("S-1", ZoneType.Supply, 104.0, 104.5);
        Zone near = ZoneOf("S-2", ZoneType.Supply, 102.5, 103.0, ZoneState.Touched);
        Zone demand = ZoneOf("D-1", ZoneType.Demand, 101.5, 101.0);
        IReadOnlyList<Bar> h4 = H4(18, (3, 103.0, 100.0), (12, 102.2, 100.0));

        ChochTarget target = Buy(new[] { demand, far, near }, h4);

        Assert.Equal(Swing(12), target.Source);
        Assert.Equal(102.2 - (0.1 * Atr), target.Price, Digits);
        Assert.Equal("zone:S-2", Buy(new[] { demand, far, near }, H4(10)).Source);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void Ties_LowestZoneIdThenOldestSwing()
    {
        Zone first = ZoneOf("S-1", ZoneType.Supply, 103.0, 103.5);
        Zone second = ZoneOf("S-2", ZoneType.Supply, 103.0, 104.0);

        Assert.Equal("zone:S-1", Buy(new[] { first, second }, H4(10)).Source);
        Assert.Equal(Swing(3), Buy(Array.Empty<Zone>(), H4(18, (3, 103.0, 100.0), (12, 103.0, 100.0))).Source);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void Sell_Mirror_HighestLevelBelowTheEntryPlusTheBuffer()
    {
        // Mirror of tail A around 200: entry 99.0, stop 100.12678571; a demand zone at 96.5 and an unbroken swing low at 97.0.
        double stop = 200.0 - StopA;
        Zone demand = ZoneOf("D-1", ZoneType.Demand, 96.5, 96.0);
        Zone supply = ZoneOf("S-1", ZoneType.Supply, 98.0, 98.5);
        IReadOnlyList<Bar> h4 = H4(10, (4, 97.0, 100.0));

        ChochTarget levels = ChochDetector.Target(Side.Sell, 99.0, stop, new[] { demand, supply }, h4, Atr, EngineScenarios.Defaults, ChochScenarios.Defaults);
        ChochTarget none = ChochDetector.Target(Side.Sell, 99.0, stop, new[] { supply }, H4(10), Atr, EngineScenarios.Defaults, ChochScenarios.Defaults);
        ChochTarget zoneOnly = ChochDetector.Target(Side.Sell, 99.0, stop, new[] { demand }, H4(10), Atr, EngineScenarios.Defaults, ChochScenarios.Defaults);

        Assert.Equal(Swing(4), levels.Source);
        Assert.Equal(97.0 + (0.1 * Atr), levels.Price, Digits);
        Assert.Equal("default", none.Source);
        Assert.Equal(96.74642857, none.Price, 8);
        Assert.Equal("zone:D-1", zoneOnly.Source);
        Assert.Equal(96.5 + (0.1 * Atr), zoneOnly.Price, Digits);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void Sell_BrokenSwingLow_Ignored()
    {
        // A swing low at 97.0 (bar 3) that the last bar 8 closes below.
        IReadOnlyList<Bar> h4 = H4(9, (3, 97.0, 100.0), (8, 96.5, 96.8));

        ChochTarget target = ChochDetector.Target(Side.Sell, 99.0, 100.2, Array.Empty<Zone>(), h4, Atr, EngineScenarios.Defaults, ChochScenarios.Defaults);

        Assert.Equal("default", target.Source);
        Assert.Equal(99.0 - (2.0 * 1.2), target.Price, Digits);
    }

    [Fact]
    [Trait("Req", "SIG-040")]
    public void BufferZero_TargetAtTheLevel_AndNoH4Bars_Default()
    {
        var choch = new ChochParameters { ChochTargetBufferAtr = 0.0 };

        ChochTarget target = ChochDetector.Target(Side.Buy, Entry, StopA, new[] { ZoneOf("S-1", ZoneType.Supply, 103.5, 104.0) }, H4(10), Atr, EngineScenarios.Defaults, choch);
        ChochTarget empty = ChochDetector.Target(Side.Buy, Entry, StopA, Array.Empty<Zone>(), Array.Empty<Bar>(), Atr, EngineScenarios.Defaults, choch);

        Assert.Equal(103.5, target.Price);
        Assert.Equal("default", empty.Source);
    }
}
