using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-008: bot trades of the symbol that opened or closed are added to the journal from broker
/// history exactly once, keyed by position id, also across new instances that share the store.
/// Now is Wednesday 2026-03-04 10:00 UTC; history is read from the epoch, 2025-12-31 23:00 UTC.
/// </summary>
public class TradeJournalTests
{
    private static readonly DateTime Now = RecoveryData.At(4, 10, 0);

    private readonly FakeBroker _broker = BotHarness.NewBroker(Now);
    private readonly MemoryStore _store = new();
    private readonly RecordingJournal _sink = new();

    private TradeJournal NewJournal()
    {
        return new TradeJournal(RecoveryData.Symbol, RiskData.Parameters, _broker, _store, _sink);
    }

    private static ClosedTradeInfo Closed(long positionId, DateTime closeUtc, double net, string symbol = RecoveryData.Symbol, string label = RecoveryData.Label)
    {
        return new ClosedTradeInfo(positionId, symbol, label, RecoveryData.At(3, 9, 0), closeUtc, net, -2.0, -1.0, 100000.0 + net);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_OpenPosition_WritesOneOpenedLineOnlyOnce()
    {
        long id = _broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());

        TradeJournalResult first = NewJournal().Record(Now);
        TradeJournalResult second = NewJournal().Record(Now.AddMinutes(5.0));

        Assert.Equal(new TradeJournalResult(1, 1), first);
        Assert.Equal(new TradeJournalResult(0, 1), second);
        string line = Assert.Single(_sink.Lines(TradeJournal.Category));
        Assert.Equal(
            "event=opened symbol=USDJPY position=" + id + " label=" + RecoveryData.Label + " side=Buy volume=41000 entry=100.675 stop=99.482 target=103.061 opened=2026-03-04T10:00:00Z comment=" + RecoveryData.Comment(),
            line);
        Assert.Equal("o" + id, _store.Values[TradeJournal.StoreKey]);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_TradeClosedWhileAway_WritesOneClosedLineAcrossRestarts()
    {
        _broker.AddClosedTrade(Closed(1001, RecoveryData.At(4, 9, 30), 978.25));

        NewJournal().Record(Now);
        NewJournal().Record(Now.AddMinutes(1.0));
        NewJournal().Record(Now.AddMinutes(2.0));

        string line = Assert.Single(_sink.Lines(TradeJournal.Category));
        Assert.Equal(
            "event=closed symbol=USDJPY position=1001 label=" + RecoveryData.Label + " opened=2026-03-03T09:00:00Z closed=2026-03-04T09:30:00Z net=978.25 commission=-2 swap=-1 balance=100978.25 parts=1",
            line);
        Assert.Equal("c1001,o1001", _store.Values[TradeJournal.StoreKey]);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_PositionOpenedThenClosed_WritesOneLineForEachEvent()
    {
        long id = _broker.AddPosition(RecoveryData.Symbol, Side.Buy, 41000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());
        NewJournal().Record(Now);

        _broker.Tick(RecoveryData.Symbol, 103.07, 103.08);
        NewJournal().Record(Now);
        NewJournal().Record(Now);

        Assert.Equal(1, _sink.Count(TradeJournal.Category, "event=opened"));
        Assert.Equal(1, _sink.Count(TradeJournal.Category, "event=closed symbol=USDJPY position=" + id));
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_PartialClosesOfOnePosition_AreSummedIntoOneLine()
    {
        _broker.AddClosedTrade(Closed(1005, RecoveryData.At(4, 9, 0), 100.0));
        _broker.AddClosedTrade(Closed(1005, RecoveryData.At(4, 9, 15), 50.5));

        NewJournal().Record(Now);

        string line = Assert.Single(_sink.Lines(TradeJournal.Category));
        Assert.Contains(" closed=2026-03-04T09:15:00Z net=150.5 commission=-4 swap=-2 balance=100050.5 parts=2", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_PartialCloseOfAnOpenPosition_WaitsUntilItIsClosed()
    {
        long id = _broker.AddPosition(RecoveryData.Symbol, Side.Buy, 20000.0, 100.675, 99.482, 103.061, RecoveryData.Label, RecoveryData.Comment());
        _broker.AddClosedTrade(Closed(id, RecoveryData.At(4, 9, 0), 100.0));

        NewJournal().Record(Now);

        Assert.Equal(1, _sink.Count(TradeJournal.Category, "event=opened"));
        Assert.Equal(0, _sink.Count(TradeJournal.Category, "event=closed"));
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_OtherSymbolsAndManualTrades_AreIgnored()
    {
        _broker.AddClosedTrade(Closed(2001, RecoveryData.At(4, 9, 0), 10.0, "EURUSD", "ZFVG|v1|EURUSD|D-1"));
        _broker.AddClosedTrade(Closed(2002, RecoveryData.At(4, 9, 0), 10.0, RecoveryData.Symbol, "manual"));
        _broker.AddPosition(RecoveryData.Symbol, Side.Sell, 1000.0, 101.0, 102.0, 99.0, "manual", "x");

        TradeJournalResult result = NewJournal().Record(Now);

        Assert.Equal(new TradeJournalResult(0, 0), result);
        Assert.Empty(_sink.Entries);
        Assert.False(_store.Values.ContainsKey(TradeJournal.StoreKey));
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_TradesBeforeTheHistoryWindow_AreNotJournaledAndOldKeysArePruned()
    {
        // Closed 2025-12-31 22:00 UTC, before the epoch start 23:00 UTC; the stored key 1003 belongs to no open or recent trade.
        _broker.AddClosedTrade(Closed(1002, new DateTime(2025, 12, 31, 22, 0, 0, DateTimeKind.Utc), 10.0));
        _store.Values[TradeJournal.StoreKey] = "c1003,o1003";

        TradeJournalResult result = NewJournal().Record(Now);

        Assert.Equal(new TradeJournalResult(0, 0), result);
        Assert.Equal(string.Empty, _store.Values[TradeJournal.StoreKey]);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Record_UnreadableStoredEntries_AreIgnoredAndReported()
    {
        _broker.AddClosedTrade(Closed(1001, RecoveryData.At(4, 9, 30), 5.0));
        _store.Values[TradeJournal.StoreKey] = "x1001,c,c1001";

        TradeJournalResult result = NewJournal().Record(Now);

        Assert.Equal(new TradeJournalResult(0, 2), result);
        Assert.Equal(1, _sink.Count(TradeJournal.Category, "event=store-entry-ignored key=journal.trades entry=x1001"));
        Assert.Equal(1, _sink.Count(TradeJournal.Category, "event=store-entry-ignored key=journal.trades entry=c"));
        Assert.Equal(0, _sink.Count(TradeJournal.Category, "event=closed"));
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Constructor_InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentException>(() => new TradeJournal(string.Empty, RiskData.Parameters, _broker, _store, _sink));
        Assert.Throws<ArgumentNullException>(() => new TradeJournal(RecoveryData.Symbol, null!, _broker, _store, _sink));
        Assert.Throws<ArgumentNullException>(() => new TradeJournal(RecoveryData.Symbol, RiskData.Parameters, null!, _store, _sink));
        Assert.Throws<ArgumentNullException>(() => new TradeJournal(RecoveryData.Symbol, RiskData.Parameters, _broker, null!, _sink));
        Assert.Throws<ArgumentNullException>(() => new TradeJournal(RecoveryData.Symbol, RiskData.Parameters, _broker, _store, null!));
    }
}
