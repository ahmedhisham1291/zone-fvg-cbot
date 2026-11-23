using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using Data = ZoneFvg.Core.Tests.SessionRangeBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-034 through <see cref="BotCore"/> and a <see cref="FakeBroker"/>: the setup of the trading
/// day becomes one buy stop (or sell stop) at 03:00 New York, in winter and in summer; a setup found
/// by the replay of a restart is journaled and not placed; the start line and the order comment
/// carry the mode and its hash. Data and every expected value: <see cref="SessionRangeBotData"/>.
/// </summary>
public class Sig034SessionRangeScenarios
{
    private static StopOrderRequest OnlyStopOrder(BotHarness h)
    {
        BrokerRequest request = Assert.Single(h.Broker.Requests);
        Assert.Equal(BrokerCall.Place, request.Call);
        Assert.Null(request.Order);
        return request.StopOrder!;
    }

    private static void AssertBuyOrder(StopOrderRequest order, DateTime tradingDate, DateTime expiryUtc)
    {
        Assert.Equal(Data.Symbol, order.Symbol);
        Assert.Equal(Side.Buy, order.Side);
        Assert.Equal(Data.OrderEntry, order.EntryPrice, 9);
        Assert.Equal(Data.Stop, order.StopLossPrice, 9);
        Assert.Equal(Data.Target, order.TakeProfitPrice, 9);
        Assert.Equal(expiryUtc, order.ExpiryUtc);
        Assert.Equal(Data.Label(Data.BuyId(tradingDate)), order.Label);
        Assert.True(order.VolumeUnits > 0.0);
        Assert.Equal(Data.Hash, OrderComment.TryParse(order.Comment)!.ParameterHash);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Winter_At0300NewYork_OneBuyStopAtTheRangeHighPlusSpreadEndingAt1200NewYork()
    {
        BotHarness h = Data.Started(Data.Utc(2026, 3, 4, 6, 0), Data.Bars(Data.Winter));

        // 03:00 New York is 08:00 UTC on 2026-03-04 (winter time).
        h.FeedTo(Data.Utc(2026, 3, 4, 7, 45));
        Assert.Empty(h.Broker.Requests);
        Assert.Empty(h.Journal.Lines("signal"));

        h.FeedTo(Data.Utc(2026, 3, 4, 8, 0));

        AssertBuyOrder(OnlyStopOrder(h), Data.Winter, Data.Utc(2026, 3, 4, 17, 0));
        Assert.Equal(Data.Utc(2026, 3, 4, 8, 0), h.Broker.Requests[0].AtUtc);
        string signal = Assert.Single(h.Journal.Lines("signal"));
        Assert.Contains(" signal symbol=USDJPY result=taken bias=Long zone=RH-20260304 zoneProximal=NA zoneDistal=NA fvgUpper=NA fvgLower=NA ", signal, StringComparison.Ordinal);
        Assert.EndsWith(" skip=none kind=range level=100.4", signal, StringComparison.Ordinal);
        Assert.Equal(BotState.Running, h.Core.State);

        // One signal per trading day: nothing more on that day.
        h.FeedTo(Data.Utc(2026, 3, 4, 16, 0));
        Assert.Single(h.Broker.Requests);
        Assert.Single(h.Journal.Lines("signal"));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Summer_At0300NewYork_OneBuyStopAt0700UtcEndingAt1600Utc()
    {
        BotHarness h = Data.Started(Data.Utc(2026, 3, 12, 5, 0), Data.Bars(Data.Summer));

        // 03:00 New York is 07:00 UTC on 2026-03-12 (summer time since 2026-03-08).
        h.FeedTo(Data.Utc(2026, 3, 12, 6, 45));
        Assert.Empty(h.Broker.Requests);

        h.FeedTo(Data.Utc(2026, 3, 12, 7, 0));

        AssertBuyOrder(OnlyStopOrder(h), Data.Summer, Data.Utc(2026, 3, 12, 16, 0));
        Assert.Equal(Data.Utc(2026, 3, 12, 7, 0), h.Broker.Requests[0].AtUtc);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void ShortBias_OneSellStopAtTheRangeLowWithTheSpreadOnTheStop()
    {
        BotHarness h = Data.Started(Data.Utc(2026, 3, 4, 6, 0), Data.Bars(true, null, Data.Winter), sell: true);

        h.FeedTo(Data.Utc(2026, 3, 4, 8, 0));

        StopOrderRequest order = OnlyStopOrder(h);
        Assert.Equal(Side.Sell, order.Side);
        Assert.Equal(Data.SellEntry, order.EntryPrice, 9);
        Assert.Equal(Data.SellOrderStop, order.StopLossPrice, 9);
        Assert.Equal(Data.SellTarget, order.TakeProfitPrice, 9);
        Assert.Equal(Data.Utc(2026, 3, 4, 17, 0), order.ExpiryUtc);
        Assert.Equal(Data.Label(Data.SellId(Data.Winter)), order.Label);
        Assert.EndsWith(" skip=none kind=range level=99.6", Assert.Single(h.Journal.Lines("signal")), StringComparison.Ordinal);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Start_SessionRangeMode_StartLineEndsWithTheModeAndTheReportCarriesItsHash()
    {
        BotHarness h = Data.Started(Data.Utc(2026, 3, 4, 6, 0), Data.Bars(Data.Winter));

        string start = Assert.Single(h.Journal.Lines("start"));
        Assert.Contains(" hash=3f0f30a3 ", start, StringComparison.Ordinal);
        Assert.EndsWith(" mode=SessionRange", start, StringComparison.Ordinal);
        Assert.Equal(Data.Hash, h.Core.LastReport!.ParameterHash);
        Assert.Empty(h.Core.ZoneSnapshot);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Restart_AfterTheEvaluation_ReplayedSetupJournaledAndNotPlaced()
    {
        BotHarness h = Data.Started(Data.Utc(2026, 3, 4, 8, 30), Data.Bars(Data.Winter));

        string signal = Assert.Single(h.Journal.Lines("signal"));
        Assert.Contains(" result=skipped bias=Long zone=RH-20260304 zoneProximal=NA zoneDistal=NA ", signal, StringComparison.Ordinal);
        Assert.EndsWith(" skip=SignalFromReplay kind=range level=100.4", signal, StringComparison.Ordinal);
        Assert.Equal(1, h.Core.LastReport!.ReplaySignals);
        Assert.Equal(BotState.Running, h.Core.State);

        h.FeedTo(Data.Utc(2026, 3, 4, 16, 0));

        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Single(h.Journal.Lines("signal"));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Restart_BeforeTheEvaluation_TheRangeIsRebuiltAndTheLiveSetupIsPlaced()
    {
        // The first start is at 01:00 UTC, inside the range window; the restart at 05:00 replays the day's bars.
        BotHarness h = Data.Started(Data.Utc(2026, 3, 4, 1, 0), Data.Bars(Data.Winter));
        h.FeedTo(Data.Utc(2026, 3, 4, 4, 0));
        h.Start(Data.Utc(2026, 3, 4, 5, 0), Data.Parameters());

        h.FeedTo(Data.Utc(2026, 3, 4, 8, 0));

        AssertBuyOrder(OnlyStopOrder(h), Data.Winter, Data.Utc(2026, 3, 4, 17, 0));
        Assert.Equal(0, h.Core.LastReport!.ReplaySignals);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void DayWithoutARange_NoSignalNoOrder()
    {
        // Without a planned day the bars stay between 100.15 and 100.25: a range of 0.1, below 0.5 x ATR(D1) = 0.3.
        BotHarness h = Data.Started(Data.Utc(2026, 3, 4, 6, 0), Data.Bars());

        h.FeedTo(Data.Utc(2026, 3, 5, 17, 0));

        Assert.Empty(h.Journal.Lines("signal"));
        Assert.Equal(0, h.Broker.WriteRequestCount);
    }
}
