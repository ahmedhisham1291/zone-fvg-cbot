using System.Globalization;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using Data = ZoneFvg.Core.Tests.OvernightBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-033, spec v0.18 rows BA and BB: a SessionRange core (0.75%) and an Overnight core (1.0%) on
/// one XAUUSD <see cref="FakeBroker"/> account, each with its own store and journal, fed the same
/// bars (<see cref="OvernightBotData"/>, session ranges on 10, 11, 12, 13 and 16 March 2026) from
/// Monday 2026-03-09 12:00 UTC to Tuesday 2026-03-17 12:00 UTC, with the timer of each core called
/// at every M15 close. New York is in summer time and Prague in winter time: the Overnight order is
/// placed at 22:15 UTC and closed at 06:45 UTC; the SessionRange order is placed at 07:00 UTC and
/// closed at 20:30 UTC (on Friday by the Friday close at 20:00 UTC); a CE(S)T day starts at
/// 23:00 UTC. The daily budget is 1.5% of 100,000 = 1,500.
/// </summary>
/// <remarks>
/// The script, worked out by hand:
/// <list type="bullet">
/// <item>ON-20260310: placed, filled at 22:30, the SessionRange core restarted at 23:00 with it open,
/// closed at 06:45. RH-20260310: placed at 07:00, the Overnight core restarted at 07:15 with it
/// pending, filled at 07:30, the Overnight core restarted at 12:00 with it open; its close is
/// refused 16 times from 20:30 (one bar and one timer call per M15 close), so it is still open at
/// 22:15 and ON-20260311 is skipped as SymbolBusy (the one-position check counts every bot trade of
/// the symbol: fail closed); it closes at 22:30.</item>
/// <item>RH-20260311: placed, filled, closed at 20:30. ON-20260312: placed at 22:15 (the SessionRange
/// core restarted at 22:20 with it pending), filled at 22:30, stopped out at 01:00 UTC on 2026-03-12
/// at 2610.39: 25 ounces x 40 = 1,000 lost in the CE(S)T day of 2026-03-12.</item>
/// <item>RH-20260312 at 07:00: about 0.75% of 98,990 = 742 more would make 1,742 above 1,500: skipped
/// as DailyBudgetExceeded in the SessionRange core, by the Overnight core's loss. ON-20260313 at
/// 22:15 UTC (still the CE(S)T day of 2026-03-12): about 990 more, skipped the same way.</item>
/// <item>RH-20260313: placed, filled, closed by the Friday close. RH-20260316 (Monday): placed,
/// filled, closed at 20:30. ON-20260317: placed, filled, closed at 06:45.</item>
/// </list>
/// </remarks>
public class Exe033TwoInstanceScenarios
{
    private readonly BotHarness _h;
    private readonly MemoryStore _rangeStore = new();
    private readonly MemoryStore _nightStore = new();
    private readonly RecordingJournal _rangeJournal = new();
    private readonly RecordingJournal _nightJournal = new();
    private readonly BotParameters _rangeParameters = Data.Parameters(EntryMode.SessionRange, 0.75);
    private readonly BotParameters _nightParameters = Data.Parameters(EntryMode.Overnight, 1.0);
    private BotCore _range;
    private BotCore _night;
    private DateTime _at;
    private int _mostPositions;

    public Exe033TwoInstanceScenarios()
    {
        _at = Data.Utc(2026, 3, 9, 12, 0);
        _h = Data.Harness(_at, Data.Bars(Day(10), Day(11), Day(12), Day(13), Day(16)));
        _h.Broker.ReportExitReasons = true;
        _h.SkipTo(_at);
        _range = Data.Instance(_h, _rangeParameters, _rangeStore, _rangeJournal);
        _night = Data.Instance(_h, _nightParameters, _nightStore, _nightJournal);
    }

    private static DateTime Day(int day)
    {
        return new DateTime(2026, 3, day);
    }

    private static string Range(int day)
    {
        return Data.Label(Data.RangeId(Day(day)));
    }

    private static string Night(int day)
    {
        return Data.Label(Data.Id(Day(day)));
    }

    // Every M15 close up to the time: the bar to both cores, then each core's timer.
    private void RunTo(DateTime untilUtc)
    {
        for (DateTime t = _at.AddMinutes(15.0); t <= untilUtc; t = t.AddMinutes(15.0))
        {
            _h.FeedTo(t, _range, _night);
            _range.OnTimer();
            _night.OnTimer();
            CountPositions();
            _at = t;
        }
    }

    private void Tick(DateTime atUtc, double bid, double ask)
    {
        _h.Broker.AdvanceTo(atUtc);
        _h.Broker.Tick(Data.Symbol, bid, ask);
        _range.OnTradeEvent();
        _night.OnTradeEvent();
        CountPositions();
    }

    private void CountPositions()
    {
        _mostPositions = Math.Max(_mostPositions, _h.Broker.GetPositions().Count);
    }

    private BotCore Restart(RecordingJournal journal, MemoryStore store, BotParameters parameters, DateTime atUtc)
    {
        _h.Broker.AdvanceTo(atUtc);
        return Data.Instance(_h, parameters, store, journal);
    }

    private string OnlyPositionLabel()
    {
        return Assert.Single(_h.Broker.GetPositions()).Label;
    }

    private int RequestsOn(long id)
    {
        return _h.Broker.Requests.Count(request => request.TargetId == id);
    }

    private static IEnumerable<long> ActionIds(RecordingJournal journal)
    {
        foreach (string line in journal.Lines("action"))
        {
            string part = line.Split(' ').Single(field => field.StartsWith("id=", StringComparison.Ordinal));
            yield return long.Parse(part.Substring(3), CultureInfo.InvariantCulture);
        }
    }

    [Fact]
    [Trait("Req", "EXE-033")]
    public void TwoInstancesOnOneGoldAccount_EachTradesItsOwnHoursAndTouchesOnlyItsOwn()
    {
        // ------------------------------------------------------------ trading day 2026-03-10
        RunTo(Data.Utc(2026, 3, 9, 22, 15));
        Assert.Equal(Night(10), Assert.Single(_h.Broker.GetPendingOrders()).Label);
        Tick(Data.Utc(2026, 3, 9, 22, 31), 2650.2, 2650.5);
        long night10 = Assert.Single(_h.Broker.GetPositions()).Id;
        RunTo(Data.Utc(2026, 3, 9, 23, 0));

        _range = Restart(_rangeJournal, _rangeStore, _rangeParameters, Data.Utc(2026, 3, 9, 23, 5));
        Assert.Empty(_range.LastReport!.PositionsAdopted);
        Assert.Empty(_range.LastReport.Strangers);
        Assert.Empty(_range.LastReport.OrdersCancelled);
        Assert.Equal(night10, Assert.Single(_h.Broker.GetPositions()).Id);
        Assert.Equal(0, RequestsOn(night10));

        RunTo(Data.Utc(2026, 3, 10, 6, 30));
        Assert.Equal(night10, Assert.Single(_h.Broker.GetPositions()).Id);
        RunTo(Data.Utc(2026, 3, 10, 6, 45));
        Assert.Empty(_h.Broker.GetPositions());
        RunTo(Data.Utc(2026, 3, 10, 7, 0));
        PendingOrderInfo range10 = Assert.Single(_h.Broker.GetPendingOrders());
        Assert.Equal(Range(10), range10.Label);
        Assert.Equal(Data.RangeOrderEntry, range10.EntryPrice, 9);
        Assert.Equal(Data.RangeStop, range10.StopLoss!.Value, 9);

        _night = Restart(_nightJournal, _nightStore, _nightParameters, Data.Utc(2026, 3, 10, 7, 5));
        Assert.Empty(_night.LastReport!.OrdersCancelled);
        Assert.Empty(_night.LastReport.OrdersKept);
        Assert.Equal(range10.Id, Assert.Single(_h.Broker.GetPendingOrders()).Id);
        Assert.Equal(0, RequestsOn(range10.Id));

        RunTo(Data.Utc(2026, 3, 10, 7, 30));
        Tick(Data.Utc(2026, 3, 10, 7, 31), 2662.2, 2662.5);
        long range10Position = Assert.Single(_h.Broker.GetPositions()).Id;
        RunTo(Data.Utc(2026, 3, 10, 12, 0));
        _night = Restart(_nightJournal, _nightStore, _nightParameters, Data.Utc(2026, 3, 10, 12, 5));
        Assert.Empty(_night.LastReport!.PositionsAdopted);
        Assert.Empty(_night.LastReport.Strangers);
        Assert.Equal(range10Position, Assert.Single(_h.Broker.GetPositions()).Id);

        // The SessionRange close is refused at every bar and timer call from 20:30 to 22:15.
        RunTo(Data.Utc(2026, 3, 10, 20, 15));
        for (int i = 0; i < 16; i++)
        {
            _h.Broker.Script(BrokerCall.Close, BrokerFault.Rejected, "Busy");
        }

        RunTo(Data.Utc(2026, 3, 10, 22, 15));
        Assert.Equal(Range(10), OnlyPositionLabel());
        Assert.Empty(_h.Broker.GetPendingOrders());
        RunTo(Data.Utc(2026, 3, 10, 22, 30));
        Assert.Empty(_h.Broker.GetPositions());

        // ------------------------------------------------------------ trading day 2026-03-11
        RunTo(Data.Utc(2026, 3, 11, 7, 0));
        Assert.Equal(Range(11), Assert.Single(_h.Broker.GetPendingOrders()).Label);
        Tick(Data.Utc(2026, 3, 11, 7, 1), 2662.2, 2662.5);
        RunTo(Data.Utc(2026, 3, 11, 20, 30));
        Assert.Empty(_h.Broker.GetPositions());
        RunTo(Data.Utc(2026, 3, 11, 22, 15));
        PendingOrderInfo night12 = Assert.Single(_h.Broker.GetPendingOrders());
        Assert.Equal(Night(12), night12.Label);
        _range = Restart(_rangeJournal, _rangeStore, _rangeParameters, Data.Utc(2026, 3, 11, 22, 20));
        Assert.Empty(_range.LastReport!.OrdersCancelled);
        Assert.Empty(_range.LastReport.OrdersKept);
        Assert.Equal(night12.Id, Assert.Single(_h.Broker.GetPendingOrders()).Id);

        // ------------------------------------------------------------ trading day 2026-03-12: the Overnight loss
        Tick(Data.Utc(2026, 3, 11, 22, 25), 2650.2, 2650.5);
        RunTo(Data.Utc(2026, 3, 12, 0, 45));
        Tick(Data.Utc(2026, 3, 12, 0, 50), 2610.0, 2610.3);
        Assert.Empty(_h.Broker.GetPositions());
        RunTo(Data.Utc(2026, 3, 12, 22, 15));
        Assert.Empty(_h.Broker.GetPendingOrders());

        // ------------------------------------------------------------ 2026-03-13 (Friday) to 2026-03-17
        RunTo(Data.Utc(2026, 3, 13, 7, 0));
        Assert.Equal(Range(13), Assert.Single(_h.Broker.GetPendingOrders()).Label);
        Tick(Data.Utc(2026, 3, 13, 7, 1), 2662.2, 2662.5);
        RunTo(Data.Utc(2026, 3, 13, 20, 0));
        Assert.Empty(_h.Broker.GetPositions());
        RunTo(Data.Utc(2026, 3, 16, 7, 0));
        Assert.Equal(Range(16), Assert.Single(_h.Broker.GetPendingOrders()).Label);
        Tick(Data.Utc(2026, 3, 16, 7, 1), 2662.2, 2662.5);
        RunTo(Data.Utc(2026, 3, 16, 22, 15));
        Assert.Equal(Night(17), Assert.Single(_h.Broker.GetPendingOrders()).Label);
        Tick(Data.Utc(2026, 3, 16, 22, 16), 2650.2, 2650.5);
        RunTo(Data.Utc(2026, 3, 17, 12, 0));

        // ------------------------------------------------------------ what each instance did
        Assert.Equal(1, _mostPositions);
        Assert.Empty(_h.Broker.GetPositions());
        Assert.Empty(_h.Broker.GetPendingOrders());
        string[] placed = _h.Broker.Requests.Where(r => r.Call == BrokerCall.Place).Select(r => r.StopOrder!.Label).ToArray();
        Assert.Equal(new[] { Night(10), Range(10), Range(11), Night(12), Range(13), Range(16), Night(17) }, placed);
        Assert.Equal(0, _h.Count(BrokerCall.Cancel));
        Assert.Equal(0, _h.Count(BrokerCall.Modify));

        var byLabel = _h.Broker.History.ToDictionary(trade => trade.Label, trade => trade);
        Assert.Equal(7, byLabel.Count);
        Assert.Equal(TradeExitReason.StopLoss, byLabel[Night(12)].ExitReason);
        Assert.Equal(-1000.0, byLabel[Night(12)].NetProfit, 6);
        foreach (string label in new[] { Night(10), Range(10), Range(11), Range(13), Range(16), Night(17) })
        {
            Assert.Equal(TradeExitReason.ClosedByBot, byLabel[label].ExitReason);
        }

        long[] nightIds = _h.Broker.History.Where(t => t.Label.Contains("|ON-", StringComparison.Ordinal)).Select(t => t.PositionId).ToArray();
        long[] rangeIds = _h.Broker.History.Where(t => t.Label.Contains("|RH-", StringComparison.Ordinal)).Select(t => t.PositionId).ToArray();
        Assert.All(ActionIds(_nightJournal), id => Assert.Contains(id, nightIds));
        Assert.All(ActionIds(_rangeJournal), id => Assert.Contains(id, rangeIds));
        Assert.Contains(_nightJournal.Lines("action"), line => line.Contains(" duty=night-exit ", StringComparison.Ordinal));
        Assert.Contains(_rangeJournal.Lines("action"), line => line.Contains(" duty=range-exit ", StringComparison.Ordinal));
        Assert.Contains(_rangeJournal.Lines("action"), line => line.Contains(" duty=friday-close ", StringComparison.Ordinal));
        Assert.All(_h.Broker.Requests.Where(r => r.Call == BrokerCall.Close), request => Assert.True(nightIds.Contains(request.TargetId!.Value) || rangeIds.Contains(request.TargetId!.Value)));
        Assert.All(_nightJournal.Lines("order").Where(l => l.Contains(" event=placement ", StringComparison.Ordinal)), line => Assert.Contains("|ON-", line, StringComparison.Ordinal));
        Assert.All(_rangeJournal.Lines("order").Where(l => l.Contains(" event=placement ", StringComparison.Ordinal)), line => Assert.Contains("|RH-", line, StringComparison.Ordinal));

        // Fail closed on the overlap, and one daily budget for the account.
        Assert.Contains(_nightJournal.Lines("signal"), l => l.Contains(" zone=ON-20260311 ", StringComparison.Ordinal) && l.Contains(" skip=SymbolBusy", StringComparison.Ordinal));
        Assert.Contains(_rangeJournal.Lines("signal"), l => l.Contains(" zone=RH-20260312 ", StringComparison.Ordinal) && l.Contains(" skip=DailyBudgetExceeded", StringComparison.Ordinal));
        Assert.Contains(_nightJournal.Lines("signal"), l => l.Contains(" zone=ON-20260313 ", StringComparison.Ordinal) && l.Contains(" skip=DailyBudgetExceeded", StringComparison.Ordinal));
        Assert.Contains(_rangeJournal.Lines("skip"), l => l.Contains(" zone=RH-20260312 reason=DailyBudgetExceeded ", StringComparison.Ordinal) && l.Contains(" limit=1500.0000 ", StringComparison.Ordinal));
    }
}
