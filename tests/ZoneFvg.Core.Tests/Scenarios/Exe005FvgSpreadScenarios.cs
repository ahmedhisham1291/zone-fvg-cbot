using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-005: a signal is skipped if its gap height is below MinFvgSpreadMult (3) x the current
/// spread. Spread 1 pip: the gap must be at least 3 pips. Gap 1.09985 to 1.10015 is 3.0 pips;
/// 1.09986 to 1.10015 is 2.9 pips.
/// </summary>
public class Exe005FvgSpreadScenarios
{
    private static Signal Of(Side side, double upper, double lower)
    {
        return side == Side.Buy ? ExecutionData.Buy(gapUpper: upper, gapLower: lower) : ExecutionData.Sell(gapUpper: upper, gapLower: lower);
    }

    [Theory]
    [Trait("Req", "EXE-005")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void GapExactlyThreeSpreads_IsPlaced(Side side)
    {
        var h = new ExecutionHarness();

        PlaceResult? result = h.EvaluateAndPlace(Of(side, 1.10015, 1.09985));

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-005")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void GapBelowThreeSpreads_IsSkipped(Side side)
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(Of(side, 1.10015, 1.09986));

        Assert.Equal(SkipReason.FvgTooSmallForSpread, skip.Reason);
        Assert.Equal(2.9, skip.Measured, 9);
        Assert.Equal(3.0, skip.Limit, 9);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-005")]
    public void WiderCurrentSpread_RaisesTheMinimum()
    {
        // Spread 1.1 pips: the 3.0-pip gap is below 3 x 1.1 = 3.3.
        var h = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(spread: 0.00011)));

        SkipRecord skip = h.SkipOf(Of(Side.Buy, 1.10015, 1.09985));

        Assert.Equal(SkipReason.FvgTooSmallForSpread, skip.Reason);
        Assert.Equal(3.0, skip.Measured, 9);
        Assert.Equal(3.3, skip.Limit, 9);
        ScenarioAssert.Global(h.Broker);
    }
}
