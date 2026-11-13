using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-011: every risk decision is a function of an explicit snapshot. The snapshot checks its
/// data on construction (fail closed) and puts every list into a fixed order, so the order in
/// which the broker returned the data cannot change a sum or a decision.
/// </summary>
public class RiskSnapshotTests
{
    private static readonly AccountSnapshot Account = RiskData.Account(100000.0);

    private static RiskSnapshot Build(
        IEnumerable<PositionInfo>? positions = null,
        IEnumerable<PendingOrderInfo>? orders = null,
        IEnumerable<ClosedTradeInfo>? history = null,
        IEnumerable<SymbolInfo>? symbols = null,
        AccountSnapshot? account = null,
        DateTime? serverTime = null,
        DateTime? historySince = null)
    {
        return new RiskSnapshot(
            serverTime ?? RiskData.Now,
            account ?? Account,
            positions ?? Array.Empty<PositionInfo>(),
            orders ?? Array.Empty<PendingOrderInfo>(),
            history ?? Array.Empty<ClosedTradeInfo>(),
            historySince ?? RiskData.Epoch,
            symbols ?? RiskData.AllSymbols);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Constructor_ValidData_KeepsTheValues()
    {
        PositionInfo position = RiskData.Position(5, "EURUSD", Side.Buy, 1000.0, 1.1, 1.098);
        PendingOrderInfo order = RiskData.Order(6, "GBPUSD", Side.Sell, 1000.0, 1.3, 1.302);
        ClosedTradeInfo closed = RiskData.Closed(4, "EURUSD", RiskData.Now.AddHours(-3.0), RiskData.Now.AddHours(-2.0), 10.0, 100000.0);

        RiskSnapshot snapshot = Build(new[] { position }, new[] { order }, new[] { closed });

        Assert.Equal(RiskData.Now, snapshot.ServerTimeUtc);
        Assert.Equal(RiskData.Epoch, snapshot.HistorySinceUtc);
        Assert.Same(Account, snapshot.Account);
        Assert.Same(position, Assert.Single(snapshot.Positions));
        Assert.Same(order, Assert.Single(snapshot.PendingOrders));
        Assert.Same(closed, Assert.Single(snapshot.History));
        Assert.Equal(4, snapshot.Symbols.Count);
        Assert.Equal("USDJPY", snapshot.FindSymbol("USDJPY")!.Name);
        Assert.Null(snapshot.FindSymbol("EURGBP"));
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Constructor_ListsInAnyOrder_AreStoredInAFixedOrder()
    {
        PositionInfo p1 = RiskData.Position(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.098);
        PositionInfo p2 = RiskData.Position(2, "GBPUSD", Side.Buy, 1000.0, 1.3, 1.298);
        PendingOrderInfo o1 = RiskData.Order(10, "EURUSD", Side.Buy, 1000.0, 1.1, 1.098);
        PendingOrderInfo o2 = RiskData.Order(11, "AUDUSD", Side.Buy, 1000.0, 0.65, 0.648);
        DateTime t = RiskData.Now.AddHours(-2.0);
        ClosedTradeInfo h1 = RiskData.Closed(7, "EURUSD", t.AddHours(-1.0), t, 0.1, 100000.1);
        ClosedTradeInfo h2 = RiskData.Closed(8, "EURUSD", t.AddHours(-1.0), t, 0.2, 100000.3);
        ClosedTradeInfo h3 = RiskData.Closed(3, "EURUSD", t.AddHours(-1.0), t.AddMinutes(1.0), 0.3, 100000.6);

        RiskSnapshot forward = Build(new[] { p1, p2 }, new[] { o1, o2 }, new[] { h1, h2, h3 }, new[] { RiskData.EurUsd, RiskData.UsdJpy });
        RiskSnapshot backward = Build(new[] { p2, p1 }, new[] { o2, o1 }, new[] { h3, h2, h1 }, new[] { RiskData.UsdJpy, RiskData.EurUsd });

        Assert.Equal(new long[] { 1, 2 }, backward.Positions.Select(p => p.Id).ToArray());
        Assert.Equal(new long[] { 10, 11 }, backward.PendingOrders.Select(o => o.Id).ToArray());
        // History by close time, then position id: h1 (7) and h2 (8) closed together, h3 later.
        Assert.Equal(new long[] { 7, 8, 3 }, backward.History.Select(h => h.PositionId).ToArray());
        Assert.Equal(new[] { "EURUSD", "USDJPY" }, backward.Symbols.Select(s => s.Name).ToArray());
        Assert.Equal(forward.History.ToArray(), backward.History.ToArray());
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Lists_CannotBeChangedThroughTheSnapshot()
    {
        var positions = new List<PositionInfo> { RiskData.Position(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.098) };
        RiskSnapshot snapshot = Build(positions);

        positions.Add(RiskData.Position(2, "GBPUSD", Side.Buy, 1000.0, 1.3, 1.298));

        Assert.Single(snapshot.Positions);
        Assert.False(snapshot.Positions is List<PositionInfo>);
    }

    [Theory]
    [Trait("Req", "RSK-011")]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Constructor_TimesThatAreNotUtc_Throw(DateTimeKind kind)
    {
        var notUtc = DateTime.SpecifyKind(RiskData.Now, kind);

        Assert.Throws<ArgumentException>(() => Build(serverTime: notUtc));
        Assert.Throws<ArgumentException>(() => Build(historySince: DateTime.SpecifyKind(RiskData.Epoch, kind)));
        Assert.Throws<ArgumentException>(() => Build(new[] { RiskData.Position(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.098, notUtc, RiskData.BotLabel("EURUSD")) }));
        Assert.Throws<ArgumentException>(() => Build(orders: new[] { RiskData.Order(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.098, notUtc, RiskData.BotLabel("EURUSD")) }));
        Assert.Throws<ArgumentException>(() => Build(history: new[] { RiskData.Closed(1, "EURUSD", notUtc, RiskData.Now, 0.0, 100000.0) }));
        Assert.Throws<ArgumentException>(() => Build(history: new[] { RiskData.Closed(1, "EURUSD", RiskData.Now, notUtc, 0.0, 100000.0) }));
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RiskSnapshot(RiskData.Now, null!, Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), Array.Empty<ClosedTradeInfo>(), RiskData.Epoch, RiskData.AllSymbols));
        Assert.Throws<ArgumentNullException>(() => new RiskSnapshot(RiskData.Now, Account, null!, Array.Empty<PendingOrderInfo>(), Array.Empty<ClosedTradeInfo>(), RiskData.Epoch, RiskData.AllSymbols));
        Assert.Throws<ArgumentNullException>(() => new RiskSnapshot(RiskData.Now, Account, Array.Empty<PositionInfo>(), null!, Array.Empty<ClosedTradeInfo>(), RiskData.Epoch, RiskData.AllSymbols));
        Assert.Throws<ArgumentNullException>(() => new RiskSnapshot(RiskData.Now, Account, Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), null!, RiskData.Epoch, RiskData.AllSymbols));
        Assert.Throws<ArgumentNullException>(() => new RiskSnapshot(RiskData.Now, Account, Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), Array.Empty<ClosedTradeInfo>(), RiskData.Epoch, null!));
        Assert.Throws<ArgumentException>(() => Build(new PositionInfo[] { null! }));
        Assert.Throws<ArgumentException>(() => Build(orders: new PendingOrderInfo[] { null! }));
        Assert.Throws<ArgumentException>(() => Build(history: new ClosedTradeInfo[] { null! }));
        Assert.Throws<ArgumentException>(() => Build(symbols: new SymbolInfo[] { null! }));
    }

    [Theory]
    [Trait("Req", "RSK-011")]
    [InlineData(double.NaN, 100000.0, 100000.0)]
    [InlineData(100000.0, double.PositiveInfinity, 100000.0)]
    [InlineData(100000.0, 100000.0, double.NegativeInfinity)]
    public void Constructor_AccountValueNotFinite_Throws(double balance, double equity, double freeMargin)
    {
        Assert.Throws<ArgumentException>(() => Build(account: new AccountSnapshot(balance, equity, freeMargin, "USD")));
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Constructor_AccountCurrencyNull_Throws()
    {
        Assert.Throws<ArgumentException>(() => Build(account: new AccountSnapshot(1.0, 1.0, 1.0, null!)));
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Constructor_InvalidPosition_Throws()
    {
        PositionInfo valid = RiskData.Position(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.098);

        Assert.Throws<ArgumentException>(() => Build(new[] { valid with { Symbol = null! } }));
        Assert.Throws<ArgumentException>(() => Build(new[] { valid with { Label = null! } }));
        Assert.Throws<ArgumentException>(() => Build(new[] { valid with { Comment = null! } }));
        Assert.Throws<ArgumentException>(() => Build(new[] { valid with { Side = (Side)9 } }));
        Assert.Throws<ArgumentException>(() => Build(new[] { valid with { VolumeUnits = 0.0 } }));
        Assert.Throws<ArgumentException>(() => Build(new[] { valid with { EntryPrice = double.NaN } }));
        Assert.Throws<ArgumentException>(() => Build(new[] { valid with { StopLoss = -1.0 } }));
        Assert.Throws<ArgumentException>(() => Build(new[] { valid with { TakeProfit = double.NaN } }));
        Assert.Throws<ArgumentException>(() => Build(new[] { valid, valid with { Symbol = "GBPUSD" } }));
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Constructor_InvalidPendingOrder_Throws()
    {
        PendingOrderInfo valid = RiskData.Order(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.098);

        Assert.Throws<ArgumentException>(() => Build(orders: new[] { valid with { Symbol = null! } }));
        Assert.Throws<ArgumentException>(() => Build(orders: new[] { valid with { Label = null! } }));
        Assert.Throws<ArgumentException>(() => Build(orders: new[] { valid with { Comment = null! } }));
        Assert.Throws<ArgumentException>(() => Build(orders: new[] { valid with { Side = (Side)9 } }));
        Assert.Throws<ArgumentException>(() => Build(orders: new[] { valid with { VolumeUnits = double.PositiveInfinity } }));
        Assert.Throws<ArgumentException>(() => Build(orders: new[] { valid with { EntryPrice = 0.0 } }));
        Assert.Throws<ArgumentException>(() => Build(orders: new[] { valid with { StopLoss = double.NaN } }));
        Assert.Throws<ArgumentException>(() => Build(orders: new[] { valid with { TakeProfit = 0.0 } }));
        Assert.Throws<ArgumentException>(() => Build(orders: new[] { valid, valid }));
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Constructor_InvalidClosedTrade_Throws()
    {
        ClosedTradeInfo valid = RiskData.Closed(1, "EURUSD", RiskData.Now.AddHours(-2.0), RiskData.Now.AddHours(-1.0), 5.0, 100005.0);

        Assert.Throws<ArgumentException>(() => Build(history: new[] { valid with { Symbol = null! } }));
        Assert.Throws<ArgumentException>(() => Build(history: new[] { valid with { Label = null! } }));
        Assert.Throws<ArgumentException>(() => Build(history: new[] { valid with { NetProfit = double.NaN } }));
        Assert.Throws<ArgumentException>(() => Build(history: new[] { valid with { Commission = double.NaN } }));
        Assert.Throws<ArgumentException>(() => Build(history: new[] { valid with { Swap = double.PositiveInfinity } }));
        Assert.Throws<ArgumentException>(() => Build(history: new[] { valid with { BalanceAfterClose = double.NaN } }));
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Constructor_SymbolDataProblems_Throw()
    {
        Assert.Throws<ArgumentException>(() => Build(symbols: new[] { RiskData.EurUsd with { Name = null! } }));
        Assert.Throws<ArgumentException>(() => Build(symbols: new[] { RiskData.EurUsd, RiskData.EurUsd with { PipSize = 0.01 } }));
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Constructor_PartialClosesOfOnePosition_AreAllKept()
    {
        // Two history rows may share a position id (partial closes); positions and orders may not.
        ClosedTradeInfo first = RiskData.Closed(1, "EURUSD", RiskData.Now.AddHours(-3.0), RiskData.Now.AddHours(-2.0), 5.0, 100005.0);
        ClosedTradeInfo second = RiskData.Closed(1, "EURUSD", RiskData.Now.AddHours(-3.0), RiskData.Now.AddHours(-1.0), 5.0, 100010.0);

        RiskSnapshot snapshot = Build(history: new[] { second, first });

        Assert.Equal(2, snapshot.History.Count);
        Assert.Same(first, snapshot.History[0]);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void Constructor_HistoryRowsWithEqualTimeAndId_AreOrderedByTheirValues()
    {
        DateTime close = RiskData.Now.AddHours(-1.0);
        DateTime entry = RiskData.Now.AddHours(-2.0);
        ClosedTradeInfo a = new(1, "EURUSD", "x", entry, close, 1.0, 0.0, 0.0, 100.0);
        ClosedTradeInfo b = a with { NetProfit = 2.0 };
        ClosedTradeInfo c = a with { BalanceAfterClose = 99.0 };
        ClosedTradeInfo d = a with { Commission = -1.0 };
        ClosedTradeInfo e = a with { Swap = -1.0 };
        ClosedTradeInfo f = a with { EntryTimeUtc = entry.AddMinutes(-1.0) };
        ClosedTradeInfo g = a with { Symbol = "AUDUSD" };
        ClosedTradeInfo h = a with { Label = "a" };

        ClosedTradeInfo[] input = { b, a, c, d, e, f, g, h };
        RiskSnapshot one = Build(history: input);
        RiskSnapshot two = Build(history: input.Reverse());

        Assert.Equal(one.History.ToArray(), two.History.ToArray());
        Assert.Same(b, one.History[^1]);
    }
}
