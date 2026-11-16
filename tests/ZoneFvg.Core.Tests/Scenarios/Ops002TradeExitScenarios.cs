using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-002 through the whole core: the trade journal line of a live trade carries the exit reason
/// the adapter saw and the largest adverse and favourable moves in R. Data: <see cref="RecoveryData"/>.
/// The reference buy fills at 08:50 at 100.675 (stop 99.482, risk 1.193) and closes at 09:30. The
/// M15 bars of its life (08:45, 09:00, 09:15) range from 100.9 to 101.0. At the target 103.061:
/// adverse 0, favourable 2.386 / 1.193 = 2.00 R. At the stop: adverse 1.00 R, favourable
/// (101.0 - 100.675) / 1.193 = 0.27 R.
/// </summary>
public class Ops002TradeExitScenarios
{
    private static readonly DateTime A = RecoveryData.Activation;

    private static BotHarness Run(bool hitStop, Action<BotHarness>? beforeClose = null, Func<IBarHistory, IBarHistory>? wrap = null)
    {
        var h = new BotHarness();
        h.Broker.ReportExitReasons = true;
        h.Broker.AdvanceTo(A);
        BotCore core = h.NewCore();
        core.OnStart(wrap == null ? h.History : wrap(h.History));
        h.FeedTo(RecoveryData.Confirmation, core);
        h.FillReferenceOrder(A.AddMinutes(50.0));
        core.OnTradeEvent();
        h.FeedTo(A.AddMinutes(75.0), core);
        h.Broker.AdvanceTo(A.AddMinutes(90.0));
        beforeClose?.Invoke(h);
        h.Broker.Tick(RecoveryData.Symbol, hitStop ? 99.47 : 103.07, hitStop ? 99.48 : 103.08);
        core.OnTradeEvent();
        return h;
    }

    private static string TradeLine(BotHarness h)
    {
        return Assert.Single(h.Journal.Lines("trade"), line => line.Contains(" trade symbol=", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Journal_TargetHit_HasExitTakeProfitAndMovesInR()
    {
        BotHarness h = Run(hitStop: false);

        string line = TradeLine(h);
        Assert.Contains(" exit=TakeProfit r=2.00 ", line, StringComparison.Ordinal);
        Assert.EndsWith(" mae=0.00 mfe=2.00", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Journal_StopHit_HasExitStopLossAndOneRAdverse()
    {
        BotHarness h = Run(hitStop: true);

        string line = TradeLine(h);
        Assert.Contains(" exit=StopLoss r=-1.00 ", line, StringComparison.Ordinal);
        Assert.EndsWith(" mae=1.00 mfe=0.27", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Journal_BarsDoNotReachTheFill_MovesAreNaAndOrdersUnchanged()
    {
        BotHarness normal = Run(hitStop: false);
        BotHarness h = Run(hitStop: false, beforeClose: x => x.History.Available[TimeFrame.M15] = 1);

        Assert.EndsWith(" mae=NA mfe=NA", TradeLine(h), StringComparison.Ordinal);
        Assert.Equal(normal.Broker.Requests, h.Broker.Requests);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Journal_BarHistoryFails_MovesAreNaTheFailureIsJournaledAndTheTradeIsJournaledOnce()
    {
        FailingHistory? failing = null;
        BotHarness h = Run(hitStop: false, beforeClose: _ => failing!.Fail = true, wrap: inner => failing = new FailingHistory(inner));

        Assert.EndsWith(" mae=NA mfe=NA", TradeLine(h), StringComparison.Ordinal);
        Assert.Equal(1, h.Journal.Count("trade", "event=excursion-unavailable reason=InvalidOperationException:\\sbars\\sunavailable"));
    }

    /// <summary>A bar history that forwards to the harness's history and can be told to throw.</summary>
    private sealed class FailingHistory : IBarHistory
    {
        private readonly IBarHistory _inner;

        public FailingHistory(IBarHistory inner)
        {
            _inner = inner;
        }

        public bool Fail { get; set; }

        public IReadOnlyList<Bar> ClosedBars(TimeFrame timeFrame, int count)
        {
            return Fail ? throw new InvalidOperationException("bars unavailable") : _inner.ClosedBars(timeFrame, count);
        }

        public void RequestMore(TimeFrame timeFrame, int count)
        {
            _inner.RequestMore(timeFrame, count);
        }
    }
}
