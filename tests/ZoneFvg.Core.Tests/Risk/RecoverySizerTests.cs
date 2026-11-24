using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;
using D = ZoneFvg.Core.Tests.RecoverySizingData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-016 and RSK-017 on the pure sizer (spec v0.13, owner decision Z): eight base amounts that sum
/// to 1% of InitialCapital, step columns C to J per symbol raised by wins without skipping a column,
/// a reset after eight non-wins in a row, the 1.28% cap and the freeze. Defaults: InitialCapital
/// 200,000, RR 2, LadderGrowth 1.5924, balance 200,000 at every placement, no other account trade.
/// Four-decimal values compared to 1e-4.
/// </summary>
public class RecoverySizerTests
{
    private const double RewardRisk = 2.0;

    private static readonly double[] Base = { 40.6027, 60.9040, 91.3561, 137.0341, 205.5511, 308.3267, 462.4901, 693.7351 };

    private static readonly RecoverySizer Sizer = new(D.Recovery);

    private static RecoveryDecision Next(IReadOnlyList<LadderEntry> entries, RecoverySizer? sizer = null, double balance = D.Capital, DateTime? at = null, IReadOnlyList<ClosedTradeInfo>? account = null)
    {
        return (sizer ?? Sizer).Next(entries, at ?? ExecutionData.Now, balance, RewardRisk, account ?? Array.Empty<ClosedTradeInfo>());
    }

    /// <summary>
    /// Plays a sequence of one symbol: 'W' wins 2R, 'L' loses its risk, 'B' is break-even, each
    /// risking what the sizer gives it, one a day. Returns the entries and the risks taken.
    /// </summary>
    private static (List<LadderEntry> Entries, List<double> Risks) Play(string outcomes, RecoverySizer? sizer = null, List<LadderEntry>? start = null)
    {
        var entries = start == null ? new List<LadderEntry>() : new List<LadderEntry>(start);
        var risks = new List<double>();
        foreach (char outcome in outcomes)
        {
            int n = entries.Count + 1;
            double risk = Next(entries, sizer, at: D.Placed(n)).RiskMoney;
            double net = outcome == 'W' ? 2.0 * risk : outcome == 'L' ? -risk : 0.0;
            entries.Add(D.Nth(n, net));
            risks.Add(risk);
        }

        return (entries, risks);
    }

    private static int[] Columns(params int[] leading)
    {
        var columns = new int[8];
        leading.CopyTo(columns, 0);
        return columns;
    }

    // ------------------------------------------------------------------ RSK-016 base amounts

    [Fact]
    [Trait("Req", "RSK-016")]
    public void BaseAmounts_At200000_AreTheOnePercentStartAndSumTo2000()
    {
        IReadOnlyList<double> amounts = Sizer.BaseAmounts(RewardRisk);

        Assert.Equal(49.2578125, RecoverySizer.Multipliers(RewardRisk).Sum());
        for (int p = 0; p < 8; p++)
        {
            Assert.Equal(Base[p], amounts[p], 4);
        }

        Assert.Equal(2000.0, amounts.Sum(), 9);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void BaseAmounts_InitialCapital100000_AreHalf()
    {
        IReadOnlyList<double> full = Sizer.BaseAmounts(RewardRisk);
        IReadOnlyList<double> half = new RecoverySizer(D.Recovery with { InitialCapital = 100000.0 }).BaseAmounts(RewardRisk);

        for (int p = 0; p < 8; p++)
        {
            Assert.Equal(full[p] / 2.0, half[p], 9);
        }
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_NoEntrySinceTheEpoch_IsPosition1At40_6027WithAllColumns0()
    {
        RecoveryDecision first = Next(Array.Empty<LadderEntry>());

        Assert.Equal(1, first.Position);
        Assert.Equal(0, first.Step);
        Assert.Equal(40.6027, first.RiskMoney, 4);
        Assert.Equal(Columns(), first.Columns);
        Assert.False(first.Capped);
        Assert.False(first.StepsFrozen);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_EightLossesFromZero_RiskTheBaseSequenceThenTheNinthStartsAgain()
    {
        (List<LadderEntry> entries, List<double> risks) = Play("LLLLLLLL");

        for (int p = 0; p < 8; p++)
        {
            Assert.Equal(Base[p], risks[p], 4);
        }

        Assert.Equal(2000.0, risks.Sum(), 9);
        RecoveryDecision ninth = Next(entries);
        Assert.Equal(1, ninth.Position);
        Assert.Equal(40.6027, ninth.RiskMoney, 4);
    }

    // ------------------------------------------------------------------ RSK-016 columns

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_AFirstWin_RaisesCAndTheNextP1Risks64_6557()
    {
        (List<LadderEntry> entries, List<double> risks) = Play("W");

        RecoveryDecision next = Next(entries);

        Assert.Equal(40.6027, risks[0], 4);
        Assert.Equal(Columns(1), next.Columns);
        Assert.Equal(1, next.Position);
        Assert.Equal(1, next.Step);
        Assert.Equal(64.6557, next.RiskMoney, 4);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_LossThenWinAtPosition2_RaisesD()
    {
        (List<LadderEntry> entries, List<double> risks) = Play("WLW");

        Assert.Equal(new[] { 40.6027, 64.6557, 60.9040 }, risks.Select(r => Math.Round(r, 4)));
        Assert.Equal(Columns(1, 1), Next(entries).Columns);
        Assert.Equal(64.6557, Next(entries).RiskMoney, 4);

        (List<LadderEntry> afterLoss, _) = Play("L", start: entries);
        RecoveryDecision p2 = Next(afterLoss);
        Assert.Equal(2, p2.Position);
        Assert.Equal(96.9836, p2.RiskMoney, 4);
    }

    [Theory]
    [Trait("Req", "RSK-016")]
    [InlineData("WLLW", new[] { 1, 1 })]
    [InlineData("LLW", new[] { 1 })]
    [InlineData("WWLW", new[] { 2, 1 })]
    [InlineData("WWLWLW", new[] { 2, 2 })]
    public void Next_AWinNeverSkipsAColumn(string outcomes, int[] expected)
    {
        // WLLW: from (1) a win at p3 with D = 0 moves back to D. LLW: from 0 a win at p3 raises C.
        // WWLW: (2) then a win at p2 raises D: (2, 1); WWLWLW: from (2, 1) a win at p2 raises D: (2, 2).
        (List<LadderEntry> entries, _) = Play(outcomes);

        RecoveryDecision next = Next(entries);

        Assert.Equal(Columns(expected), next.Columns);
        Assert.Equal(1, next.Position);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_EightLossesFromColumns11_RiskTheRaisedSequenceThenResetToZero()
    {
        (List<LadderEntry> entries, _) = Play("WLW");
        (List<LadderEntry> after, List<double> risks) = Play("LLLLLLLL", start: entries);

        double[] expected = { 64.6557, 96.9836, 91.3561, 137.0341, 205.5511, 308.3267, 462.4901, 693.7351 };
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(expected[i], risks[i], 4);
        }

        Assert.Equal(2060.1326, risks.Sum(), 4);
        RecoveryDecision next = Next(after);
        Assert.Equal(Columns(), next.Columns);
        Assert.Equal(1, next.Position);
        Assert.Equal(40.6027, next.RiskMoney, 4);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_SevenLossesAndABreakEven_Reset()
    {
        (List<LadderEntry> entries, _) = Play("WLLLLLLLB");

        RecoveryDecision next = Next(entries);

        Assert.Equal(Columns(), next.Columns);
        Assert.Equal(1, next.Position);
        Assert.Equal(40.6027, next.RiskMoney, 4);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_SevenLossesAndAWinAtP8_DoNotResetAndTheWinRaisesC()
    {
        (List<LadderEntry> entries, List<double> risks) = Play("LLLLLLLW");

        RecoveryDecision next = Next(entries);

        Assert.Equal(693.7351, risks[7], 4);
        Assert.Equal(Columns(1), next.Columns);
        Assert.Equal(64.6557, next.RiskMoney, 4);
    }

    // ------------------------------------------------------------------ RSK-016 cap and freeze

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_RawAbove1_28PercentOf200000_RisksTheCap2560()
    {
        // RecoveryRunLossPercent 68.9609375 makes B1 = 2,800.
        var sizer = new RecoverySizer(D.Recovery with { RecoveryRunLossPercent = 68.9609375 });

        RecoveryDecision decision = Next(Array.Empty<LadderEntry>(), sizer);

        Assert.Equal(2800.0, decision.RawAmount, 6);
        Assert.Equal(2560.0, decision.CapAmount, 6);
        Assert.Equal(2560.0, decision.RiskMoney, 6);
        Assert.True(decision.Capped);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_CapBelowTheBaseAmount_RisksTheCap()
    {
        RecoveryDecision decision = Next(Array.Empty<LadderEntry>(), balance: 2000.0);

        Assert.Equal(25.6, decision.RiskMoney, 6);
        Assert.True(decision.Capped);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_RawAboveTheCap_FreezesTheStepsUntilTheEightLossReset()
    {
        // Cap 100 at 200,000: W (40.6027) gives (1), W (64.6557) gives (2), then p1 has raw 102.9578.
        var sizer = new RecoverySizer(D.Recovery with { RecoveryMaxRiskPercent = 0.05 });
        (List<LadderEntry> entries, List<double> risks) = Play("WW", sizer);
        RecoveryDecision third = Next(entries, sizer);
        (List<LadderEntry> afterWin, _) = Play("W", sizer, entries);
        RecoveryDecision fourth = Next(afterWin, sizer);
        (List<LadderEntry> afterReset, _) = Play("LLLLLLLL", sizer, afterWin);
        RecoveryDecision fresh = Next(afterReset, sizer);
        (List<LadderEntry> raisedAgain, _) = Play("W", sizer, afterReset);

        Assert.Equal(64.6557, risks[1], 4);
        Assert.Equal(102.9578, third.RawAmount, 4);
        Assert.Equal(100.0, third.RiskMoney, 6);
        Assert.True(third.Capped);
        Assert.True(third.StepsFrozen);
        Assert.Equal(Columns(2), fourth.Columns);
        Assert.Equal(100.0, fourth.RiskMoney, 6);
        Assert.True(fourth.StepsFrozen);
        Assert.Equal(Columns(), fresh.Columns);
        Assert.False(fresh.StepsFrozen);
        Assert.Equal(40.6027, fresh.RiskMoney, 4);
        Assert.Equal(Columns(1), Next(raisedAgain, sizer).Columns);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_RawEqualToTheCap_DoesNotFreeze()
    {
        double b1 = Sizer.BaseAmounts(RewardRisk)[0];
        double balance = b1 / 0.0128;

        RecoveryDecision atCap = Next(Array.Empty<LadderEntry>(), balance: balance);
        var entries = new List<LadderEntry> { D.Nth(1, 2.0 * b1) };
        RecoveryDecision next = Next(entries, balance: balance);

        Assert.Equal(atCap.CapAmount, atCap.RawAmount, 9);
        Assert.False(atCap.Capped);
        Assert.False(atCap.StepsFrozen);
        Assert.Equal(Columns(1), next.Columns);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    [Trait("Req", "RSK-017")]
    public void Next_TheCapOfAPastEntry_UsesTheBalanceAtItsPlacement()
    {
        // Cap 0.05%: at 200,000 the third p1 (raw 102.9578) is above the cap of 100 and freezes. If the
        // balance was 300,000 then (100,000 lost on another symbol after it), its cap was 150: no freeze, C = 3.
        var sizer = new RecoverySizer(D.Recovery with { RecoveryMaxRiskPercent = 0.05 });
        (List<LadderEntry> entries, _) = Play("WW", sizer);
        entries.Add(D.Nth(3, 2.0 * 102.9578));
        var laterLoss = new[] { D.Trade(99, "AUDUSD", "BIG", D.Placed(3).AddHours(3.0), D.Placed(3).AddHours(4.0), -100000.0) };

        RecoveryDecision frozen = Next(entries, sizer);
        RecoveryDecision notFrozen = Next(entries, sizer, account: laterLoss);

        Assert.Equal(Columns(2), frozen.Columns);
        Assert.Equal(Columns(3), notFrozen.Columns);
        Assert.Equal(100.0, notFrozen.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Next_RawAmountNotFinite_IsCappedAndFreezes()
    {
        // Extreme values only to reach the guard: B1 = 2.03e306, a win makes it B1 x 1e300 = infinity.
        var sizer = new RecoverySizer(D.Recovery with { InitialCapital = 1e308, RecoveryRunLossPercent = 100.0, RecoveryMaxRiskPercent = 100.0, LadderGrowth = 1e300 });
        var entries = new[] { D.Nth(1, 1.0) };

        RecoveryDecision next = Next(entries, sizer, balance: 1e308);

        Assert.True(double.IsPositiveInfinity(next.RawAmount));
        Assert.True(next.Capped);
        Assert.True(next.StepsFrozen);
        Assert.Equal(1e308, next.RiskMoney);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Multipliers_RewardRisk3_StillSumTo2000()
    {
        IReadOnlyList<double> m = RecoverySizer.Multipliers(3.0);
        IReadOnlyList<double> amounts = Sizer.BaseAmounts(3.0);

        Assert.Equal(1.0, m[0]);
        Assert.Equal(1.333333, m[1], 6);
        Assert.Equal(1.777778, m[2], 6);
        Assert.Equal(2000.0, amounts.Sum(), 9);
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
        var exception = Assert.Throws<ArgumentException>(() => Sizer.Next(Array.Empty<LadderEntry>(), ExecutionData.Now, balance, rewardRisk, Array.Empty<ClosedTradeInfo>()));

        Assert.StartsWith("Recovery sizing (RSK-017): ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-016")]
    public void Constructor_AndStateArguments_AreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => new RecoverySizer(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecoverySizer(D.Recovery with { RecoveryRunLossPercent = 0.0 }));
        Assert.Throws<ArgumentException>(() => new RecoverySizer(D.Recovery with { InitialCapital = double.NaN }).BaseAmounts(2.0));
        RecoveryState state = Sizer.StartState(2.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Close(0, ExecutionData.Now, -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Close(9, ExecutionData.Now, -1.0));
        Assert.Throws<ArgumentException>(() => state.Preview(0.0));
        RecoveryState later = state.Place(ExecutionData.Now, D.Capital, out _);
        Assert.Throws<InvalidOperationException>(() => later.Close(1, ExecutionData.Now.AddTicks(-1), -1.0));
        Assert.Equal(0, later.NonWinsInARow);
        Assert.Equal(1, later.Close(1, ExecutionData.Now, -1.0).NonWinsInARow);
        Assert.False(later.Frozen);
    }

    // ------------------------------------------------------------------ RSK-017 broker data

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_EachPairOnItsOwn_OtherSymbolsNeverChangeTheAmount()
    {
        var history = new List<ClosedTradeInfo> { D.Trade(1, "EURUSD", "E1", D.Placed(1), D.ClosedAt(1), 81.2054) };
        for (int n = 2; n <= 9; n++)
        {
            history.Add(D.NthTrade(n, "GBPUSD", -Base[n - 2]));
        }

        double eur = Next(FromBroker("EURUSD", history)).RiskMoney;
        double gbp = Next(FromBroker("GBPUSD", history)).RiskMoney;
        double gbpBeforeTheLosses = Next(FromBroker("GBPUSD", history.Take(1))).RiskMoney;

        Assert.Equal(64.6557, eur, 4);
        Assert.Equal(40.6027, gbp, 4);
        Assert.Equal(40.6027, gbpBeforeTheLosses, 4);
        Assert.Equal(eur, Next(FromBroker("EURUSD", history.Take(1))).RiskMoney);

        var positions = new[] { RiskData.Position(30, "AUDUSD", Side.Buy, 10000.0, 1.1, 1.098) };
        var orders = new[] { RiskData.Order(31, "GBPUSD", Side.Sell, 10000.0, 1.3, 1.302) };
        var more = new List<ClosedTradeInfo>(history) { D.Trade(20, "USDJPY", "Y", D.Placed(5), D.ClosedAt(5), 450.0) };
        Assert.Equal(eur, Next(FromBroker("EURUSD", more, positions, orders)).RiskMoney);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Ladder_SameBrokerData_StaysAccountWide()
    {
        var history = new List<ClosedTradeInfo>
        {
            D.Trade(1, "EURUSD", "E1", D.Placed(1), D.ClosedAt(1), -100.0),
            D.Trade(2, "GBPUSD", "G1", D.Placed(2), D.ClosedAt(2), -300.0),
            D.Trade(3, "EURUSD", "E2", D.Placed(3), D.ClosedAt(3), -150.0),
            D.Trade(4, "GBPUSD", "G2", D.Placed(4), D.ClosedAt(4), 200.0),
        };
        var ladder = new LadderSizer(D.Defaults);
        IReadOnlyList<LadderEntry> all = LadderSizer.EntriesFromBroker(Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart);
        IReadOnlyList<LadderEntry> eurOnly = LadderSizer.EntriesFromBroker(Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history.Where(t => t.Symbol == "EURUSD"), RiskData.EpochStart);

        Assert.Equal(4, all.Count);
        Assert.Equal(ladder.NextRiskPercent(all, ExecutionData.Now), new LadderSizer(D.Recovery).NextRiskPercent(all, ExecutionData.Now));
        Assert.NotEqual(ladder.NextRiskPercent(all, ExecutionData.Now), ladder.NextRiskPercent(eurOnly, ExecutionData.Now));
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_WinThreshold_OneCentIsAWin_ATenthOfANanoAndCostsAboveGrossAreNot()
    {
        Assert.Equal(Columns(1), Next(new[] { D.Nth(1, 0.01) }).Columns);

        // Seven losses and a +1e-10 after a win: eight non-wins, so a reset.
        (List<LadderEntry> seven, _) = Play("WLLLLLLL");
        seven.Add(D.Nth(9, 1e-10));
        Assert.Equal(Columns(), Next(seven).Columns);
        Assert.Equal(40.6027, Next(seven).RiskMoney, 4);

        // Gross +3.00 and commission -4.20: net -1.20, a non-win at p1 after a win: the next entry is p2.
        var history = new[]
        {
            D.Trade(1, "EURUSD", "A", D.Placed(1), D.ClosedAt(1), 81.2054),
            D.Trade(2, "EURUSD", "B", D.Placed(2), D.ClosedAt(2), -1.2, -4.2),
        };
        RecoveryDecision afterCosts = Next(FromBroker("EURUSD", history));
        Assert.Equal(2, afterCosts.Position);
        Assert.Equal(60.9040, afterCosts.RiskMoney, 4);
    }

    [Theory]
    [Trait("Req", "RSK-017")]
    [InlineData(true, 102.9578, 2)]
    [InlineData(false, 64.6557, 1)]
    public void Next_WinAndLossCloseAtTheSameInstant_PlacementOrderDecides(bool winPlacedFirst, double expected, int c)
    {
        // From (1): entries A (10:00 next day) and B (11:00) both close at 15:00. A win at p1 then a
        // loss gives C = 2, so the next p1 risks 40.6027 x 1.5924^2; a loss then a win at p2 raises D.
        DateTime day = D.Placed(2);
        DateTime close = day.AddHours(5.0);
        var entries = new List<LadderEntry>
        {
            D.Nth(1, 81.2054),
            D.Closed("ZFVG|v1|EURUSD|A", day, close, winPlacedFirst ? 100.0 : -64.6557),
            D.Closed("ZFVG|v1|EURUSD|B", day.AddHours(1.0), close, winPlacedFirst ? -60.904 : 100.0),
        };

        RecoveryDecision next = Next(entries);

        Assert.Equal(expected, next.RiskMoney, 4);
        Assert.Equal(c, next.Columns[0]);
        Assert.Equal(winPlacedFirst ? 0 : 1, next.Columns[1]);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_SamePlacementAndCloseTime_LabelDecides()
    {
        DateTime day = D.Placed(2);
        var aWins = new[] { D.Nth(1, 81.2054), D.Closed("ZFVG|v1|EURUSD|A", day, day.AddHours(5.0), 100.0), D.Closed("ZFVG|v1|EURUSD|B", day, day.AddHours(5.0), -60.904) };
        var bWins = new[] { D.Nth(1, 81.2054), D.Closed("ZFVG|v1|EURUSD|A", day, day.AddHours(5.0), -64.6557), D.Closed("ZFVG|v1|EURUSD|B", day, day.AddHours(5.0), 100.0) };

        Assert.Equal(Columns(2), Next(aWins).Columns);
        Assert.Equal(Columns(1, 1), Next(bWins).Columns);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_ACloseAtThePlacementInstant_CountsBeforeIt()
    {
        DateTime t = D.ClosedAt(1);

        RecoveryDecision next = Next(new[] { D.Nth(1, 81.2054) }, at: t);

        Assert.Equal(Columns(1), next.Columns);
        Assert.Equal(1, next.Position);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_OpenEntriesCountTowardsThePosition_ANinthUsesPosition8()
    {
        var oneOpen = new[] { new LadderEntry("ZFVG|v1|EURUSD|O1", D.Placed(1), null, 0.0) };
        LadderEntry[] nineOpen = Enumerable.Range(1, 9).Select(n => new LadderEntry("ZFVG|v1|EURUSD|O" + n.ToString(System.Globalization.CultureInfo.InvariantCulture), D.Placed(n), null, 0.0)).ToArray();

        Assert.Equal(2, Next(oneOpen).Position);
        Assert.Equal(60.9040, Next(oneOpen).RiskMoney, 4);
        Assert.Equal(8, Next(nineOpen).Position);
        Assert.Equal(693.7351, Next(nineOpen).RiskMoney, 4);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_PartialEntry_RaisesNothingWhileOpenAndIsANonWinAtNetMinus20()
    {
        DateTime placed = D.Placed(3);
        var part1 = new ClosedTradeInfo(7, "EURUSD", D.Label("EURUSD", "P"), placed, placed.AddHours(1.0), 50.0, 0.0, 0.0, D.Capital + 50.0);
        var part2 = part1 with { CloseTimeUtc = placed.AddHours(2.0), NetProfit = -70.0 };
        var open = new PositionInfo(7, "EURUSD", Side.Buy, 5000.0, 1.1, 1.098, 1.104, D.Label("EURUSD", "P"), "e=;s=;t=", placed);

        IReadOnlyList<LadderEntry> whileOpen = FromBroker("EURUSD", new[] { part1 }, new[] { open });
        IReadOnlyList<LadderEntry> closed = FromBroker("EURUSD", new[] { part1, part2 });

        Assert.Equal(Columns(), Next(whileOpen).Columns);
        Assert.Equal(2, Next(whileOpen).Position);
        LadderEntry entry = Assert.Single(closed);
        Assert.Equal(-20.0, entry.NetResult, 9);
        Assert.Equal(Columns(), Next(closed).Columns);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void EntriesFromBroker_EntriesPlacedBeforeTheEpoch_AreNotCounted()
    {
        DateTime beforeEpoch = RiskData.EpochStart.AddDays(-1.0);
        var history = new[]
        {
            D.Trade(1, "EURUSD", "OLD1", beforeEpoch.AddDays(-2.0), beforeEpoch.AddDays(-2.0).AddHours(2.0), 500.0),
            D.Trade(2, "EURUSD", "OLD2", beforeEpoch, RiskData.EpochStart.AddHours(5.0), 700.0),
        };

        RecoveryDecision first = Next(FromBroker("EURUSD", history));

        Assert.Equal(1, first.Position);
        Assert.Equal(Columns(), first.Columns);
        Assert.Equal(40.6027, first.RiskMoney, 4);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_SameDataInThreeRecordOrders_SameDecisionBitForBit()
    {
        var history = new List<ClosedTradeInfo>
        {
            D.Trade(1, "EURUSD", "A", D.Placed(1), D.ClosedAt(1), 81.2054),
            D.Trade(2, "EURUSD", "B", D.Placed(2), D.ClosedAt(2), -64.0),
            D.Trade(3, "EURUSD", "C", D.Placed(3), D.ClosedAt(3), 121.8),
            D.Trade(4, "EURUSD", "D", D.Placed(4), D.ClosedAt(4), -0.2),
            D.Trade(5, "GBPUSD", "E", D.Placed(5), D.ClosedAt(5), -333.33),
        };
        List<ClosedTradeInfo> reversed = Enumerable.Reverse(history).ToList();
        var shuffled = new[] { history[3], history[0], history[4], history[2], history[1] };

        RecoveryDecision a = Next(FromBroker("EURUSD", history), account: history);
        RecoveryDecision b = Next(FromBroker("EURUSD", reversed), account: reversed);
        RecoveryDecision c = Next(FromBroker("EURUSD", shuffled), account: shuffled);

        Assert.Equal(a.RiskMoney, b.RiskMoney);
        Assert.Equal(a.RiskMoney, c.RiskMoney);
        Assert.Equal(a.Position, c.Position);
        Assert.Equal(a.Columns, b.Columns);
        Assert.Equal(a.Columns, c.Columns);
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_EntryPlacedOrClosedAfterThePlacementOrNotUtc_FailsClosed()
    {
        Assert.Throws<ArgumentException>(() => Next(new[] { D.Nth(1, -40.0) }, at: D.ClosedAt(1).AddTicks(-1)));
        Assert.Throws<ArgumentException>(() => Next(new[] { new LadderEntry("ZFVG|v1|EURUSD|O", D.Placed(2), null, 0.0) }, at: D.Placed(1)));
        Assert.Throws<ArgumentException>(() => Next(new[] { D.Nth(1, -40.0) }, at: DateTime.SpecifyKind(ExecutionData.Now, DateTimeKind.Unspecified)));
        Assert.Throws<ArgumentException>(() => Next(new[] { D.Nth(1, double.NaN) }));
    }

    [Fact]
    [Trait("Req", "RSK-017")]
    public void Next_InvalidAccountHistory_FailsClosed()
    {
        ClosedTradeInfo nan = D.Trade(1, "GBPUSD", "A", D.Placed(1), D.ClosedAt(1), double.NaN);
        ClosedTradeInfo local = D.Trade(2, "GBPUSD", "B", D.Placed(2), DateTime.SpecifyKind(D.ClosedAt(2), DateTimeKind.Local), -1.0);

        Assert.Throws<ArgumentException>(() => Next(Array.Empty<LadderEntry>(), account: new[] { nan }));
        Assert.Throws<ArgumentException>(() => Next(Array.Empty<LadderEntry>(), account: new[] { local }));
        Assert.Throws<ArgumentException>(() => Next(Array.Empty<LadderEntry>(), account: new ClosedTradeInfo[] { null! }));
        Assert.Throws<ArgumentNullException>(() => Sizer.Next(Array.Empty<LadderEntry>(), ExecutionData.Now, D.Capital, 2.0, null!));
        Assert.Throws<ArgumentNullException>(() => Sizer.Next(null!, ExecutionData.Now, D.Capital, 2.0, Array.Empty<ClosedTradeInfo>()));
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
    }

    private static IReadOnlyList<LadderEntry> FromBroker(string symbol, IEnumerable<ClosedTradeInfo> history, IEnumerable<PositionInfo>? positions = null, IEnumerable<PendingOrderInfo>? orders = null)
    {
        return RecoverySizer.EntriesFromBroker(symbol, positions ?? Array.Empty<PositionInfo>(), orders ?? Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart);
    }
}
