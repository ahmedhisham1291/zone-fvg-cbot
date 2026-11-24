using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;
using D = ZoneFvg.Core.Tests.RecoverySizingData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-016 and RSK-017 on the pure sizer: R = min((L + RR x F) / RR, B x 1.28 / 100), F = 100,
/// RR = 2.0, L the losing run of the symbol from its closed trades in the order close time,
/// placement time, label. Money compared to 1e-6 unless "bit for bit".
/// </summary>
public class RecoverySizerTests
{
    private const double RewardRisk = 2.0;

    private static readonly RecoverySizer Sizer = new(D.Recovery);

    private static RecoveryAmount Next(IReadOnlyList<LadderEntry> entries, double balance = D.Capital, double rewardRisk = RewardRisk, DateTime? at = null)
    {
        return Sizer.Next(entries, at ?? ExecutionData.Now, balance, rewardRisk);
    }

    private static List<LadderEntry> Losses(params double[] nets)
    {
        return nets.Select((net, i) => D.Nth(i + 1, net)).ToList();
    }

    // ------------------------------------------------------------------ RSK-016 amount

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_OwnersSequenceOfTenLossesFrom200000_RawCapFlagAndRiskOfEachTrade()
    {
        double[] expectedRaw = { 100.0, 150.0, 225.0, 337.5, 506.25, 759.375, 1139.0625, 1708.59375, 2562.890625, 3811.365625 };
        double[] expectedRisk = { 100.0, 150.0, 225.0, 337.5, 506.25, 759.375, 1139.0625, 1708.59375, 2496.95, 2464.98904 };
        var trades = new List<LadderEntry>();
        double balance = D.Capital;
        for (int n = 1; n <= 10; n++)
        {
            RecoveryAmount amount = Next(trades, balance, at: D.Placed(n));

            Assert.Equal(expectedRaw[n - 1], amount.RawAmount, 6);
            Assert.Equal(expectedRisk[n - 1], amount.RiskMoney, 6);
            Assert.Equal(n >= 9, amount.Capped);
            trades.Add(D.Nth(n, -amount.RiskMoney));
            balance -= amount.RiskMoney;
        }

        // Before the 9th trade the balance is 195,074.21875 (cap 2,496.95); before the 10th 192,577.26875.
        Assert.Equal(190112.27971, balance, 5);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_LongRunAt200000_IsCappedAt2560()
    {
        RecoveryAmount amount = Next(Losses(-10000.0));

        Assert.Equal(5100.0, amount.RawAmount, 6);
        Assert.Equal(2560.0, amount.CapAmount, 6);
        Assert.Equal(2560.0, amount.RiskMoney, 6);
        Assert.True(amount.Capped);
    }

    [Theory]
    [Trait("Req", "RSK-016")]
    [InlineData(-4920.0, 2560.0, 2560.0, true)]
    [InlineData(-4919.98, 2559.99, 2559.99, false)]
    [InlineData(-4920.02, 2560.01, 2560.0, true)]
    public void Next_RawAmountAroundTheCap_ExactlyAtTheCapIsTheCap(double loss, double raw, double risk, bool capped)
    {
        RecoveryAmount amount = Next(Losses(loss));

        Assert.Equal(raw, amount.RawAmount, 6);
        Assert.Equal(risk, amount.RiskMoney, 6);
        Assert.Equal(capped, amount.Capped);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_AfterAWin_StartsAgainAt100()
    {
        List<LadderEntry> trades = Losses(-100.0, -150.0, -225.0, 675.0);

        Assert.Equal(100.0, Next(trades).RiskMoney, 6);
        trades.Add(D.Nth(5, -100.0));
        Assert.Equal(150.0, Next(trades).RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_WinAfterACappedRun_StartsAgainAt100()
    {
        var trades = new List<LadderEntry>();
        double balance = D.Capital;
        for (int n = 1; n <= 9; n++)
        {
            RecoveryAmount amount = Next(trades, balance, at: D.Placed(n));
            trades.Add(D.Nth(n, -amount.RiskMoney));
            balance -= amount.RiskMoney;
        }

        Assert.True(Next(trades, balance).Capped);
        trades.Add(D.Nth(10, 2.0 * 2464.98904));

        RecoveryAmount next = Next(trades, balance);
        Assert.Equal(100.0, next.RiskMoney, 6);
        Assert.Equal(0.0, next.LosingRun);
        Assert.False(next.Capped);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_FourthTradeOfARun_A2RWinReturnsTheRunPlusTwoFirstAmounts()
    {
        RecoveryAmount fourth = Next(Losses(-100.0, -150.0, -225.0));

        Assert.Equal(475.0, fourth.LosingRun, 6);
        Assert.Equal(337.5, fourth.RiskMoney, 6);
        Assert.Equal(475.0 + (2.0 * 100.0), 2.0 * fourth.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_NoClosedTradeSinceTheEpoch_Risks100()
    {
        RecoveryAmount amount = Next(Array.Empty<LadderEntry>());

        Assert.Equal(100.0, amount.RiskMoney, 6);
        Assert.Equal(0.0, amount.LosingRun);
        Assert.False(amount.Capped);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_CapSmallerThanTheFirstAmount_RisksTheCap()
    {
        RecoveryAmount amount = Next(Array.Empty<LadderEntry>(), 5000.0);

        Assert.Equal(64.0, amount.RiskMoney, 6);
        Assert.True(amount.Capped);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_RewardRisk3AfterOneLoss_DividesBy3()
    {
        RecoveryAmount amount = Next(Losses(-100.0), rewardRisk: 3.0);

        Assert.Equal(133.333333, amount.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_OtherFirstAmountAndCap_AreTheParameters()
    {
        var sizer = new RecoverySizer(D.Recovery with { RecoveryFirstRiskUsd = 250.0, RecoveryMaxRiskPercent = 0.2 });

        Assert.Equal(250.0, sizer.Next(Array.Empty<LadderEntry>(), ExecutionData.Now, D.Capital, 2.0).RiskMoney, 6);
        Assert.Equal(400.0, sizer.Next(Losses(-1000.0), ExecutionData.Now, D.Capital, 2.0).RiskMoney, 6);
    }

    [Theory]
    [Trait("Req", "RSK-016")]
    [InlineData(0.0, 2.0)]
    [InlineData(-1.0, 2.0)]
    [InlineData(double.NaN, 2.0)]
    [InlineData(double.PositiveInfinity, 2.0)]
    [InlineData(D.Capital, 0.0)]
    [InlineData(D.Capital, double.NaN)]
    public void Next_BalanceOrRewardRiskNotUsable_FailsClosed(double balance, double rewardRisk)
    {
        var exception = Assert.Throws<ArgumentException>(() => Next(Array.Empty<LadderEntry>(), balance, rewardRisk));

        Assert.StartsWith("Recovery sizing (RSK-017): ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void FirstAmount_IsRecoveryFirstRiskUsd_WithOrWithoutWins()
    {
        var sizer = new RecoverySizer(D.Recovery with { RecoveryFirstRiskUsd = 125.0 });

        Assert.Equal(125.0, sizer.FirstAmount(Array.Empty<LadderEntry>(), ExecutionData.Now));
        Assert.Equal(125.0, sizer.FirstAmount(Losses(-100.0, 400.0, 300.0), ExecutionData.Now));
        Assert.Equal(125.0, sizer.Next(Losses(-100.0, 400.0), ExecutionData.Now, D.Capital, 2.0).RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void FirstAmount_InvalidTrades_FailClosed()
    {
        Assert.Throws<ArgumentNullException>(() => Sizer.FirstAmount(null!, ExecutionData.Now));
        Assert.Throws<ArgumentException>(() => Sizer.FirstAmount(Losses(-100.0), D.ClosedAt(1).AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Sizer.FirstAmount(Losses(double.NaN), ExecutionData.Now));
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Constructor_InvalidSizingValues_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RecoverySizer(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecoverySizer(D.Recovery with { RecoveryFirstRiskUsd = 0.0 }));
    }

    // ------------------------------------------------------------------ RSK-017 losing run

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_ActualNetResultsWithCosts_AreTheRun()
    {
        // Net -104.20 (gross -100, commission -4.20) and -157.35 (gross -150, commission -5.10, swap -2.25).
        var history = new[]
        {
            D.Trade(1, "EURUSD", "A", D.Placed(1), D.ClosedAt(1), -104.2, -4.2),
            D.Trade(2, "EURUSD", "B", D.Placed(2), D.ClosedAt(2), -157.35, -5.1, -2.25),
        };

        RecoveryAmount amount = Next(FromBroker("EURUSD", history));

        Assert.Equal(261.55, amount.LosingRun, 6);
        Assert.Equal(230.775, amount.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_BreakEvenContinuesTheRun()
    {
        List<LadderEntry> trades = Losses(-100.0, 0.0);
        Assert.Equal(150.0, Next(trades).RiskMoney, 6);

        trades.Add(D.Nth(3, -150.0));
        Assert.Equal(225.0, Next(trades).RiskMoney, 6);

        RecoveryAmount withCommission = Next(Losses(-100.0, -3.5));
        Assert.Equal(103.5, withCommission.LosingRun, 6);
        Assert.Equal(151.75, withCommission.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_WinThreshold_OneCentIsAWin_OneTenthOfANanoIsNot()
    {
        Assert.Equal(100.0, Next(Losses(-100.0, 0.01)).RiskMoney, 6);

        RecoveryAmount tiny = Next(Losses(-100.0, 1e-10));
        Assert.Equal(100.0 + 1e-10, tiny.LosingRun);
        Assert.Equal((100.0 + 1e-10 + 200.0) / 2.0, tiny.RiskMoney);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_EachPairOnItsOwn_OtherSymbolsNeverChangeTheAmount()
    {
        List<ClosedTradeInfo> history = PerPairHistory();

        double eur = Next(FromBroker("EURUSD", history)).RiskMoney;
        double gbp = Next(FromBroker("GBPUSD", history)).RiskMoney;
        double jpy = Next(FromBroker("USDJPY", history)).RiskMoney;

        Assert.Equal(225.0, eur, 6);
        Assert.Equal(100.0, gbp, 6);
        Assert.Equal(100.0, jpy, 6);

        // Other symbols' closed trades, open positions and pending orders added or removed: bit for bit the same.
        var more = new List<ClosedTradeInfo>(history)
        {
            D.Trade(20, "AUDUSD", "X", D.Placed(5), D.ClosedAt(5), -900.0),
            D.Trade(21, "USDJPY", "Y", D.Placed(6), D.ClosedAt(6), 450.0),
        };
        var positions = new[] { RiskData.Position(30, "AUDUSD", Side.Buy, 10000.0, 1.1, 1.098) };
        var orders = new[] { RiskData.Order(31, "GBPUSD", Side.Sell, 10000.0, 1.3, 1.302) };
        Assert.Equal(eur, Next(FromBroker("EURUSD", more, positions, orders)).RiskMoney);
        Assert.Equal(gbp, Next(FromBroker("GBPUSD", history.Where(t => t.Symbol == "GBPUSD").ToList())).RiskMoney);
        Assert.Equal(eur, Next(FromBroker("EURUSD", history.Where(t => t.Symbol == "EURUSD").ToList())).RiskMoney);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Ladder_SameBrokerData_StaysAccountWide()
    {
        // The ladder counts the entries of every symbol: four closed entries of two symbols, the last a win.
        List<ClosedTradeInfo> history = PerPairHistory();
        var ladder = new LadderSizer(D.Defaults);
        IReadOnlyList<LadderEntry> all = LadderSizer.EntriesFromBroker(Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart);
        IReadOnlyList<LadderEntry> eurOnly = LadderSizer.EntriesFromBroker(Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history.Where(t => t.Symbol == "EURUSD"), RiskData.EpochStart);

        Assert.Equal(4, all.Count);
        Assert.Equal(ladder.NextRiskPercent(all, ExecutionData.Now), new LadderSizer(D.Recovery).NextRiskPercent(all, ExecutionData.Now));
        Assert.NotEqual(ladder.NextRiskPercent(all, ExecutionData.Now), ladder.NextRiskPercent(eurOnly, ExecutionData.Now));
    }

    [Theory]
    [Trait("Req", "RSK-017")]
    [InlineData("10:00", 100.0)]
    [InlineData("12:00", 175.0)]
    public void Next_WinAndLossCloseAtTheSameInstant_PlacementTimeDecides(string lossPlaced, double expected)
    {
        // The win is placed at 11:00. A loss placed at 10:00 comes before it (run reset); placed at 12:00 after it.
        DateTime close = D.ClosedAt(3).AddHours(3.0);
        DateTime lossTime = D.Placed(3).Date + TimeSpan.Parse(lossPlaced, System.Globalization.CultureInfo.InvariantCulture);
        var trades = new[]
        {
            D.Closed("ZFVG|v1|EURUSD|WIN", D.Placed(3).AddHours(1.0), close, 300.0),
            D.Closed("ZFVG|v1|EURUSD|LOSS", lossTime, close, -150.0),
        };

        Assert.Equal(expected, Next(trades).RiskMoney, 6);
    }

    [Theory]
    [Trait("Req", "RSK-017")]
    [InlineData("ZFVG|v1|EURUSD|A", "ZFVG|v1|EURUSD|B", 175.0)]
    [InlineData("ZFVG|v1|EURUSD|B", "ZFVG|v1|EURUSD|A", 100.0)]
    public void Next_SameCloseAndPlacementTime_LabelDecidesOrdinal(string winKey, string lossKey, double expected)
    {
        DateTime placed = D.Placed(3);
        DateTime close = D.ClosedAt(3);
        var trades = new[] { D.Closed(winKey, placed, close, 300.0), D.Closed(lossKey, placed, close, -150.0) };

        Assert.Equal(expected, Next(trades).RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_TradeClosingAtThePlacementInstant_CountsAsClosed()
    {
        DateTime t = D.ClosedAt(2);
        var trades = new[] { D.Nth(1, -100.0), D.Closed("ZFVG|v1|EURUSD|T02", D.Placed(2), t, -150.0) };

        // L = 100 + 150 = 250: (250 + 200) / 2.
        Assert.Equal(225.0, Next(trades, at: t).RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_PartialTrade_CountsOnlyWhenNothingOfItIsOpen()
    {
        DateTime placed = D.Placed(3);
        var part1 = new ClosedTradeInfo(7, "EURUSD", D.Label("EURUSD", "P"), placed, placed.AddHours(1.0), -50.0, 0.0, 0.0, D.Capital - 50.0);
        var part2 = part1 with { CloseTimeUtc = placed.AddHours(2.0), NetProfit = -70.0 };
        var open = new PositionInfo(7, "EURUSD", Side.Buy, 5000.0, 1.1, 1.098, 1.104, D.Label("EURUSD", "P"), "e=;s=;t=", placed);

        RecoveryAmount whileOpen = Next(FromBroker("EURUSD", new[] { part1 }, new[] { open }));
        RecoveryAmount closed = Next(FromBroker("EURUSD", new[] { part1, part2 }));

        Assert.Equal(0.0, whileOpen.LosingRun);
        Assert.Equal(100.0, whileOpen.RiskMoney, 6);
        Assert.Equal(120.0, closed.LosingRun, 6);
        Assert.Single(RecoverySizer.EntriesFromBroker("EURUSD", Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), new[] { part1, part2 }, RiskData.EpochStart));
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void EntriesFromBroker_TradesPlacedBeforeTheEpoch_AreNotCounted()
    {
        DateTime beforeEpoch = RiskData.EpochStart.AddDays(-1.0);
        var history = new[]
        {
            D.Trade(1, "EURUSD", "OLD1", beforeEpoch.AddDays(-2.0), beforeEpoch.AddDays(-2.0).AddHours(2.0), -500.0),
            D.Trade(2, "EURUSD", "OLD2", beforeEpoch, RiskData.EpochStart.AddHours(5.0), -700.0),
        };

        RecoveryAmount amount = Next(FromBroker("EURUSD", history));

        Assert.Equal(100.0, amount.RiskMoney, 6);
        Assert.Equal(0.0, amount.LosingRun);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_SameDataInThreeRecordOrders_SameAmountBitForBit()
    {
        var history = new List<ClosedTradeInfo>
        {
            D.Trade(1, "EURUSD", "A", D.Placed(1), D.ClosedAt(1), -104.2, -4.2),
            D.Trade(2, "EURUSD", "B", D.Placed(2), D.ClosedAt(2), -0.1),
            D.Trade(3, "EURUSD", "C", D.Placed(3), D.ClosedAt(3), -157.35, -5.1, -2.25),
            D.Trade(4, "EURUSD", "D", D.Placed(4), D.ClosedAt(4), -0.2),
            D.Trade(5, "EURUSD", "E", D.Placed(5), D.ClosedAt(5), -333.33),
        };

        double first = Next(FromBroker("EURUSD", history)).RiskMoney;
        double reversed = Next(FromBroker("EURUSD", Enumerable.Reverse(history).ToList())).RiskMoney;
        double shuffled = Next(FromBroker("EURUSD", new[] { history[3], history[0], history[4], history[2], history[1] })).RiskMoney;

        Assert.Equal(first, reversed);
        Assert.Equal(first, shuffled);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_TradePlacedOrClosedAfterThePlacement_FailsClosed()
    {
        Assert.Throws<ArgumentException>(() => Next(Losses(-100.0), at: D.ClosedAt(1).AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Next(new[] { new LadderEntry("ZFVG|v1|EURUSD|O", D.Placed(2), null, 0.0) }, at: D.Placed(1)));
        Assert.Throws<ArgumentException>(() => Next(Losses(-100.0), at: DateTime.SpecifyKind(ExecutionData.Now, DateTimeKind.Unspecified)));
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void EntriesFromBroker_InvalidRecordOfTheSymbol_FailsClosedNamingTheRecoveryPlan()
    {
        ClosedTradeInfo nan = D.Trade(1, "EURUSD", "A", D.Placed(1), D.ClosedAt(1), double.NaN);
        ClosedTradeInfo local = D.Trade(2, "EURUSD", "B", D.Placed(2), DateTime.SpecifyKind(D.ClosedAt(2), DateTimeKind.Local), -100.0);

        var e1 = Assert.Throws<ArgumentException>(() => FromBroker("EURUSD", new[] { nan }));
        var e2 = Assert.Throws<ArgumentException>(() => FromBroker("EURUSD", new[] { local }));
        Assert.Throws<ArgumentException>(() => FromBroker("EURUSD", new ClosedTradeInfo[] { null! }));
        Assert.Throws<ArgumentException>(() => FromBroker(string.Empty, Array.Empty<ClosedTradeInfo>()));

        Assert.StartsWith("Recovery sizing (RSK-017): Ladder sizing (RSK-013): ", e1.Message, StringComparison.Ordinal);
        Assert.Contains("not UTC", e2.Message, StringComparison.Ordinal);

        // The same faults on another symbol are not read by this instance.
        Assert.Empty(FromBroker("GBPUSD", new[] { nan, local }));
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void EntriesFromBroker_NullPositionOrOrderRecord_FailsClosed()
    {
        var p = Assert.Throws<ArgumentException>(() => FromBroker("EURUSD", Array.Empty<ClosedTradeInfo>(), new PositionInfo[] { null! }));
        var o = Assert.Throws<ArgumentException>(() => FromBroker("EURUSD", Array.Empty<ClosedTradeInfo>(), orders: new PendingOrderInfo[] { null! }));

        Assert.Contains("a position is null", p.Message, StringComparison.Ordinal);
        Assert.Contains("a pending order is null", o.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void EntriesFromBroker_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => RecoverySizer.EntriesFromBroker(null!, Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), Array.Empty<ClosedTradeInfo>(), RiskData.EpochStart));
        Assert.Throws<ArgumentNullException>(() => RecoverySizer.EntriesFromBroker("EURUSD", null!, Array.Empty<PendingOrderInfo>(), Array.Empty<ClosedTradeInfo>(), RiskData.EpochStart));
        Assert.Throws<ArgumentNullException>(() => RecoverySizer.EntriesFromBroker("EURUSD", Array.Empty<PositionInfo>(), null!, Array.Empty<ClosedTradeInfo>(), RiskData.EpochStart));
        Assert.Throws<ArgumentNullException>(() => RecoverySizer.EntriesFromBroker("EURUSD", Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), null!, RiskData.EpochStart));
        Assert.Throws<ArgumentNullException>(() => RecoverySizer.LosingRun(null!, ExecutionData.Now));
    }

    private static List<ClosedTradeInfo> PerPairHistory()
    {
        // EURUSD lost -100 and -150; GBPUSD lost -300 and then won +200.
        return new List<ClosedTradeInfo>
        {
            D.Trade(1, "EURUSD", "E1", D.Placed(1), D.ClosedAt(1), -100.0),
            D.Trade(2, "GBPUSD", "G1", D.Placed(2), D.ClosedAt(2), -300.0),
            D.Trade(3, "EURUSD", "E2", D.Placed(3), D.ClosedAt(3), -150.0),
            D.Trade(4, "GBPUSD", "G2", D.Placed(4), D.ClosedAt(4), 200.0),
        };
    }

    private static IReadOnlyList<LadderEntry> FromBroker(string symbol, IEnumerable<ClosedTradeInfo> history, IEnumerable<PositionInfo>? positions = null, IEnumerable<PendingOrderInfo>? orders = null)
    {
        return RecoverySizer.EntriesFromBroker(symbol, positions ?? Array.Empty<PositionInfo>(), orders ?? Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart);
    }
}
