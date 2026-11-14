using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Bias gates only the signal (SIG-015, SIG-018). The same H4 and M15 history is run with three D1
/// histories giving Long, Short and Neutral bias. Zones are identical in the three engines at every
/// bar; only the returned signals differ.
/// </summary>
public class SignalEngineBiasTests
{
    [Theory]
    [Trait("Req", "SIG-015")]
    [Trait("Req", "SIG-018")]
    [InlineData(false)] // demand zone: only Long signals, a buy
    [InlineData(true)] // mirrored, supply zone: only Short signals, a sell
    public void ThreeBiases_SameZonesAtEveryBar_OnlyTheMatchingBiasSignals(bool mirrored)
    {
        IReadOnlyList<EngineStep> underLong = Run(BiasHistory.Long, mirrored);
        IReadOnlyList<EngineStep> underShort = Run(BiasHistory.Short, mirrored);
        IReadOnlyList<EngineStep> underNeutral = Run(BiasHistory.Neutral, mirrored);

        Assert.Equal(Bias.Long, underLong[^1].Bias);
        Assert.Equal(Bias.Short, underShort[^1].Bias);
        Assert.Equal(Bias.Neutral, underNeutral[^1].Bias);
        Assert.Equal(underLong.Count, underShort.Count);
        Assert.Equal(underLong.Count, underNeutral.Count);
        int barsWithAZone = 0;
        for (int i = 0; i < underLong.Count; i++)
        {
            // The D1 bars differ in price; every H4 and M15 bar is the same in the three runs.
            Assert.Equal(underLong[i].Input.TimeFrame, underShort[i].Input.TimeFrame);
            Assert.Equal(underLong[i].Zones, underShort[i].Zones);
            Assert.Equal(underLong[i].Zones, underNeutral[i].Zones);
            barsWithAZone += underLong[i].Zones.Count > 0 ? 1 : 0;
        }

        Assert.True(barsWithAZone > 2, "The zone should be visible on several bars.");
        IReadOnlyList<Signal> matching = EngineFeed.AllSignals(mirrored ? underShort : underLong);
        IReadOnlyList<Signal> opposite = EngineFeed.AllSignals(mirrored ? underLong : underShort);
        Signal signal = Assert.Single(matching);
        Assert.Equal(mirrored ? Side.Sell : Side.Buy, signal.Side);
        Assert.Equal(mirrored ? EngineScenarios.MirroredZoneId : EngineScenarios.ZoneId, signal.ZoneId);
        Assert.Empty(opposite);
        Assert.Empty(EngineFeed.AllSignals(underNeutral));
    }

    [Theory]
    [Trait("Req", "SIG-015")]
    [InlineData(BiasHistory.Long)]
    [InlineData(BiasHistory.Short)]
    [InlineData(BiasHistory.Neutral)]
    public void AnyBias_ZoneIsDetectedTouchedAndEnded(BiasHistory bias)
    {
        IReadOnlyList<EngineStep> steps = Run(bias, false);

        Zone[] seen = steps.SelectMany(step => step.Zones).ToArray();
        Assert.Contains(seen, zone => zone.Id == EngineScenarios.ZoneId && zone.State == ZoneState.Active);
        Assert.Contains(seen, zone => zone.Id == EngineScenarios.ZoneId && zone.State == ZoneState.Touched);
        Assert.Empty(steps[^1].Zones);
    }

    private static IReadOnlyList<EngineStep> Run(BiasHistory bias, bool mirrored)
    {
        IReadOnlyList<FeedBar> feed = EngineScenarios.History(bias, 5, 5, 40, mirrored, EngineScenarios.TouchAndGap, EngineScenarios.SecondGap);
        return EngineFeed.Run(new SignalEngine(EngineScenarios.Defaults), feed);
    }
}
