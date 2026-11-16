using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-001: a signal is executed only if its confirmation bar closed at most one M15 bar
/// (15 minutes) before server time and it was produced live. Server time 10:00 (see
/// <see cref="ExecutionData"/>).
/// </summary>
public class Exe001FreshnessScenarios
{
    private static Signal Of(Side side, DateTime close, bool fromReplay = false)
    {
        return side == Side.Buy ? ExecutionData.Buy(closeUtc: close, fromReplay: fromReplay) : ExecutionData.Sell(closeUtc: close, fromReplay: fromReplay);
    }

    [Theory]
    [Trait("Req", "EXE-001")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void FreshLiveSignal_IsPlaced(Side side)
    {
        var h = new ExecutionHarness();

        PlaceResult? result = h.EvaluateAndPlace(Of(side, ExecutionData.Now));

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        Assert.Single(h.Broker.GetPendingOrders());
        Assert.Empty(h.Events.Skips);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-001")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void SignalExactlyOneBarOld_IsPlaced(Side side)
    {
        // Confirmed at 09:45, server time 10:00: 15 minutes, the limit.
        var h = new ExecutionHarness();

        PlaceResult? result = h.EvaluateAndPlace(Of(side, ExecutionData.At(14, 9, 45)));

        Assert.Equal(PlaceOutcome.Placed, result!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-001")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void SignalOneTickOlderThanOneBar_IsSkipped(Side side)
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(Of(side, ExecutionData.At(14, 9, 45).AddTicks(-1)));

        Assert.Equal(SkipReason.SignalStale, skip.Reason);
        Assert.True(skip.Measured > 15.0, "age " + skip.Measured);
        Assert.Equal(15.0, skip.Measured, 6);
        Assert.Equal(15.0, skip.Limit);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-001")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void TwoBarOldSignal_IsSkipped(Side side)
    {
        // Confirmed at 09:30, server time 10:00: 30 minutes.
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(Of(side, ExecutionData.At(14, 9, 30)));

        Assert.Equal(SkipReason.SignalStale, skip.Reason);
        Assert.Equal(30.0, skip.Measured, 9);
        Assert.Equal(15.0, skip.Limit);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-001")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void ReplaySignal_IsSkippedEvenWhenFresh(Side side)
    {
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(Of(side, ExecutionData.Now, fromReplay: true));

        Assert.Equal(SkipReason.SignalFromReplay, skip.Reason);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Empty(h.Broker.MarginRequests);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-001")]
    public void SignalConfirmedAfterServerTime_IsSkipped()
    {
        // A confirmation close later than server time cannot come from a closed bar: fail closed.
        var h = new ExecutionHarness();

        SkipRecord skip = h.SkipOf(Of(Side.Buy, ExecutionData.At(14, 10, 15)));

        Assert.Equal(SkipReason.SignalStale, skip.Reason);
        Assert.Equal(-15.0, skip.Measured, 9);
        ScenarioAssert.Global(h.Broker);
    }
}
