using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using Data = ZoneFvg.Core.Tests.SessionRangeBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-026 through <see cref="BotCore"/> and a <see cref="FakeBroker"/>: the broker reports the
/// market closed from 20:50 UTC, as cTrader does for gold all year, while M15 bars keep coming. At
/// the closed M15 bar that makes the timed exit due the close is sent whatever the reported market
/// state; on the timer, and at a bar that closed before the exit, the duty waits while the market is
/// reported closed, as EXE-025 says. Data: <see cref="SessionRangeBotData"/>.
/// </summary>
public class Exe026TimedExitHoursScenarios
{
    private static readonly DateTime Start = Data.Utc(2026, 3, 4, 6, 0);
    private static readonly DateTime WinterExit = Data.Utc(2026, 3, 4, 21, 30);
    private static readonly DateTime ReportedClose = Data.Utc(2026, 3, 4, 20, 50);

    // Placed at 03:00 New York and filled 20 minutes later: the ask reaches the buy stop 100.41.
    private static (BotHarness Harness, long Position) Filled(DateTime start, DateTime tradingDate)
    {
        BotHarness h = Data.Started(start, Data.Bars(tradingDate));
        DateTime placement = Data.Evaluation(tradingDate);
        h.FeedTo(placement);
        h.Broker.ReportExitReasons = true;
        h.Broker.AdvanceTo(placement.AddMinutes(20.0));
        h.Broker.Tick(Data.Symbol, 100.4, 100.41);
        h.Core.OnTradeEvent();
        PositionInfo position = Assert.Single(h.Broker.GetPositions());
        Assert.Equal(Data.Label(Data.BuyId(tradingDate)), position.Label);
        return (h, position.Id);
    }

    private static void ReportClosed(BotHarness h)
    {
        h.Broker.SetSymbol(Data.Quote() with { IsMarketOpen = false });
    }

    [Fact]
    [Trait("Req", "EXE-026")]
    public void Winter_MarketReportedClosedFrom2050Utc_ClosedAtThe2130UtcBarWithoutDeferral()
    {
        (BotHarness h, long position) = Filled(Start, Data.Winter);
        h.FeedTo(ReportedClose);
        ReportClosed(h);

        h.FeedTo(WinterExit.AddMinutes(-15.0));
        h.Timer(WinterExit.AddMinutes(-1.0));
        Assert.Equal(position, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(0, h.Count(BrokerCall.Close));

        h.FeedTo(WinterExit);
        h.Core.OnTradeEvent();

        Assert.Empty(h.Broker.GetPositions());
        BrokerRequest close = Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close);
        Assert.Equal(position, close.TargetId);
        Assert.Equal(WinterExit, close.AtUtc);
        Assert.Equal(TradeExitReason.ClosedByBot, Assert.Single(h.Broker.History).ExitReason);
        Assert.Equal(1, h.Journal.Count("action", "duty=range-exit "));
        Assert.Equal(0, h.Journal.Count("range-exit", "range-exit event=deferred"));

        h.FeedTo(WinterExit.AddMinutes(30.0));
        h.Timer(WinterExit.AddMinutes(31.0));
        Assert.Equal(1, h.Count(BrokerCall.Close));
        Assert.Equal(0, h.Journal.Count("range-exit", "range-exit event=deferred"));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "EXE-026")]
    public void Summer_ExitAt2030UtcBeforeTheReportedClose_ClosedAtThe2030UtcBarAsBefore()
    {
        DateTime exit = Data.Utc(2026, 3, 12, 20, 30);
        (BotHarness h, long position) = Filled(Data.Utc(2026, 3, 12, 5, 0), Data.Summer);
        h.FeedTo(exit.AddMinutes(-15.0));
        Assert.Equal(0, h.Count(BrokerCall.Close));

        h.FeedTo(exit);
        h.Core.OnTradeEvent();

        Assert.Empty(h.Broker.GetPositions());
        BrokerRequest close = Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close);
        Assert.Equal(position, close.TargetId);
        Assert.Equal(exit, close.AtUtc);

        h.FeedTo(Data.Utc(2026, 3, 12, 20, 50));
        ReportClosed(h);
        h.FeedTo(exit.AddHours(1.0));
        h.Timer(exit.AddHours(1.0).AddMinutes(1.0));
        Assert.Equal(1, h.Count(BrokerCall.Close));
        Assert.Equal(0, h.Journal.Count("range-exit", "range-exit event=deferred"));
    }

    [Fact]
    [Trait("Req", "EXE-026")]
    public void Timer_ExitDueWithoutABarAndMarketReportedClosed_DeferredAndClosedAtTheReopen()
    {
        (BotHarness h, long position) = Filled(Start, Data.Winter);
        h.FeedTo(WinterExit.AddMinutes(-15.0));
        ReportClosed(h);

        h.Timer(WinterExit);
        h.Timer(WinterExit.AddMinutes(1.0));

        Assert.Equal(0, h.Count(BrokerCall.Close));
        Assert.Equal(position, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(1, h.Journal.Count("range-exit", "range-exit event=deferred reason=market-closed"));

        h.Broker.SetSymbol(Data.Quote());
        h.Timer(WinterExit.AddMinutes(5.0));

        Assert.Equal(position, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close).TargetId);
        Assert.Empty(h.Broker.GetPositions());
    }

    [Fact]
    [Trait("Req", "EXE-026")]
    public void LateBar_ClosedBeforeTheExitAndMarketReportedClosed_DeferredUntilTheBarAtTheExit()
    {
        (BotHarness h, long position) = Filled(Start, Data.Winter);
        h.FeedTo(WinterExit.AddMinutes(-30.0));
        ReportClosed(h);
        h.Broker.AdvanceTo(WinterExit.AddMinutes(1.0));

        // The 21:15 bar arrives at 21:31: it is no proof that the market trades at the exit.
        h.FeedTo(WinterExit.AddMinutes(-15.0));

        Assert.Equal(0, h.Count(BrokerCall.Close));
        Assert.Equal(position, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(1, h.Journal.Count("range-exit", "range-exit event=deferred reason=market-closed"));

        h.FeedTo(WinterExit);

        Assert.Equal(position, Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close).TargetId);
        Assert.Empty(h.Broker.GetPositions());
    }

    [Fact]
    [Trait("Req", "EXE-026")]
    public void Refused_AtTheBarWithMarketReportedClosed_OneRequestPerBarClosedWhenAccepted()
    {
        (BotHarness h, long position) = Filled(Start, Data.Winter);
        h.FeedTo(ReportedClose);
        ReportClosed(h);
        h.Broker.Script(BrokerCall.Close, BrokerFault.Rejected);

        h.FeedTo(WinterExit);

        BrokerRequest refused = Assert.Single(h.Broker.Requests, request => request.Call == BrokerCall.Close);
        Assert.Equal(OrderOutcome.Rejected, refused.Outcome);
        Assert.Equal(position, Assert.Single(h.Broker.GetPositions()).Id);

        h.Timer(WinterExit.AddMinutes(1.0));
        h.Timer(WinterExit.AddMinutes(10.0));
        Assert.Equal(1, h.Count(BrokerCall.Close));

        h.FeedTo(WinterExit.AddMinutes(15.0));

        Assert.Equal(2, h.Count(BrokerCall.Close));
        Assert.Equal(OrderOutcome.Success, h.Broker.Requests.Last(request => request.Call == BrokerCall.Close).Outcome);
        Assert.Empty(h.Broker.GetPositions());

        h.FeedTo(WinterExit.AddMinutes(30.0));
        Assert.Equal(2, h.Count(BrokerCall.Close));
    }

    [Fact]
    [Trait("Req", "EXE-026")]
    public void OtherKinds_MarketReportedClosed_PositionOfAnotherKindNotTouched()
    {
        BotHarness h = Data.Harness(Start, Data.Bars());
        long breakout = h.Broker.AddPosition(Data.Symbol, Side.Buy, 1000.0, 100.2, 99.5, 101.0, BreakoutRecoveryData.BuyLabel, BreakoutRecoveryData.BuyComment());
        h.Start(Start, Data.Parameters());
        h.FeedTo(ReportedClose);
        ReportClosed(h);

        h.FeedTo(WinterExit.AddMinutes(30.0));
        h.Timer(WinterExit.AddMinutes(31.0));

        Assert.Equal(breakout, Assert.Single(h.Broker.GetPositions()).Id);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Equal(0, h.Journal.Count("range-exit", "range-exit event=deferred"));
    }

    public static IEnumerable<object[]> OtherModes()
    {
        yield return new object[] { "ZoneFvg" };
        yield return new object[] { "Breakout" };
    }

    [Theory]
    [Trait("Req", "EXE-026")]
    [MemberData(nameof(OtherModes))]
    public void OtherModes_SessionRangePositionAtTheExitBar_NoRequestAndNoExtraBrokerRead(string mode)
    {
        (BotHarness open, int openReads) = OtherModeRun(mode, false);
        (BotHarness closed, int closedReads) = OtherModeRun(mode, true);

        Assert.Single(open.Broker.GetPositions());
        Assert.Single(closed.Broker.GetPositions());
        Assert.Equal(0, open.Count(BrokerCall.Close));
        Assert.Equal(0, closed.Count(BrokerCall.Close));
        Assert.Equal(openReads, closedReads);
        Assert.Equal(0, closed.Journal.Count("range-exit", "range-exit event=deferred"));
    }

    // The broker reads of the bars and timers around the exit, with the market reported open or closed from 20:50 UTC.
    private static (BotHarness Harness, int Reads) OtherModeRun(string mode, bool reportClosed)
    {
        BotParameters parameters = mode == "Breakout" ? BreakoutRecoveryData.Parameters() : RecoveryData.Parameters();
        BotHarness h = Data.Harness(Start, Data.Bars());
        h.Broker.AddPosition(Data.Symbol, Side.Buy, 1000.0, 100.41, 100.0, 101.2, Data.Label(Data.BuyId(Data.Winter)), BreakoutRecoveryData.BuyComment());
        h.Start(Start, parameters);
        h.FeedTo(ReportedClose);
        if (reportClosed)
        {
            ReportClosed(h);
        }

        int before = h.Broker.QueryCount;
        h.FeedTo(WinterExit.AddMinutes(30.0));
        h.Timer(WinterExit.AddMinutes(31.0));
        return (h, h.Broker.QueryCount - before);
    }
}
