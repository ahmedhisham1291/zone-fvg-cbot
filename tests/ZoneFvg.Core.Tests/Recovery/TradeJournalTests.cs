using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>Records every trade event reported by a <see cref="TradeJournal"/>.</summary>
public sealed class RecordingTradeEvents : ITradeEvents
{
    public List<PositionInfo> Opened { get; } = new();

    public List<(ClosedTradeInfo Trade, OpenedTradePlan? Plan)> Closed { get; } = new();

    void ITradeEvents.Opened(PositionInfo position, DateTime atUtc)
    {
        Opened.Add(position);
    }

    void ITradeEvents.Closed(ClosedTradeInfo trade, OpenedTradePlan? plan, DateTime atUtc)
    {
        Closed.Add((trade, plan));
    }
}

/// <summary>
/// REC-008: bot trades of the symbol that opened or closed are reported from broker history
/// exactly once, keyed by position id, also across new instances that share the store. The plan of
/// an open position is kept until it closes, so the closing can be journaled in R (OPS-002). Now is
/// Wednesday 2026-03-04 10:00 UTC; history is read from the epoch, 2025-12-31 23:00 UTC.
/// </summary>
public class TradeJournalTests
{
    private static readonly DateTime Now = RecoveryData.At(4, 10, 0);

    private readonly FakeBroker _broker = BotHarness.NewBroker(Now);
    private readonly MemoryStore _store = new();
    private readonly RecordingTradeEvents _events = new();

    private TradeJournal NewJournal()
    {
        return new TradeJournal(RecoveryData.Symbol, RiskData.Parameters, _broker, _store, _events);
    }

    private static ClosedTradeInfo Closed(long positionId, DateTime closeUtc, double net, string symbol = RecoveryData.Symbol, string label = RecoveryData.Label)
    {
        return new ClosedTradeInfo(positionId, symbol, label, RecoveryData.At(3, 9, 0), closeUtc, net, -2.0, -1.0, 100000.0 + net);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_OpenPosition_IsReportedOnceAndItsPlanKept()
    {
        long id = _broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());

        TradeJournalResult first = NewJournal().Record(Now);
        TradeJournalResult second = NewJournal().Record(Now.AddMinutes(5.0));

        Assert.Equal(1, first.Written);
        Assert.Equal(1, first.Known);
        Assert.Equal(0, second.Written);
        Assert.Equal(id, Assert.Single(_events.Opened).Id);
        Assert.Empty(_events.Closed);
        Assert.Equal("o" + id, _store.Values[TradeJournal.StoreKey]);
        Assert.Equal("Buy\n100.675\n" + RecoveryData.Comment(), _store.Values[TradeJournal.PlanKeyPrefix + id]);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_TradeClosedWhileAway_IsReportedOnceAcrossRestartsWithoutAPlan()
    {
        _broker.AddClosedTrade(Closed(1001, RecoveryData.At(4, 9, 30), 978.25));

        NewJournal().Record(Now);
        NewJournal().Record(Now.AddMinutes(1.0));
        NewJournal().Record(Now.AddMinutes(2.0));

        (ClosedTradeInfo trade, OpenedTradePlan? plan) = Assert.Single(_events.Closed);
        Assert.Equal(1001, trade.PositionId);
        Assert.Equal(978.25, trade.NetProfit);
        Assert.Null(plan);
        Assert.Equal("c1001,o1001", _store.Values[TradeJournal.StoreKey]);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_PositionOpenedThenClosedAfterARestart_IsReportedOnceEachWithThePlan()
    {
        long id = _broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());
        NewJournal().Record(Now);

        _broker.Tick(RecoveryData.Symbol, 103.07, 103.08);
        NewJournal().Record(Now);
        NewJournal().Record(Now);

        Assert.Single(_events.Opened);
        (ClosedTradeInfo trade, OpenedTradePlan? plan) = Assert.Single(_events.Closed);
        Assert.Equal(id, trade.PositionId);
        Assert.Equal(new OpenedTradePlan(Side.Buy, 100.675, RecoveryData.Comment()), plan);
        Assert.Equal(string.Empty, _store.Values[TradeJournal.PlanKeyPrefix + id]);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_PartialClosesOfOnePosition_AreSummedIntoOneClosing()
    {
        _broker.AddClosedTrade(Closed(1005, RecoveryData.At(4, 9, 0), 100.0));
        _broker.AddClosedTrade(Closed(1005, RecoveryData.At(4, 9, 15), 50.5));

        NewJournal().Record(Now);

        ClosedTradeInfo trade = Assert.Single(_events.Closed).Trade;
        Assert.Equal(150.5, trade.NetProfit);
        Assert.Equal(-4.0, trade.Commission);
        Assert.Equal(-2.0, trade.Swap);
        Assert.Equal(100050.5, trade.BalanceAfterClose);
        Assert.Equal(RecoveryData.At(4, 9, 15), trade.CloseTimeUtc);
        Assert.Equal(RecoveryData.At(3, 9, 0), trade.EntryTimeUtc);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_PartialCloseOfAnOpenPosition_WaitsUntilItIsClosed()
    {
        long id = _broker.AddPosition(RecoveryData.Symbol, Side.Buy, 20000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());
        _broker.AddClosedTrade(Closed(id, RecoveryData.At(4, 9, 0), 100.0));

        NewJournal().Record(Now);

        Assert.Single(_events.Opened);
        Assert.Empty(_events.Closed);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_OtherSymbolsAndManualTrades_AreIgnored()
    {
        _broker.AddClosedTrade(Closed(2001, RecoveryData.At(4, 9, 0), 10.0, "EURUSD", "ZFVG|v1|EURUSD|D-1"));
        _broker.AddClosedTrade(Closed(2002, RecoveryData.At(4, 9, 0), 10.0, RecoveryData.Symbol, "manual"));
        _broker.AddPosition(RecoveryData.Symbol, Side.Sell, 1000.0, 101.0, 102.0, 99.0, "manual", "x");

        TradeJournalResult result = NewJournal().Record(Now);

        Assert.Equal(0, result.Written);
        Assert.Equal(0, result.Known);
        Assert.Empty(_events.Opened);
        Assert.Empty(_events.Closed);
        Assert.False(_store.Values.ContainsKey(TradeJournal.StoreKey));
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_TradesBeforeTheHistoryWindow_AreNotReportedAndOldKeysArePruned()
    {
        // Closed 2025-12-31 22:00 UTC, before the epoch start 23:00 UTC; the stored key 1003 belongs to no open or recent trade.
        _broker.AddClosedTrade(Closed(1002, new DateTime(2025, 12, 31, 22, 0, 0, DateTimeKind.Utc), 10.0));
        _store.Values[TradeJournal.StoreKey] = "c1003,o1003";

        TradeJournalResult result = NewJournal().Record(Now);

        Assert.Equal(0, result.Known);
        Assert.Empty(_events.Closed);
        Assert.Equal(string.Empty, _store.Values[TradeJournal.StoreKey]);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_UnreadableStoredEntriesOrPlan_AreIgnoredAndReturned()
    {
        _broker.AddClosedTrade(Closed(1001, RecoveryData.At(4, 9, 30), 5.0));
        _broker.AddClosedTrade(Closed(1007, RecoveryData.At(4, 9, 30), 5.0));
        _store.Values[TradeJournal.StoreKey] = "x1001,c,c1001";
        _store.Values[TradeJournal.PlanKeyPrefix + "1007"] = "Up\n1.0\nx";

        TradeJournalResult result = NewJournal().Record(Now);

        Assert.Equal(new[] { "x1001", "c" }, result.Ignored);
        (ClosedTradeInfo trade, OpenedTradePlan? plan) = Assert.Single(_events.Closed);
        Assert.Equal(1007, trade.PositionId);
        Assert.Null(plan);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => new TradeJournal(string.Empty, RiskData.Parameters, _broker, _store, _events));
        Assert.Throws<ArgumentNullException>(() => new TradeJournal(RecoveryData.Symbol, null!, _broker, _store, _events));
        Assert.Throws<ArgumentNullException>(() => new TradeJournal(RecoveryData.Symbol, RiskData.Parameters, null!, _store, _events));
        Assert.Throws<ArgumentNullException>(() => new TradeJournal(RecoveryData.Symbol, RiskData.Parameters, _broker, null!, _events));
        Assert.Throws<ArgumentNullException>(() => new TradeJournal(RecoveryData.Symbol, RiskData.Parameters, _broker, _store, null!));
    }

    // ---------------------------------------------------------------- SIM-001 closing data of summed parts

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Record_PartialClosesWithVolumeAndPrice_SumVolumeAndAverageThePriceByVolume()
    {
        _broker.AddClosedTrade(Closed(1008, RecoveryData.At(4, 9, 0), 100.0) with { VolumeUnits = 30000.0, ClosingPrice = 101.0, ExitReason = TradeExitReason.ClosedByBot });
        _broker.AddClosedTrade(Closed(1008, RecoveryData.At(4, 9, 15), 50.5) with { VolumeUnits = 10000.0, ClosingPrice = 103.0, ExitReason = TradeExitReason.TakeProfit });

        NewJournal().Record(Now);

        ClosedTradeInfo trade = Assert.Single(_events.Closed).Trade;
        Assert.Equal(40000.0, trade.VolumeUnits);
        Assert.Equal(101.5, trade.ClosingPrice!.Value, 9);
        Assert.Equal(TradeExitReason.TakeProfit, trade.ExitReason);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Record_OneClosingDeal_KeepsItsOwnPriceVolumeAndReason()
    {
        _broker.AddClosedTrade(Closed(1009, RecoveryData.At(4, 9, 0), 100.0) with { VolumeUnits = 41000.0, ClosingPrice = 103.061, ExitReason = TradeExitReason.StopOut });

        NewJournal().Record(Now);

        ClosedTradeInfo trade = Assert.Single(_events.Closed).Trade;
        Assert.Equal(41000.0, trade.VolumeUnits);
        Assert.Equal(103.061, trade.ClosingPrice);
        Assert.Equal(TradeExitReason.StopOut, trade.ExitReason);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData(false, true, 10000.0)]
    [InlineData(true, false, 10000.0)]
    [InlineData(true, true, 0.0)]
    public void Record_PartWithoutVolumeOrPrice_GivesNoAveragePrice(bool secondHasVolume, bool secondHasPrice, double volume)
    {
        _broker.AddClosedTrade(Closed(1010, RecoveryData.At(4, 9, 0), 100.0) with { VolumeUnits = volume == 0.0 ? 0.0 : 30000.0, ClosingPrice = 101.0 });
        _broker.AddClosedTrade(Closed(1010, RecoveryData.At(4, 9, 15), 50.5) with
        {
            VolumeUnits = secondHasVolume ? volume : null,
            ClosingPrice = secondHasPrice ? 103.0 : null,
        });

        NewJournal().Record(Now);

        ClosedTradeInfo trade = Assert.Single(_events.Closed).Trade;
        Assert.Null(trade.ClosingPrice);
        double? expectedVolume = secondHasVolume ? (volume == 0.0 ? 0.0 : 30000.0 + volume) : null;
        Assert.Equal(expectedVolume, trade.VolumeUnits);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Record_ClosedTradeWithoutClosingData_ReportsUnknownReasonAndNoVolumeOrPrice()
    {
        _broker.AddClosedTrade(Closed(1011, RecoveryData.At(4, 9, 0), 100.0));

        NewJournal().Record(Now);

        ClosedTradeInfo trade = Assert.Single(_events.Closed).Trade;
        Assert.Null(trade.VolumeUnits);
        Assert.Null(trade.ClosingPrice);
        Assert.Equal(TradeExitReason.Unknown, trade.ExitReason);
    }
}
