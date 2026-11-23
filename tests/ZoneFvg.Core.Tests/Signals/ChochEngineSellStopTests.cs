using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-039, sell side of the answer of 2026-11-23 (validator finding on WP37a): a sell's stop that
/// is not strictly above its entry confirms nothing, and the window goes on. The history is the
/// mirror around 200 of <c>ChochEngineTests.StopNotBelowTheEntry_ConfirmsNothing</c>: before the
/// activation a swing low at 99.85, above the proximal 99.8 of the supply zone S-20260301-0800; m0
/// touches (high 99.85) and closes below 99.85, so its high, the highest since the touch, equals
/// the entry.
/// </summary>
public class ChochEngineSellStopTests
{
    private static readonly (double Open, double High, double Low, double Close)[] Tail =
    {
        (100.0, 100.05, 99.95, 100.0),
        (100.0, 100.08, 99.95, 100.05),
        (100.05, 100.1, 100.0, 100.05),
        (100.05, 100.15, 100.0, 100.1),
        (100.1, 100.12, 100.0, 100.05),
        (100.05, 100.1, 100.0, 100.05),
        (100.05, 100.1, 100.0, 100.1),
        (100.15, 100.3, 100.15, 100.25),
    };

    [Fact]
    [Trait("Req", "SIG-039")]
    [Trait("Req", "SIG-037")]
    public void SellStopNotAboveTheEntry_ConfirmsNothing_WithTheBufferTheSellIsGiven()
    {
        IReadOnlyList<FeedBar> feed = ChochScenarios.History(BiasHistory.Short, Tail, mirrored: true, flatBefore: ChochScenarios.Warmup - 7, tailBeforeActivation: 7);

        IReadOnlyList<ChochStep> withBuffer = ChochScenarios.Run(feed);
        IReadOnlyList<ChochStep> withoutBuffer = ChochScenarios.Run(feed, parameters: EngineScenarios.Defaults with { StopBufferAtr = 0.0 });

        Signal signal = Assert.Single(ChochScenarios.At(withBuffer, 0).Signals);
        Assert.Equal(Side.Sell, signal.Side);
        Assert.Equal(EngineScenarios.MirroredZoneId, signal.ZoneId);
        Assert.Equal(99.85, signal.Entry, 9);
        Assert.True(signal.Stop > 99.85);

        Assert.Empty(ChochScenarios.Signals(withoutBuffer));
        ChochStep m0 = ChochScenarios.At(withoutBuffer, 0);
        Assert.DoesNotContain(m0.Activity.Ended, end => end.ZoneId == EngineScenarios.MirroredZoneId);
        Zone zone = Assert.Single(m0.Zones);
        Assert.Equal(ZoneState.Touched, zone.State);
        Assert.Equal(EngineScenarios.MirroredZoneId, zone.Id);
    }
}
