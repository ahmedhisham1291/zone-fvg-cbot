using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-011: every bot position must have a stop loss. A position without one gets it set from its
/// comment; after two failed attempts it is closed and an alert raised. While any bot position has
/// no stop loss, no new entry is placed (architecture section 10). The position: buy 250,000
/// EURUSD at 1.10000, comment e=1.10000;s=1.09800;t=1.10400;r=500.00;h=f681dff2.
/// </summary>
public class Exe011ProtectionScenarios
{
    private const string Comment = "e=1.10000;s=1.09800;t=1.10400;r=500.00;h=f681dff2";
    private static readonly string OtherLabel = ExecutionData.Label("D-20260113-0800");

    private static long Unprotected(ExecutionHarness h, double? takeProfit = 1.104, string comment = Comment)
    {
        return h.Broker.AddPosition("EURUSD", Side.Buy, 250000.0, 1.1, null, takeProfit, OtherLabel, comment);
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void PositionWithoutStop_GetsItRestoredFromTheComment()
    {
        var h = new ExecutionHarness();
        long id = Unprotected(h);

        TradeAction action = Assert.Single(h.Trades.EnsureProtection());

        Assert.Equal(new TradeAction(id, TradeActionOutcome.StopRestored, "stop 1.098"), action);
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(1.098, position.StopLoss);
        Assert.Equal(1.104, position.TakeProfit);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Modify));
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.StopLossMissing));
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.StopLossRestored));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void PositionWithoutStopAndTarget_GetsBothFromTheComment()
    {
        var h = new ExecutionHarness();
        Unprotected(h, takeProfit: null);

        h.Trades.EnsureProtection();

        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Equal(1.098, request.StopLoss);
        Assert.Equal(1.104, request.TakeProfit);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void ExistingTarget_IsKeptWhenTheStopIsSet()
    {
        var h = new ExecutionHarness();
        Unprotected(h, takeProfit: 1.105);

        h.Trades.EnsureProtection();

        Assert.Equal(1.105, Assert.Single(h.Broker.GetPositions()).TakeProfit);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-011")]
    [InlineData(BrokerFault.Rejected)]
    [InlineData(BrokerFault.UnclearNotApplied)]
    [InlineData(BrokerFault.SuccessNotApplied)]
    public void TwoFailedAttempts_PositionClosedAndAlerted(BrokerFault fault)
    {
        var h = new ExecutionHarness();
        long id = Unprotected(h);
        h.Broker.Script(BrokerCall.Modify, fault);
        h.Broker.Script(BrokerCall.Modify, fault);

        TradeAction action = Assert.Single(h.Trades.EnsureProtection());

        Assert.Equal(id, action.Id);
        Assert.Equal(TradeActionOutcome.Closed, action.Outcome);
        Assert.Empty(h.Broker.GetPositions());
        Assert.Equal(2, h.Broker.Count(BrokerCall.Modify));
        Assert.Equal(1, h.Broker.Count(BrokerCall.Close));
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.PositionClosedWithoutStop));
        Assert.Equal(0, h.Events.AlertsOf(AlertKind.StopLossRestored));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void OneFailureThenSuccess_StopRestoredOnTheSecondAttempt()
    {
        var h = new ExecutionHarness();
        Unprotected(h);
        h.Broker.Script(BrokerCall.Modify, BrokerFault.Rejected);

        Assert.Equal(TradeActionOutcome.StopRestored, Assert.Single(h.Trades.EnsureProtection()).Outcome);

        Assert.Equal(2, h.Broker.Count(BrokerCall.Modify));
        Assert.Equal(0, h.Broker.Count(BrokerCall.Close));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void UnclearButApplied_TheBrokerReadDecidesAndNoSecondAttemptIsMade()
    {
        var h = new ExecutionHarness();
        Unprotected(h);
        h.Broker.Script(BrokerCall.Modify, BrokerFault.UnclearApplied);

        Assert.Equal(TradeActionOutcome.StopRestored, Assert.Single(h.Trades.EnsureProtection()).Outcome);

        Assert.Equal(1, h.Broker.Count(BrokerCall.Modify));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void UnreadableComment_PositionClosedAtOnce()
    {
        var h = new ExecutionHarness();
        Unprotected(h, comment: "manual edit");

        TradeAction action = Assert.Single(h.Trades.EnsureProtection());

        Assert.Equal(TradeActionOutcome.Closed, action.Outcome);
        Assert.Equal("comment cannot be read", action.Detail);
        Assert.Equal(0, h.Broker.Count(BrokerCall.Modify));
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.PositionClosedWithoutStop));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void CloseAlsoFails_AlertedAndThePositionIsStillThere()
    {
        // The global assertion cannot hold here: the broker refuses both repairs and the close.
        var h = new ExecutionHarness();
        Unprotected(h);
        h.Broker.Script(BrokerCall.Modify, BrokerFault.Rejected);
        h.Broker.Script(BrokerCall.Modify, BrokerFault.Rejected);
        h.Broker.Script(BrokerCall.Close, BrokerFault.Rejected, "MarketClosed");

        TradeAction action = Assert.Single(h.Trades.EnsureProtection());

        Assert.Equal(TradeActionOutcome.CloseFailed, action.Outcome);
        Assert.Equal(1, h.Events.AlertsOf(AlertKind.CloseFailed));
        Assert.Single(h.Broker.GetPositions());
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void ProtectedAndForeignPositions_AreLeftAlone()
    {
        var h = new ExecutionHarness();
        h.Broker.SetSymbol(RiskData.GbpUsd);
        h.Broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.1, 1.098, 1.104, OtherLabel, Comment);
        h.Broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.1, null, null, "manual", "c");
        h.Broker.AddPosition("GBPUSD", Side.Buy, 1000.0, 1.25, null, null, "ZFVG|v1|GBPUSD|D-20260113-0800", Comment);

        Assert.Empty(h.Trades.EnsureProtection());

        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Empty(h.Events.Alerts);
    }

    [Theory]
    [Trait("Req", "EXE-011")]
    [InlineData(Side.Buy)]
    [InlineData(Side.Sell)]
    public void WhileABotPositionHasNoStop_ANewValidSignalIsSkippedAndNoOrderSent(Side side)
    {
        var h = new ExecutionHarness();
        long id = Unprotected(h);

        SkipRecord skip = h.SkipOf(side == Side.Buy ? ExecutionData.Buy() : ExecutionData.Sell());

        Assert.Equal(SkipReason.PositionWithoutStop, skip.Reason);
        Assert.Equal(1.0, skip.Measured);
        Assert.Contains(id.ToString(System.Globalization.CultureInfo.InvariantCulture), skip.Detail, StringComparison.Ordinal);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Empty(h.Broker.MarginRequests);

        // Once the stop is restored, the gate opens; the symbol is then busy with that position.
        h.Trades.EnsureProtection();
        Assert.Equal(SkipReason.SymbolBusy, h.SkipOf(side == Side.Buy ? ExecutionData.Buy() : ExecutionData.Sell()).Reason);
        Assert.Equal(0, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void BotPositionWithoutStopOnAnotherSymbol_AlsoBlocksNewEntries()
    {
        // "Any bot position" is account-wide: an unprotected GBPUSD bot position blocks EURUSD entries.
        var h = new ExecutionHarness();
        h.Broker.AddPosition("GBPUSD", Side.Buy, 1000.0, 1.25, null, 1.254, "ZFVG|v1|GBPUSD|D-20260113-0800", Comment);

        Assert.Equal(SkipReason.PositionWithoutStop, h.SkipOf(ExecutionData.Buy()).Reason);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }

    [Fact]
    [Trait("Req", "EXE-011")]
    public void ManualPositionWithoutStop_DoesNotBlockNewEntries()
    {
        var h = new ExecutionHarness();
        h.Broker.AddPosition("EURUSD", Side.Buy, 1000.0, 1.1, null, null, "manual", "c");

        Assert.Equal(PlaceOutcome.Placed, h.EvaluateAndPlace(ExecutionData.Buy())!.Outcome);
        ScenarioAssert.Global(h.Broker);
    }
}
