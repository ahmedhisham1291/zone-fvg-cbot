using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Signals;
using Data = ZoneFvg.Core.Tests.SessionRangeBotData;
using Trend = ZoneFvg.Core.Tests.SignalEngineRangeTrendTests;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-042 through <see cref="BotCore"/> and a <see cref="FakeBroker"/>, on bars where the two
/// direction rules disagree (the D1 series of <see cref="SignalEngineRangeTrendTests"/>: on the
/// last D1 bar before the winter evaluation of 2026-03-04 the close is below EMA(50) while EMA(20)
/// is above it). With RangeTrendFastEma 0 the bot places a sell stop at the range low, with 20 a buy
/// stop at the range high, for the same bars. The parameter hash with 20, c3c1d46e, was computed
/// outside the code under test with Python's hashlib from the canonical text.
/// </summary>
public class Sig042RangeTrendScenarios
{
    private static BarSet Bars(bool sell)
    {
        return Trend.WithD1(Trend.D1(Data.Utc(2025, 6, 1, 0, 0), Trend.Rising(Data.Utc(2026, 3, 3, 0, 0), -0.05), sell), sell, Data.Winter);
    }

    private static BotHarness Run(int fast, bool sell)
    {
        BotParameters parameters = Data.Parameters() with { SessionRange = new SessionRangeParameters { RangeTrendFastEma = fast } };
        BotHarness h = Data.Started(Data.Utc(2026, 3, 4, 6, 0), Bars(sell), sell, parameters);
        h.FeedTo(Data.Utc(2026, 3, 4, 16, 0));
        return h;
    }

    private static StopOrderRequest OnlyStopOrder(BotHarness h)
    {
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Equal(BrokerCall.Place, request.Call);
        Assert.Equal(Data.Evaluation(Data.Winter), request.AtUtc);
        return request.StopOrder!;
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void SameBars_Zero_SellStopAtTheRangeLow()
    {
        BotHarness h = Run(0, sell: false);

        StopOrderRequest order = OnlyStopOrder(h);
        Assert.Equal(Side.Sell, order.Side);
        Assert.Equal(100.0, order.EntryPrice, 9);
        Assert.Equal(100.41, order.StopLossPrice, 9);
        Assert.Equal(99.2, order.TakeProfitPrice, 9);
        Assert.Equal(Data.Label(Data.SellId(Data.Winter)), order.Label);
        Assert.Equal("3f0f30a3", OrderComment.TryParse(order.Comment)!.ParameterHash);
        Assert.Contains(" result=taken bias=Short zone=RL-20260304 ", Assert.Single(h.Journal.Lines("signal")), StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void SameBars_Twenty_BuyStopAtTheRangeHighPlusSpread()
    {
        BotHarness h = Run(20, sell: false);

        StopOrderRequest order = OnlyStopOrder(h);
        Assert.Equal(Side.Buy, order.Side);
        Assert.Equal(Data.OrderEntry, order.EntryPrice, 9);
        Assert.Equal(Data.Stop, order.StopLossPrice, 9);
        Assert.Equal(Data.Target, order.TakeProfitPrice, 9);
        Assert.Equal(Data.Utc(2026, 3, 4, 17, 0), order.ExpiryUtc);
        Assert.Equal(Data.Label(Data.BuyId(Data.Winter)), order.Label);
        Assert.True(order.VolumeUnits > 0.0);
        Assert.Equal("c3c1d46e", OrderComment.TryParse(order.Comment)!.ParameterHash);

        // The journal's bias field is the bias of 3.1, unchanged (Short); the signal's side is the direction.
        string signal = Assert.Single(h.Journal.Lines("signal"));
        Assert.Contains(" result=taken bias=Short zone=RH-20260304 ", signal, StringComparison.Ordinal);
        Assert.EndsWith(" skip=none kind=range level=100.4", signal, StringComparison.Ordinal);
        Assert.Contains(" hash=c3c1d46e ", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);
        Assert.Equal(BotState.Running, h.Core.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void MirroredBars_ZeroBuys_TwentySells()
    {
        BotHarness zero = Run(0, sell: true);
        BotHarness twenty = Run(20, sell: true);

        StopOrderRequest buy = OnlyStopOrder(zero);
        StopOrderRequest sell = OnlyStopOrder(twenty);
        Assert.Equal(Side.Buy, buy.Side);
        Assert.Equal(Data.Label(Data.BuyId(Data.Winter)), buy.Label);
        Assert.Equal(Side.Sell, sell.Side);
        Assert.Equal(Data.SellEntry, sell.EntryPrice, 9);
        Assert.Equal(Data.SellOrderStop, sell.StopLossPrice, 9);
        Assert.Equal(Data.SellTarget, sell.TakeProfitPrice, 9);
        Assert.Equal(Data.Label(Data.SellId(Data.Winter)), sell.Label);
        ScenarioAssert.Global(zero.Broker);
        ScenarioAssert.Global(twenty.Broker);
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Restart_AfterTheEvaluationWithTwenty_TheReplayRebuildsTheBuyAndPlacesNothing()
    {
        BotParameters parameters = Data.Parameters() with { SessionRange = new SessionRangeParameters { RangeTrendFastEma = 20 } };

        BotHarness h = Data.Started(Data.Utc(2026, 3, 4, 8, 30), Bars(false), parameters: parameters);
        h.FeedTo(Data.Utc(2026, 3, 4, 16, 0));

        string signal = Assert.Single(h.Journal.Lines("signal"));
        Assert.Contains(" result=skipped bias=Short zone=RH-20260304 ", signal, StringComparison.Ordinal);
        Assert.EndsWith(" skip=SignalFromReplay kind=range level=100.4", signal, StringComparison.Ordinal);
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }
}
