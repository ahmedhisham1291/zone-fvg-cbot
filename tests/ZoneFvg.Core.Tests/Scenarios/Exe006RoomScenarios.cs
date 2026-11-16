using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-006: a buy is skipped if the proximal line of a non-terminal supply zone lies between entry
/// and entry + MinRoomR (1.5) x stop distance; a sell mirrors this with demand zones. Buy: entry
/// 1.10000, stop 1.09800, R = 20 pips, so 1.5R is 1.10300. Sell: signal stop 1.10190 plus the
/// 1-pip spread = 1.10200, R = 20 pips, so 1.5R is 1.09700. "Between" excludes both ends.
/// </summary>
public class Exe006RoomScenarios
{
    private static IReadOnlyList<Zone> Zones(params Zone[] zones)
    {
        return zones;
    }

    [Fact]
    [Trait("Req", "EXE-006")]
    public void Buy_OpposingProximalAt1Point49R_IsSkipped()
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(), zones: Zones(ExecutionData.Zone("S-20260113-0800", ZoneType.Supply, 1.10298)));

        Assert.Equal(SkipReason.NoRoom, skip.Reason);
        Assert.Equal(1.49, skip.Measured, 9);
        Assert.Equal(1.5, skip.Limit);
        Assert.Contains("S-20260113-0800", skip.Detail, StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-006")]
    public void Buy_OpposingProximalAt1Point5R_IsPlaced()
    {
        var h = new ExecutionHarness();

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy(), zones: Zones(ExecutionData.Zone("S-20260113-0800", ZoneType.Supply, 1.103)));

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-006")]
    [InlineData(ZoneState.Used)]
    [InlineData(ZoneState.Invalid)]
    [InlineData(ZoneState.Expired)]
    public void Buy_TerminalSupplyZoneInTheWay_IsIgnored(ZoneState state)
    {
        var h = new ExecutionHarness();

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy(), zones: Zones(ExecutionData.Zone("S-20260113-0800", ZoneType.Supply, 1.102, state)));

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-006")]
    [InlineData(ZoneState.Touched)]
    [InlineData(ZoneState.Signalled)]
    public void Buy_NonTerminalSupplyZoneInTheWay_IsSkipped(ZoneState state)
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(), zones: Zones(ExecutionData.Zone("S-20260113-0800", ZoneType.Supply, 1.102, state)));

        Assert.Equal(SkipReason.NoRoom, skip.Reason);
        Assert.Equal(1.0, skip.Measured, 9);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-006")]
    [InlineData(ZoneType.Demand, 1.102)] // same side as the trade: not opposing
    [InlineData(ZoneType.Supply, 1.1)] // proximal exactly at entry: not between
    [InlineData(ZoneType.Supply, 1.099)] // below entry
    public void Buy_ZonesNotBetweenEntryAnd1Point5R_DoNotBlock(ZoneType type, double proximal)
    {
        var h = new ExecutionHarness();

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Buy(), zones: Zones(ExecutionData.Zone("Z-20260113-0800", type, proximal)));

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-006")]
    public void Buy_TwoZonesInTheWay_TheNearestIsReported()
    {
        // 1.10250 is 25 pips = 1.25R; 1.10290 is 29 pips = 1.45R.
        var h = new ExecutionHarness();
        IReadOnlyList<Zone> zones = Zones(
            ExecutionData.Zone("S-20260113-0800", ZoneType.Supply, 1.1029),
            ExecutionData.Zone("S-20260113-1200", ZoneType.Supply, 1.1025));

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(), zones: zones);

        Assert.Equal(1.25, skip.Measured, 9);
        Assert.Contains("S-20260113-1200", skip.Detail, StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-006")]
    public void Sell_OpposingProximalAt1Point49R_IsSkipped()
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.Sell(stop: 1.1019), zones: Zones(ExecutionData.Zone("D-20260113-0800", ZoneType.Demand, 1.09702)));

        Assert.Equal(SkipReason.NoRoom, skip.Reason);
        Assert.Equal(1.49, skip.Measured, 9);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-006")]
    public void Sell_OpposingProximalAt1Point5R_IsPlacedAndSupplyAboveIsIgnored()
    {
        var h = new ExecutionHarness();
        IReadOnlyList<Zone> zones = Zones(
            ExecutionData.Zone("D-20260113-0800", ZoneType.Demand, 1.097),
            ExecutionData.Zone("S-20260113-0800", ZoneType.Supply, 1.099));

        PlaceResult? result = h.EvaluateAndPlace(ExecutionData.Sell(stop: 1.1019), zones: zones);

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }
}
