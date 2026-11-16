using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-002: a trade is skipped, recorded with its reason and alerted once if its computed risk
/// exceeds 0.55% of balance, its volume exceeds MaxVolumeUnits, or its stop or target is on the
/// wrong side of entry (exactly at entry counts as wrong, architecture section 10).
/// Reference buy: 20 pips, 250,000 units, risk 500 on a balance of 100,000.
/// </summary>
public class Rsk002SanityCapScenarios
{
    private static void AssertOneRefusalOneAlert(ExecutionHarness h, SkipRecord skip, SkipReason reason)
    {
        Assert.Equal(reason, skip.Reason);
        Assert.Single(h.Events.Skips);
        ExecutionAlert alert = Assert.Single(h.Events.Alerts);
        Assert.Equal(AlertKind.SanityCap, alert.Kind);
        Assert.Equal("EURUSD", alert.Symbol);
        Assert.Contains(reason.ToString(), alert.Message, StringComparison.Ordinal);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void RiskAbove055Percent_IsRefusedAndAlerted()
    {
        // RiskPercent 0.6: risk 600, volume 600 / (20 x 0.0001) = 300,000, risk money 600 above 550.
        var h = new ExecutionHarness(risk: ExecutionData.RiskDefaults with { RiskPercent = 0.6 });

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        AssertOneRefusalOneAlert(h, skip, SkipReason.RiskAboveSanityCap);
        Assert.Equal(600.0, skip.Measured, 6);
        Assert.Equal(550.0, skip.Limit, 6);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void VolumeAboveMaxVolumeUnits_IsRefusedAndAlerted()
    {
        var h = new ExecutionHarness(risk: ExecutionData.RiskDefaults with { MaxVolumeUnits = 249000.0 });

        SkipRecord skip = h.SkipOf(ExecutionData.Buy());

        AssertOneRefusalOneAlert(h, skip, SkipReason.VolumeAboveCap);
        Assert.Equal(250000.0, skip.Measured);
        Assert.Equal(249000.0, skip.Limit);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void VolumeExactlyAtMaxVolumeUnits_IsPlaced()
    {
        var h = new ExecutionHarness(risk: ExecutionData.RiskDefaults with { MaxVolumeUnits = 250000.0 });

        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ExecutionData.Buy())!.Outcome);
        Assert.Empty(h.Events.Alerts);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "RSK-002")]
    [InlineData(1.102)] // above entry
    [InlineData(1.1)] // exactly at entry
    public void BuyStopOnTheWrongSide_IsRefusedAndAlerted(double stop)
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.Buy(stop: stop));

        AssertOneRefusalOneAlert(h, skip, SkipReason.StopOnWrongSide);
        Assert.Equal(stop, skip.Measured, 9);
        Assert.Equal(1.1, skip.Limit, 9);
    }

    [Theory]
    [Trait("Req", "RSK-002")]
    [InlineData(1.0979)] // order stop 1.09800, below entry
    [InlineData(1.0999)] // order stop 1.10000, exactly at entry once the spread is added
    public void SellStopOnTheWrongSide_IsRefusedAndAlerted(double signalStop)
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(ExecutionData.Sell(stop: signalStop));

        AssertOneRefusalOneAlert(h, skip, SkipReason.StopOnWrongSide);
    }

    [Theory]
    [Trait("Req", "RSK-002")]
    [InlineData(Side.Buy, 1.099)]
    [InlineData(Side.Buy, 1.1)]
    [InlineData(Side.Sell, 1.101)]
    [InlineData(Side.Sell, 1.1)]
    public void TargetOnTheWrongSide_IsRefusedAndAlerted(Side side, double target)
    {
        var h = new ExecutionHarness();
        Signal signal = side == Side.Buy ? ExecutionData.Buy(target: target) : ExecutionData.Sell(target: target);

        SkipRecord skip = h.SkipOf(signal);

        AssertOneRefusalOneAlert(h, skip, SkipReason.TargetOnWrongSide);
        Assert.Equal(target, skip.Measured, 9);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void OtherRefusals_RaiseNoAlert()
    {
        var h = new ExecutionHarness();

        h.SkipOf(ExecutionData.Buy(closeUtc: ExecutionData.At(14, 9, 30)));
        h.SkipOf(ExecutionData.Buy(stop: 1.09881));

        Assert.Equal(2, h.Events.Skips.Count);
        Assert.Empty(h.Events.Alerts);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void BrokerMaximumVolume_ExactlyAtItIsPlacedAboveItIsSkipped()
    {
        // The broker's own maximum for the symbol (WP06): 250,000 places the 250,000 trade,
        // 249,000 skips it. This is not an RSK-002 sanity cap, so no alert is raised.
        var at = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(maxVolume: 250000.0)));
        var below = new ExecutionHarness(ExecutionHarness.DefaultBroker(ExecutionData.EurUsd(maxVolume: 249000.0)));

        Assert.Equal(PlaceOutcome.Placed, at.EvaluateAndPlace(ExecutionData.Buy())!.Outcome);
        SkipRecord skip = below.SkipOf(ExecutionData.Buy());

        Assert.Equal(SkipReason.AboveBrokerMaxVolume, skip.Reason);
        Assert.Equal(250000.0, skip.Measured);
        Assert.Equal(249000.0, skip.Limit);
        Assert.Empty(below.Events.Alerts);
        Assert.Empty(below.Broker.MarginRequests);
        ScenarioAssert.Global(at.Broker);
        ScenarioAssert.Global(below.Broker);
    }
}
