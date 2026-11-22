using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-020: after an unclear outcome of a stop order with nothing at the broker, the order is
/// resent once only when the signal is at most one M15 bar old, a bar has closed since the
/// confirmation, at least 15 minutes of order life are left and no closed bar since the
/// confirmation reached the signal's entry before the spread: high at or above it for a buy
/// (1.08630; the order's entry is 1.08640), low at or below it for a sell (1.07670). The signals
/// confirm at 10:00; the next bar opens at 10:00 and closes at 10:15. The rule for limit orders is
/// the one of EXE-009.
/// </summary>
public class Exe020StopOrderResendScenarios
{
    private static readonly DateTime NextClose = ExecutionData.At(14, 10, 15);

    private static Bar NextBar(double high, double low)
    {
        double middle = (high + low) / 2.0;
        return ExecutionData.M15(ExecutionData.Now, middle, high, low, middle);
    }

    private static ExecutionHarness Unclear(Side side, BrokerFault fault = BrokerFault.UnclearNotApplied, string news = ExecutionData.QuietNews)
    {
        ExecutionHarness h = ExecutionData.BreakoutHarness(side == Side.Buy ? ExecutionData.BuyQuote() : ExecutionData.SellQuote(), news);
        h.Broker.Script(BrokerCall.Place, fault, "Timeout");
        Signal signal = side == Side.Buy ? ExecutionData.BreakoutBuy() : ExecutionData.BreakoutSell();
        Assert.Equal(PlaceOutcome.Unclear, h.EvaluateAndPlace(signal)!.Outcome);
        return h;
    }

    [Theory]
    [Trait("Req", "EXE-020")]
    [InlineData(1.0862)]
    [InlineData(1.08629)]
    public void BuyStop_NextBarHighBelowTheSignalEntry_IsResentOnce(double high)
    {
        // The low 1.08550 is below the order's entry: the limit order rule would not resend.
        ExecutionHarness h = Unclear(Side.Buy);
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(high, 1.0855) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal(2, h.Broker.Count(BrokerCall.Place));
        Assert.All(h.Broker.Requests, request => Assert.NotNull(request.StopOrder));
        Assert.Equal(1.0864, Assert.Single(h.Broker.GetPendingOrders()).EntryPrice);
        Assert.Equal(0, h.Orders.UnresolvedCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-020")]
    [InlineData(1.0863)]
    [InlineData(1.08635)]
    [InlineData(1.08629999995)]
    public void BuyStop_NextBarHighAtOrAboveTheSignalEntry_IsAbandoned(double high)
    {
        // 1.08630 and 1.08635 are below the order's entry 1.08640: the signal's entry decides.
        ExecutionHarness h = Unclear(Side.Buy);
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(high, 1.0855) }));

        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Contains("traded through", resolved.Detail, StringComparison.Ordinal);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());
        SkipRecord skip = Assert.Single(h.Events.Skips);
        Assert.Equal(SkipReason.UnclearOutcomeNotResent, skip.Reason);
        Assert.Equal(ExecutionData.BreakoutBuyId, skip.ZoneId);
        Assert.Equal(0, h.Orders.UnresolvedCount);
        ScenarioAssert.Global(h.Broker);
    }

    [Theory]
    [Trait("Req", "EXE-020")]
    [InlineData(1.0767, false)]
    [InlineData(1.0766, false)]
    [InlineData(1.07670000005, false)]
    [InlineData(1.07671, true)]
    public void SellStop_NextBarLowAgainstTheSignalEntry_DecidesTheResend(double low, bool resent)
    {
        // The high 1.07750 is above the entry: the limit order rule for a sell would not resend.
        ExecutionHarness h = Unclear(Side.Sell);
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.0775, low) }));

        Assert.Equal(resent ? PlaceOutcome.Placed : PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Equal(resent ? 2 : 1, h.Broker.Count(BrokerCall.Place));
        Assert.Equal(resent, h.Broker.GetPendingOrders().Any());
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-020")]
    public void BuyStop_BarBeforeTheConfirmationAboveTheEntry_DoesNotCount()
    {
        // Resolved at 10:15 with the bar 09:45 to 10:00 (before the confirmation, ignored) above
        // the entry, and the bar 10:00 to 10:15 below it: resent. The earlier bar does not count.
        ExecutionHarness h = Unclear(Side.Buy);
        h.Broker.AdvanceTo(NextClose);
        Bar before = ExecutionData.M15(ExecutionData.At(14, 9, 45), 1.0858, 1.087, 1.0855, 1.0866);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { before, NextBar(1.0862, 1.0855) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
    }

    [Fact]
    [Trait("Req", "EXE-020")]
    public void UnclearApplied_FoundAtTheNextBarVerifiedAndNotResent()
    {
        ExecutionHarness h = Unclear(Side.Buy, BrokerFault.UnclearApplied);
        long id = Assert.Single(h.Broker.GetPendingOrders()).Id;
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.087, 1.0855) }));

        Assert.Equal(PlaceOutcome.Placed, resolved.Outcome);
        Assert.Equal("verified", resolved.Detail);
        Assert.Equal(id, resolved.OrderId);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-020")]
    public void TwoBarsOld_IsAbandoned()
    {
        ExecutionHarness h = Unclear(Side.Buy);
        h.Broker.AdvanceTo(ExecutionData.At(14, 10, 30));

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.0862, 1.0855), ExecutionData.M15(NextClose, 1.0858, 1.0862, 1.0855, 1.0858) }));

        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Contains("no longer fresh", resolved.Detail, StringComparison.Ordinal);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "EXE-020")]
    public void BeforeABarHasClosedSinceTheConfirmation_IsKeptUnresolved()
    {
        ExecutionHarness h = Unclear(Side.Buy);
        h.Broker.AdvanceTo(NextClose.AddTicks(-1));

        Assert.Empty(h.Orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));

        Assert.Equal(1, h.Orders.UnresolvedCount);
        Assert.True(h.Orders.HasUnresolved("EURUSD"));
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
    }

    [Fact]
    [Trait("Req", "EXE-020")]
    public void NoClosedBarGiven_IsAbandoned()
    {
        ExecutionHarness h = Unclear(Side.Buy);
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(Array.Empty<Bar>()));

        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Contains("price path unknown", resolved.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-020")]
    public void LessThan15MinutesOfOrderLifeLeft_IsAbandoned()
    {
        // A USD event at 10:50 opens a window at 10:20: the order expires at 10:20. At 10:15 five
        // minutes are left.
        ExecutionHarness h = Unclear(Side.Buy, news: "2026-01-14 10:50,USD,High,Speech\n");
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.0862, 1.0855) }));

        Assert.Equal(PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.Contains("15 minutes", resolved.Detail, StringComparison.Ordinal);
        Assert.Equal(1, h.Broker.Count(BrokerCall.Place));
    }

    [Theory]
    [Trait("Req", "EXE-020")]
    [InlineData(1.10001, true)]
    [InlineData(1.1, false)]
    public void LimitOrder_AfterUnclearNotApplied_KeepsTheRuleOfExe009(double low, bool resent)
    {
        // The reference ZoneFvg buy (entry 1.10000) in the breakout harness: the low decides.
        ExecutionHarness h = ExecutionData.BreakoutHarness(ExecutionData.BuyQuote());
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        Assert.Equal(PlaceOutcome.Unclear, h.EvaluateAndPlace(ExecutionData.Buy())!.Outcome);
        h.Broker.AdvanceTo(NextClose);

        PlaceResult resolved = Assert.Single(h.Orders.ResolvePending(new[] { NextBar(1.1008, low) }));

        Assert.Equal(resent ? PlaceOutcome.Placed : PlaceOutcome.Abandoned, resolved.Outcome);
        Assert.All(h.Broker.Requests, request => Assert.Null(request.StopOrder));
        Assert.Equal(resent ? 2 : 1, h.Broker.Count(BrokerCall.Place));
    }
}
