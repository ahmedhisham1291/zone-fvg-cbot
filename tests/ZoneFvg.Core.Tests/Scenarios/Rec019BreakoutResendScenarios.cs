using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using Data = ZoneFvg.Core.Tests.BreakoutRecoveryData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-019, follow-up of the review of WP29: fault scenario 6 through the core in Breakout mode
/// with an unclear placement that was not applied and bars that do not reach the entry. Bar set of
/// the gap scenario of <see cref="Rec019BreakoutFaultScenarios"/>: the bar 09:15 to 09:30 has the
/// high 100.35, below the signal's entry 100.36. The Wednesday buy confirms at 09:15 and becomes
/// the buy stop ZFVG|v1|USDJPY|BH-20260303-0000 at 100.37, expiry 11:15. At the close of that
/// bar the placement is one bar old with nothing at the broker and nothing in the history, so
/// EXE-020 resends it once.
/// </summary>
public class Rec019BreakoutResendScenarios
{
    // The bar set of the data, with the bar 09:15 to 09:30 replaced as in the gap scenario.
    private static BarSet GapBars()
    {
        BarSet bars = Data.Bars();
        List<Bar> m15 = bars.M15
            .Select(bar => bar.OpenTimeUtc == Data.Confirmation ? BarBuilder.One(TimeFrame.M15, bar.OpenTimeUtc, 100.34, 100.35, 99.9, 99.95) : bar)
            .ToList();
        return new BarSet(bars.D1, bars.H4, m15);
    }

    private static int WithLabel(BotHarness h, string label)
    {
        return h.Broker.GetPendingOrders().Count(order => order.Label == label) + h.Broker.GetPositions().Count(position => position.Label == label);
    }

    [Fact]
    [Trait("Req", "REC-019")]
    public void Fault06_UnclearNotAppliedAndBarsBelowTheEntry_StopOrderSentASecondTimeOnce_OneOrderForTheBreakoutId()
    {
        BotHarness h = Data.Harness(Data.Start, bars: GapBars());
        BotCore core = h.Start(Data.Start, Data.Parameters());
        h.FeedTo(Data.Confirmation.AddMinutes(-15.0));
        h.Broker.Script(BrokerCall.Place, BrokerFault.UnclearNotApplied, "Timeout");
        h.FeedTo(Data.Confirmation);
        Assert.Equal(1, h.Count(BrokerCall.Place));
        Assert.Empty(h.Broker.GetPendingOrders());
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(1.0));
        core.OnDisconnected();
        h.Broker.AdvanceTo(Data.Confirmation.AddMinutes(5.0));
        core.OnConnected(h.History);

        // The bar 09:15 to 09:30 resolves the placement; the next bar resolves nothing.
        h.FeedTo(Data.Confirmation.AddMinutes(30.0));

        Assert.Equal(2, h.Count(BrokerCall.Place));
        Assert.All(h.Broker.Requests, request => Assert.NotNull(request.StopOrder));
        Assert.All(h.Broker.Requests, request => Assert.Equal(Data.BuyLabel, request.StopOrder!.Label));
        Assert.Equal(1, WithLabel(h, Data.BuyLabel));
        Assert.Equal(Data.BuyLabel, Assert.Single(h.Broker.GetPendingOrders()).Label);
        string resolved = Assert.Single(h.Journal.Lines("order"), line => line.Contains(" event=resolved ", StringComparison.Ordinal));
        Assert.Contains(" outcome=Placed detail=verified", resolved, StringComparison.Ordinal);
        Assert.Equal(0, h.Journal.Count("skip", "reason=UnclearOutcomeNotResent"));
        Assert.Equal(BotState.Running, core.State);
        ScenarioAssert.Global(h.Broker);
    }
}
