using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Indicators;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Zones of both types are detected and tracked whatever the bias (SIG-015). Neither
/// <see cref="ZoneDetector"/> nor <see cref="ZoneBook"/> takes a bias, so these tests compute a
/// real bias next to them and show that a zone against that bias is still found and followed.
/// </summary>
public class ZoneBiasIndependenceTests
{
    private const int BiasEmaPeriod = 50;

    private static readonly SignalParameters Defaults = new();

    [Fact]
    [Trait("Req", "SIG-015")]
    public void SupplyZone_WhileBiasIsLong_IsDetectedAndTracked()
    {
        // 201 rising D1 closes (100.0, 100.1, ..., 120.0): the last close is above its EMA(50).
        Assert.Equal(Bias.Long, BiasCalculator.Compute(D1Closes(0.1), BiasEmaPeriod));

        // Mirror of scenario A: a supply zone with proximal 99.8, distal 100.5, activation
        // 2026-01-10 04:00.
        IReadOnlyList<Bar> h4 = ZoneScenarios.ScenarioA().Mirror(200.0);
        Zone zone = Detected(h4);
        Assert.Equal(ZoneType.Supply, zone.Type);

        // An M15 bar at activation with high 99.9 >= 99.8 touches it; close 99.6 < 100.5.
        Zone touched = TrackOneTouch(zone, 99.6, 99.9, 99.5, 99.6);

        Assert.Equal(ZoneType.Supply, touched.Type);
        Assert.Equal(ZoneState.Touched, touched.State);
        Assert.Equal(99.9, touched.ExtremeSinceTouch);
    }

    [Fact]
    [Trait("Req", "SIG-015")]
    public void DemandZone_WhileBiasIsShort_IsDetectedAndTracked()
    {
        // 201 falling D1 closes (120.0 down to 100.0): the last close is below its EMA(50).
        Assert.Equal(Bias.Short, BiasCalculator.Compute(D1Closes(-0.1), BiasEmaPeriod));

        // Scenario A: a demand zone with proximal 100.2, distal 99.5.
        Zone zone = Detected(ZoneScenarios.ScenarioA());
        Assert.Equal(ZoneType.Demand, zone.Type);

        // An M15 bar at activation with low 100.1 <= 100.2 touches it; close 100.3 > 99.5.
        Zone touched = TrackOneTouch(zone, 100.4, 100.5, 100.1, 100.3);

        Assert.Equal(ZoneType.Demand, touched.Type);
        Assert.Equal(ZoneState.Touched, touched.State);
        Assert.Equal(100.1, touched.ExtremeSinceTouch);
    }

    [Fact]
    [Trait("Req", "SIG-015")]
    public void BothZoneTypes_TrackedTogetherInOneBook()
    {
        // The demand zone of scenario A and a supply zone of the same history (mirrored, then
        // shifted up by 10.0 so the ranges do not meet) live side by side in one book.
        Zone demand = Detected(ZoneScenarios.ScenarioA());
        Zone supply = Detected(ZoneScenarios.ScenarioA().Mirror(210.0));
        var book = new ZoneBook(Defaults);

        Assert.True(book.Add(demand));
        Assert.True(book.Add(supply));

        Assert.Equal(new[] { ZoneType.Demand, ZoneType.Supply }, book.Zones.Select(z => z.Type).ToArray());
    }

    private static Zone Detected(IReadOnlyList<Bar> h4)
    {
        ZoneDetection result = ZoneDetector.Detect(h4, SwingDetector.Find(h4, ZoneScenarios.Strength), Defaults);
        Assert.Equal(ZoneDetectionOutcome.ZoneCreated, result.Outcome);
        return result.Zone!;
    }

    private static Zone TrackOneTouch(Zone zone, double open, double high, double low, double close)
    {
        var book = new ZoneBook(Defaults);
        Assert.True(book.Add(zone));
        Bar m15 = BarBuilder.One(TimeFrame.M15, zone.ActivationTimeUtc, open, high, low, close);

        Zone touched = Assert.Single(book.OnM15Closed(m15));

        Assert.Equal(touched, Assert.Single(book.Zones));
        return touched;
    }

    private static IReadOnlyList<Bar> D1Closes(double step)
    {
        double first = step > 0.0 ? 100.0 : 120.0;
        return new BarBuilder(TimeFrame.D1)
            .At(new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc))
            .Repeat(201, (b, i) => b.Doji(first + (step * i)))
            .Build();
    }
}
