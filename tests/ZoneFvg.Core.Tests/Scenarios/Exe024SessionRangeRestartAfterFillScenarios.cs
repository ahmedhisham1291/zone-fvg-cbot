using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using Data = ZoneFvg.Core.Tests.SessionRangeBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-024 (decision of the orchestrating session for WP35, item 3): a restart after the session
/// range order has filled, when the replay of recovery then finds the setup void. The position is
/// adopted and left untouched, and no cancel or close request is sent because of the void. Data:
/// <see cref="SessionRangeBotData"/>, winter day 2026-03-04: placement 08:00 UTC, fill 08:20 UTC,
/// the void bar opens 10:00 UTC and closes 10:15 UTC.
/// </summary>
public class Exe024SessionRangeRestartAfterFillScenarios
{
    private static readonly DateTime Start = Data.Utc(2026, 3, 4, 6, 0);
    private static readonly DateTime Placement = Data.Utc(2026, 3, 4, 8, 0);
    private static readonly DateTime FillTime = Data.Utc(2026, 3, 4, 8, 20);
    private static readonly DateTime VoidOpen = Data.Utc(2026, 3, 4, 10, 0);
    private static readonly string BuyLabel = Data.Label(Data.BuyId(Data.Winter));

    // Placed at 08:00 and filled at 08:20, when the ask reaches the buy stop 100.41.
    private static BotHarness Filled()
    {
        BotHarness h = Data.Started(Start, Data.Bars(false, VoidOpen, Data.Winter));
        h.FeedTo(Placement);
        Assert.Equal(BuyLabel, Assert.Single(h.Broker.GetPendingOrders()).Label);
        h.Broker.AdvanceTo(FillTime);
        h.Broker.Tick(Data.Symbol, 100.4, 100.41);
        h.Core.OnTradeEvent();
        Assert.Empty(h.Broker.GetPendingOrders());
        return h;
    }

    private static PositionInfo OnlyPosition(BotHarness h)
    {
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(BuyLabel, position.Label);
        Assert.Equal(Data.Stop, position.StopLoss!.Value, 9);
        Assert.Equal(Data.Target, position.TakeProfit!.Value, 9);
        return position;
    }

    [Theory]
    [Trait("Req", "EXE-024")]
    [InlineData(10, 30)]
    [InlineData(11, 0)]
    public void Restart_AfterTheFillAndAVoidWhileAway_PositionAdoptedUntouchedNoCancelNoClose(int hour, int minute)
    {
        BotHarness h = Filled();
        PositionInfo before = OnlyPosition(h);

        // The bot is away from 08:20; the void bar closes at 10:15, before the restart.
        DateTime restart = Data.Utc(2026, 3, 4, hour, minute);
        h.SkipTo(restart);
        BotCore restarted = h.Start(restart, Data.Parameters());

        Assert.Equal(BotState.Running, restarted.State);
        Assert.Equal(new[] { before.Id }, restarted.LastReport!.PositionsAdopted);
        Assert.Empty(restarted.LastReport.OrdersCancelled);
        Assert.Empty(restarted.LastReport.OrdersKept);
        Assert.Empty(restarted.LastReport.Strangers);

        h.FeedTo(Data.Utc(2026, 3, 4, 14, 0));

        Assert.Equal(0, h.Count(BrokerCall.Cancel));
        Assert.Equal(0, h.Count(BrokerCall.Close));
        Assert.Equal(0, h.Count(BrokerCall.Modify));
        Assert.Equal(BrokerCall.Place, Assert.Single(h.Broker.Requests).Call);
        PositionInfo after = OnlyPosition(h);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.VolumeUnits, after.VolumeUnits);
        ScenarioAssert.Global(h.Broker);
    }
}
