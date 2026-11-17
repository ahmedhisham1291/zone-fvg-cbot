using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-013: ladder sizing, the owner's 4-part plan. Deal t = LadderDealRiskPercent x LadderGrowth
/// ^ step(t), the step being the column C, D, E or F of the entry's position after the last win;
/// weights w1 = 0.2962, w2 = w1/2 = 0.1481, w3 = (w1+w2)/2 = 0.22215, w4 = (w1+w2+w3)/2 =
/// 0.333225 (together 0.999675); the risk of entry t is w1 x deal t + w2 x deal t-1 + w3 x deal
/// t-2 + w4 x deal t-3 over the deals no win has ended, capped at MaxRiskPercentPerTrade (0.5).
/// The step-column rule itself is tested in <see cref="LadderStepColumnTests"/>.
/// </summary>
/// <remarks>
/// The sheet values (deal risk 2%, no cap, 100,000): Step 0 parts 592.40, 296.20, 444.30,
/// 666.45; Step 2 parts 1,502.17, 751.09, 1,126.63, 1,689.94; a row with four overlapping Step-0
/// deals 1,999.35. Defaults: entries 1 to 4 from the start risk 0.08886%, 0.13329%, 0.199935%,
/// 0.2999025%; after losses every entry risks 0.30 x 0.999675 = 0.2999025%.
/// </remarks>
public class LadderSizerTests
{
    private const double Capital = 100000.0;

    /// <summary>Monday 2026-01-05 00:00 UTC, after the test epoch of 1 January 2026.</summary>
    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    private static RiskParameters Ladder => RiskData.Parameters with { LadderEnabled = true };

    private static RiskParameters Sheet => Ladder with { LadderDealRiskPercent = 2.0, MaxRiskPercentPerTrade = 100.0 };

    private static LadderSizer Sizer(RiskParameters? parameters = null)
    {
        return new LadderSizer(parameters ?? Ladder);
    }

    private static DateTime H(double hours)
    {
        return T0.AddHours(hours);
    }

    private static LadderEntry Open(string key, double placedHour)
    {
        return new LadderEntry(key, H(placedHour), null, 0.0);
    }

    private static LadderEntry Closed(string key, double placedHour, double closedHour, double net)
    {
        return new LadderEntry(key, H(placedHour), H(closedHour), net);
    }

    /// <summary>Trades one after the other: entry i is placed at hour 2i and closes at hour 2i + 1 with the given result.</summary>
    private static List<LadderEntry> Sequential(params double[] results)
    {
        var entries = new List<LadderEntry>();
        for (int i = 0; i < results.Length; i++)
        {
            entries.Add(Closed("E" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), 2.0 * i, (2.0 * i) + 1.0, results[i]));
        }

        return entries;
    }

    private static string Label(string symbol, string zone)
    {
        return "ZFVG|v1|" + symbol + "|" + zone;
    }

    // ------------------------------------------------------------------ the owner's sheet

    [Fact]
    [Trait("Req", "RSK-013")]
    public void PartWeights_Defaults_FollowTheSheet()
    {
        IReadOnlyList<double> w = Sizer().PartWeights;

        Assert.Equal(4, w.Count);
        Assert.Equal(0.2962, w[0], 12);
        Assert.Equal(0.1481, w[1], 12);
        Assert.Equal(0.22215, w[2], 12);
        Assert.Equal(0.333225, w[3], 12);
        Assert.Equal(0.999675, w.Sum(), 12);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void DealParts_Step0DealRisk2Percent_GiveTheSheetPartsOn100000()
    {
        IReadOnlyList<double> parts = Sizer(Sheet).DealParts(0);

        Assert.Equal(592.40, parts[0] * Capital / 100.0, 2);
        Assert.Equal(296.20, parts[1] * Capital / 100.0, 2);
        Assert.Equal(444.30, parts[2] * Capital / 100.0, 2);
        Assert.Equal(666.45, parts[3] * Capital / 100.0, 2);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void DealParts_Step2DealRisk2Percent_GiveTheSheetPartsOn100000()
    {
        IReadOnlyList<double> parts = Sizer(Sheet).DealParts(2);

        Assert.Equal(1502.17, parts[0] * Capital / 100.0, 2);
        Assert.Equal(751.09, parts[1] * Capital / 100.0, 2);
        Assert.Equal(1126.63, parts[2] * Capital / 100.0, 2);
        Assert.Equal(1689.94, parts[3] * Capital / 100.0, 2);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_FourOverlappingStep0Deals_RowSumsTo1999_35()
    {
        // Entries 1 to 3 are still open, so the fourth entry carries part 1 of deal 4, part 2 of
        // deal 3, part 3 of deal 2 and part 4 of deal 1, all at Step 0.
        var entries = new List<LadderEntry> { Open("A", 0.0), Open("B", 1.0), Open("C", 2.0) };

        double percent = Sizer(Sheet).NextRiskPercent(entries);

        Assert.Equal(1999.35, percent * Capital / 100.0, 2);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void DealPercent_GrowsBy1_5924PerWin()
    {
        LadderSizer sizer = Sizer();

        Assert.Equal(0.30, sizer.DealPercent(0), 12);
        Assert.Equal(0.30 * 1.5924, sizer.DealPercent(1), 12);
        Assert.Equal(0.30 * 1.5924 * 1.5924, sizer.DealPercent(2), 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => sizer.DealPercent(-1));
    }

    // ------------------------------------------------------------------ defaults

    [Theory]
    [Trait("Req", "RSK-013")]
    [InlineData(0, 0.08886)]
    [InlineData(1, 0.13329)]
    [InlineData(2, 0.199935)]
    [InlineData(3, 0.2999025)]
    public void NextRiskPercent_AtTheStart_RampsUpAsOneToFourDealsOverlap(int openEntries, double expected)
    {
        var entries = new List<LadderEntry>();
        for (int i = 0; i < openEntries; i++)
        {
            entries.Add(Open("E" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), i));
        }

        Assert.Equal(expected, Sizer().NextRiskPercent(entries), 12);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_AfterLosses_IsTheSteadyState0_30Times0_999675()
    {
        double percent = Sizer().NextRiskPercent(Sequential(-100.0, -100.0, -100.0, -100.0, -100.0, -100.0));

        Assert.Equal(0.30 * 0.999675, percent, 12);
        Assert.Equal(0.2999025, percent, 12);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_OneWinAfterLosses_GrowsOnlyTheNewDeal()
    {
        // WP18: the win at position 5 raises F, moved back to C (D, E, F... C is 0); it ends the deals of
        // entries 2 to 5, so the new entry (position 1, step C = 1) carries only its own part 1.
        double percent = Sizer().NextRiskPercent(Sequential(-100.0, -100.0, -100.0, -100.0, 100.0));

        Assert.Equal(0.30 * 1.5924 * 0.2962, percent, 12);
        Assert.Equal(0.141500664, percent, 9);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_FourDealsAtOneWin_StaysJustBelowTheCap()
    {
        // Entry A won and closed (C = 1); B, C and D were placed after it at positions 1, 2, 3 and are
        // open: only B (position 1) has step 1. The new entry is position 4 at step 0 (WP18).
        var entries = new List<LadderEntry> { Closed("A", 0.0, 1.0, 50.0), Open("B", 2.0), Open("C", 3.0), Open("D", 4.0) };

        double percent = Sizer().NextRiskPercent(entries);

        Assert.Equal((0.30 * (0.2962 + 0.1481 + 0.22215)) + (0.30 * 1.5924 * 0.333225), percent, 12);
        Assert.True(percent < 0.5);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_AfterManyWins_IsCappedAtExactly0_5()
    {
        double percent = Sizer().NextRiskPercent(Sequential(100.0, 100.0, 100.0, 100.0, 100.0, 100.0));

        Assert.Equal(0.5, percent);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_SumExactlyAtTheCap_IsAllowed_OneTickAbove_IsCapped()
    {
        var entries = new List<LadderEntry> { Closed("A", 0.0, 1.0, 50.0), Open("B", 2.0), Open("C", 3.0), Open("D", 4.0) };
        double uncapped = Sizer(Ladder with { MaxRiskPercentPerTrade = 100.0 }).NextRiskPercent(entries);

        Assert.Equal(uncapped, Sizer(Ladder with { MaxRiskPercentPerTrade = uncapped }).NextRiskPercent(entries));
        Assert.Equal(uncapped - 1e-6, Sizer(Ladder with { MaxRiskPercentPerTrade = uncapped - 1e-6 }).NextRiskPercent(entries));
        Assert.Equal(uncapped, Sizer(Ladder with { MaxRiskPercentPerTrade = uncapped + 1e-6 }).NextRiskPercent(entries));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void RiskPercentFromDeals_EndlessWinRun_IsStillCapped()
    {
        LadderSizer sizer = Sizer();
        double deal = sizer.DealPercent(5000);

        Assert.True(double.IsPositiveInfinity(deal));
        Assert.Equal(0.5, sizer.RiskPercentFromDeals(new[] { deal }));
    }

    // ------------------------------------------------------------------ wins and columns (WP12 consecutive wins, rewritten in WP18)

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_ALossMovesTheNextEntryToTheNextColumn()
    {
        // L W W: the first win (position 2) moves back to C; the second (position 1) raises C to 2.
        Assert.Equal(2, Sizer().Next(Sequential(-10.0, 10.0, 10.0)).Step);

        // W W L: C = 2, the loss puts the next entry at position 2, column D = 0.
        Assert.Equal(0, Sizer().Next(Sequential(10.0, 10.0, -10.0)).Step);

        // W W L W: the last win at position 2 raises D; the next entry is position 1 with C = 2.
        Assert.Equal(2, Sizer().Next(Sequential(10.0, 10.0, -10.0, 10.0)).Step);
        Assert.Equal(new[] { 2, 1, 0, 0 }, Sizer().Next(Sequential(10.0, 10.0, -10.0, 10.0)).Columns.ToArray());
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_BreakEvenIsNotAWin_OneCentIs()
    {
        Assert.Equal(0, Sizer().Next(Sequential(10.0, 0.0)).Step);
        Assert.Equal(2, Sizer().Next(Sequential(10.0, 0.01)).Step);
        Assert.Equal(0, Sizer().Next(Sequential(10.0, -0.01)).Step);
        Assert.Equal(2, Sizer().Next(Sequential(10.0, 0.0)).Position);
        Assert.Equal(1, Sizer().Next(Sequential(10.0, 0.01)).Position);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_OpenTradesAreNotWins_ButTakeAPosition()
    {
        // A (position 1) wins at 5: C = 1. C (position 3, B still open) wins at 6: E moves back to D.
        // D is placed after both wins at position 1; the next entry is position 2 with D = 1.
        var entries = new List<LadderEntry> { Closed("A", 0.0, 5.0, 10.0), Open("B", 1.0), Closed("C", 2.0, 6.0, 10.0), Open("D", 7.0) };

        LadderDecision next = Sizer().Next(entries);

        Assert.Equal(1, next.Step);
        Assert.Equal(2, next.Position);
        Assert.Equal(new[] { 1, 1, 0, 0 }, next.Columns.ToArray());
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_TheLatestWinByCloseTime_StartsThePositions()
    {
        // A was placed first but closed last. Either way one win raised C, and nothing was placed after it.
        var lossClosedLast = new List<LadderEntry> { Closed("A", 0.0, 5.0, -10.0), Closed("B", 1.0, 3.0, 10.0) };
        var winClosedLast = new List<LadderEntry> { Closed("A", 0.0, 5.0, 10.0), Closed("B", 1.0, 3.0, -10.0) };

        Assert.Equal(1, Sizer().Next(lossClosedLast).Step);
        Assert.Equal(1, Sizer().Next(lossClosedLast).Position);
        Assert.Equal(1, Sizer().Next(winClosedLast).Step);
        Assert.Equal(1, Sizer().Next(winClosedLast).Position);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_SameCloseTime_OneWinRaisesOneColumn()
    {
        var laterLost = new List<LadderEntry> { Closed("A", 0.0, 5.0, 10.0), Closed("B", 1.0, 5.0, -10.0) };
        var laterWon = new List<LadderEntry> { Closed("A", 0.0, 5.0, -10.0), Closed("B", 1.0, 5.0, 10.0) };

        Assert.Equal(new[] { 1, 0, 0, 0 }, Sizer().Next(laterLost).Columns.ToArray());
        Assert.Equal(new[] { 1, 0, 0, 0 }, Sizer().Next(laterWon).Columns.ToArray());
        Assert.Equal(1, Sizer().Next(laterLost).Step);
        Assert.Equal(1, Sizer().Next(laterWon).Step);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_OnlyTradesClosedAtOrBeforeThePlacementCount()
    {
        // Sequential(10, 10, 10) as the broker shows it at each time: A placed 0 closes 1, B placed 2 closes 3.
        LadderDecision at0999 = Sizer().Next(new[] { Open("A", 0.0) }, H(0.999));
        LadderDecision at1 = Sizer().Next(new[] { Closed("A", 0.0, 1.0, 10.0) }, H(1.0));
        LadderDecision at2999 = Sizer().Next(new[] { Closed("A", 0.0, 1.0, 10.0), Open("B", 2.0) }, H(2.999));
        LadderDecision at3 = Sizer().Next(new[] { Closed("A", 0.0, 1.0, 10.0), Closed("B", 2.0, 3.0, 10.0) }, H(3.0));

        Assert.Equal(0, at0999.Step);
        Assert.Equal(1, at1.Step);
        Assert.Equal(0, at2999.Step);
        Assert.Equal(2, at3.Step);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_EarlierDealsKeepTheRunOfTheirOwnPlacement()
    {
        // WP18: W, W (C = 2), then three entries placed after the wins are open at positions 1, 2, 3: only C
        // (position 1) has step 2, D and E step 0, and the new entry (position 4) step 0. Compare with the
        // same wins closing after those entries were placed: A (position 1) raises C, B (position 2) raises
        // D, and the wins end every deal, so the new entry (position 1, step 1) carries only its own part.
        var before = new List<LadderEntry> { Closed("A", 0.0, 1.0, 10.0), Closed("B", 2.0, 3.0, 10.0), Open("C", 4.0), Open("D", 5.0), Open("E", 6.0) };
        var after = new List<LadderEntry> { Closed("A", 0.0, 7.0, 10.0), Closed("B", 2.0, 8.0, 10.0), Open("C", 4.0), Open("D", 5.0), Open("E", 6.0) };
        double g2 = 1.5924 * 1.5924;

        Assert.Equal((0.30 * (0.2962 + 0.1481 + 0.22215)) + (0.30 * g2 * 0.333225), Sizer().NextRiskPercent(before), 12);
        Assert.Equal(0.30 * 1.5924 * 0.2962, Sizer().NextRiskPercent(after), 12);
    }

    // ------------------------------------------------------------------ disabled, restart, order

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_LadderDisabled_IsTheFixedRiskPercent()
    {
        RiskParameters fixedRisk = Ladder with { LadderEnabled = false, RiskPercent = 0.43 };

        Assert.Equal(0.43, Sizer(fixedRisk).NextRiskPercent(Array.Empty<LadderEntry>()));
        Assert.Equal(0.43, Sizer(fixedRisk).NextRiskPercent(Sequential(100.0, 100.0, 100.0, 100.0, 100.0)));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_SameEntriesInAnyOrder_GiveBitIdenticalResults()
    {
        List<LadderEntry> entries = Sequential(10.0, -3.3, 0.7, 12.1, -0.1, 4.2);
        entries.Add(Open("X", 20.0));
        List<LadderEntry> reversed = Enumerable.Reverse(entries).ToList();

        Assert.Equal(Sizer().NextRiskPercent(entries), Sizer().NextRiskPercent(reversed));
        Assert.Equal(Sizer().NextRiskPercent(entries), new LadderSizer(Ladder).NextRiskPercent(entries));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void EntriesFromBroker_SameBrokerDataAfterARestart_GiveTheSamePercent()
    {
        PositionInfo[] positions = { RiskData.Position(5, "GBPUSD", Side.Buy, 1000.0, 1.3, 1.29, H(30.0), Label("GBPUSD", "Z5")) };
        PendingOrderInfo[] orders = { RiskData.Order(6, "AUDUSD", Side.Buy, 1000.0, 0.65, 0.64, H(31.0), Label("AUDUSD", "Z6")) };
        ClosedTradeInfo[] history =
        {
            RiskData.Closed(1, "EURUSD", H(1.0), H(2.0), 120.5, 100120.5, Label("EURUSD", "Z1")),
            RiskData.Closed(2, "GBPUSD", H(3.0), H(4.0), 80.1, 100200.6, Label("GBPUSD", "Z2")),
            RiskData.Closed(3, "USDJPY", H(5.0), H(9.0), -60.0, 100140.6, Label("USDJPY", "Z3")),
            RiskData.Closed(4, "EURUSD", H(6.0), H(8.0), 99.9, 100240.5, Label("EURUSD", "Z4")),
        };

        double first = Sizer().NextRiskPercent(LadderSizer.EntriesFromBroker(positions, orders, history, RiskData.EpochStart));
        double restarted = new LadderSizer(Ladder).NextRiskPercent(LadderSizer.EntriesFromBroker(Enumerable.Reverse(positions), orders, Enumerable.Reverse(history), RiskData.EpochStart));

        Assert.Equal(first, restarted);
    }

    // ------------------------------------------------------------------ broker data

    [Fact]
    [Trait("Req", "RSK-013")]
    public void EntriesFromBroker_NumbersBotEntriesSinceTheEpochByPlacement()
    {
        PositionInfo[] positions =
        {
            RiskData.Position(10, "GBPUSD", Side.Buy, 1000.0, 1.3, 1.29, H(20.0), Label("GBPUSD", "P")),
            RiskData.Position(11, "EURUSD", Side.Buy, 1000.0, 1.1, 1.09, H(21.0), "manual"),
        };
        PendingOrderInfo[] orders =
        {
            RiskData.Order(20, "AUDUSD", Side.Buy, 1000.0, 0.65, 0.64, H(22.0), Label("AUDUSD", "O")),
            RiskData.Order(21, "AUDUSD", Side.Buy, 1000.0, 0.65, 0.64, H(23.0), "other"),
        };
        ClosedTradeInfo[] history =
        {
            RiskData.Closed(1, "EURUSD", H(2.0), H(3.0), 50.0, 100050.0, Label("EURUSD", "C")),
            RiskData.Closed(2, "EURUSD", RiskData.EpochStart.AddHours(-1.0), H(1.0), 70.0, 100120.0, Label("EURUSD", "Old")),
            RiskData.Closed(3, "EURUSD", H(4.0), H(5.0), 70.0, 100190.0, "manual"),
        };

        IReadOnlyList<LadderEntry> entries = LadderSizer.EntriesFromBroker(positions, orders, history, RiskData.EpochStart);

        Assert.Equal(new[] { Label("EURUSD", "C"), Label("GBPUSD", "P"), Label("AUDUSD", "O") }, entries.Select(e => e.Key));
        Assert.Equal(new LadderEntry(Label("EURUSD", "C"), H(2.0), H(3.0), 50.0), entries[0]);
        Assert.Equal(new LadderEntry(Label("GBPUSD", "P"), H(20.0), null, 0.0), entries[1]);
        Assert.Equal(new LadderEntry(Label("AUDUSD", "O"), H(22.0), null, 0.0), entries[2]);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void EntriesFromBroker_EntryPlacedExactlyAtTheEpoch_IsCounted_OneTickBefore_IsNot()
    {
        PendingOrderInfo[] atEpoch = { RiskData.Order(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.09, RiskData.EpochStart, Label("EURUSD", "A")) };
        PendingOrderInfo[] before = { RiskData.Order(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.09, RiskData.EpochStart.AddTicks(-1), Label("EURUSD", "A")) };

        Assert.Single(LadderSizer.EntriesFromBroker(Array.Empty<PositionInfo>(), atEpoch, Array.Empty<ClosedTradeInfo>(), RiskData.EpochStart));
        Assert.Empty(LadderSizer.EntriesFromBroker(Array.Empty<PositionInfo>(), before, Array.Empty<ClosedTradeInfo>(), RiskData.EpochStart));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void EntriesFromBroker_PartialClosesOfOneEntry_AreOneTradeWithTheSummedResult()
    {
        string label = Label("EURUSD", "A");
        ClosedTradeInfo[] history =
        {
            RiskData.Closed(1, "EURUSD", H(1.0), H(2.0), 30.0, 100030.0, label),
            RiskData.Closed(1, "EURUSD", H(1.0), H(4.0), -40.0, 99990.0, label),
        };

        LadderEntry entry = Assert.Single(LadderSizer.EntriesFromBroker(Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart));

        Assert.Equal(new LadderEntry(label, H(1.0), H(4.0), -10.0), entry);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void EntriesFromBroker_PartiallyClosedPositionStillOpen_IsAnOpenEntry()
    {
        string label = Label("EURUSD", "A");
        PositionInfo[] positions = { RiskData.Position(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.09, H(1.0), label) };
        ClosedTradeInfo[] history = { RiskData.Closed(1, "EURUSD", H(1.0), H(2.0), 30.0, 100030.0, label) };

        LadderEntry entry = Assert.Single(LadderSizer.EntriesFromBroker(positions, Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart));

        Assert.Null(entry.ClosedUtc);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void EntriesFromBroker_PartialFillAndItsRemainder_AreOneEntryPlacedAtTheOrderTime()
    {
        string label = Label("EURUSD", "A");
        PositionInfo[] positions = { RiskData.Position(2, "EURUSD", Side.Buy, 1000.0, 1.1, 1.09, H(3.0), label) };
        PendingOrderInfo[] orders = { RiskData.Order(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.09, H(1.0), label) };

        LadderEntry entry = Assert.Single(LadderSizer.EntriesFromBroker(positions, orders, Array.Empty<ClosedTradeInfo>(), RiskData.EpochStart));

        Assert.Equal(new LadderEntry(label, H(1.0), null, 0.0), entry);
    }

    // ------------------------------------------------------------------ fail closed

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_InvalidEntries_Throw()
    {
        LadderSizer sizer = Sizer();
        DateTime local = new(2026, 1, 5, 1, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentNullException>(() => sizer.NextRiskPercent(null!));
        Assert.Throws<ArgumentException>(() => sizer.NextRiskPercent(new LadderEntry[] { null! }));
        Assert.Throws<ArgumentException>(() => sizer.NextRiskPercent(new[] { Open("A", 0.0), Open("A", 1.0) }));
        Assert.Throws<ArgumentException>(() => sizer.NextRiskPercent(new[] { Open(string.Empty, 0.0) }));
        Assert.Throws<ArgumentException>(() => sizer.NextRiskPercent(new[] { new LadderEntry("A", local, null, 0.0) }));
        Assert.Throws<ArgumentException>(() => sizer.NextRiskPercent(new[] { new LadderEntry("A", H(2.0), local, 0.0) }));
        Assert.Throws<ArgumentException>(() => sizer.NextRiskPercent(new[] { Closed("A", 2.0, 1.0, 5.0) }));
        Assert.Throws<ArgumentException>(() => sizer.NextRiskPercent(new[] { Closed("A", 0.0, 1.0, double.NaN) }));
        Assert.Throws<ArgumentException>(() => Sizer(Ladder with { LadderEnabled = false }).NextRiskPercent(new[] { Closed("A", 0.0, 1.0, double.PositiveInfinity) }));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void EntriesFromBroker_InvalidBotRecords_Throw_InvalidForeignRecordsAreIgnored()
    {
        DateTime local = new(2026, 1, 5, 1, 0, 0, DateTimeKind.Local);
        PositionInfo[] none = Array.Empty<PositionInfo>();
        PendingOrderInfo[] noOrders = Array.Empty<PendingOrderInfo>();
        ClosedTradeInfo[] noHistory = Array.Empty<ClosedTradeInfo>();
        string label = Label("EURUSD", "A");

        Assert.Throws<ArgumentNullException>(() => LadderSizer.EntriesFromBroker(null!, noOrders, noHistory, RiskData.EpochStart));
        Assert.Throws<ArgumentNullException>(() => LadderSizer.EntriesFromBroker(none, null!, noHistory, RiskData.EpochStart));
        Assert.Throws<ArgumentNullException>(() => LadderSizer.EntriesFromBroker(none, noOrders, null!, RiskData.EpochStart));
        Assert.Throws<ArgumentException>(() => LadderSizer.EntriesFromBroker(none, noOrders, noHistory, local));
        Assert.Throws<ArgumentException>(() => LadderSizer.EntriesFromBroker(new PositionInfo[] { null! }, noOrders, noHistory, RiskData.EpochStart));
        Assert.Throws<ArgumentException>(() => LadderSizer.EntriesFromBroker(none, new PendingOrderInfo[] { null! }, noHistory, RiskData.EpochStart));
        Assert.Throws<ArgumentException>(() => LadderSizer.EntriesFromBroker(none, noOrders, new ClosedTradeInfo[] { null! }, RiskData.EpochStart));
        Assert.Throws<ArgumentException>(() => LadderSizer.EntriesFromBroker(new[] { RiskData.Position(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.09, local, label) }, noOrders, noHistory, RiskData.EpochStart));
        Assert.Throws<ArgumentException>(() => LadderSizer.EntriesFromBroker(none, new[] { RiskData.Order(1, "EURUSD", Side.Buy, 1000.0, 1.1, 1.09, local, label) }, noHistory, RiskData.EpochStart));
        Assert.Throws<ArgumentException>(() => LadderSizer.EntriesFromBroker(none, noOrders, new[] { RiskData.Closed(1, "EURUSD", H(1.0), H(2.0), double.NaN, 100000.0, label) }, RiskData.EpochStart));
        Assert.Throws<ArgumentException>(() => LadderSizer.EntriesFromBroker(none, noOrders, new[] { RiskData.Closed(1, "EURUSD", H(1.0), local, 5.0, 100000.0, label) }, RiskData.EpochStart));
        Assert.Throws<ArgumentException>(() => LadderSizer.EntriesFromBroker(none, noOrders, new[] { RiskData.Closed(1, "EURUSD", local, H(2.0), 5.0, 100000.0, label) }, RiskData.EpochStart));
        Assert.Empty(LadderSizer.EntriesFromBroker(none, noOrders, new[] { RiskData.Closed(1, "EURUSD", local, local, double.NaN, 100000.0, "manual") }, RiskData.EpochStart));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void RiskPercentFromDeals_WrongNumberOrInvalidDeals_Throw()
    {
        LadderSizer sizer = Sizer();

        Assert.Throws<ArgumentNullException>(() => sizer.RiskPercentFromDeals(null!));
        Assert.Throws<ArgumentException>(() => sizer.RiskPercentFromDeals(Array.Empty<double>()));
        Assert.Throws<ArgumentException>(() => sizer.RiskPercentFromDeals(new[] { 0.3, 0.3, 0.3, 0.3, 0.3 }));
        Assert.Throws<ArgumentException>(() => sizer.RiskPercentFromDeals(new[] { 0.3, 0.0 }));
        Assert.Throws<ArgumentException>(() => sizer.RiskPercentFromDeals(new[] { double.NaN }));
        Assert.Throws<ArgumentException>(() => sizer.RiskPercentFromDeals(new[] { -0.3 }));
    }

    // ------------------------------------------------------------------ parameters

    [Fact]
    [Trait("Req", "RSK-013")]
    public void RiskParameters_LadderDefaults_AreTheOwnersPlan()
    {
        var parameters = new RiskParameters();

        Assert.True(parameters.LadderEnabled);
        Assert.Equal(0.30, parameters.LadderDealRiskPercent);
        Assert.Equal(1.5924, parameters.LadderGrowth);
        Assert.Equal(0.2962, parameters.LadderFirstWeight);
        Assert.Equal(0.5, parameters.MaxRiskPercentPerTrade);
    }

    [Theory]
    [Trait("Req", "RSK-013")]
    [InlineData("LadderDealRiskPercent", 0.0, false)]
    [InlineData("LadderDealRiskPercent", 1e-9, true)]
    [InlineData("LadderDealRiskPercent", 100.0, true)]
    [InlineData("LadderDealRiskPercent", 100.000001, false)]
    [InlineData("LadderDealRiskPercent", double.NaN, false)]
    [InlineData("LadderGrowth", 1.0, true)]
    [InlineData("LadderGrowth", 0.999999, false)]
    [InlineData("LadderGrowth", double.PositiveInfinity, false)]
    [InlineData("LadderFirstWeight", 0.0, false)]
    [InlineData("LadderFirstWeight", 0.2962962962962963, true)]
    [InlineData("LadderFirstWeight", 0.2963, false)]
    [InlineData("LadderFirstWeight", double.NaN, false)]
    [InlineData("MaxRiskPercentPerTrade", 0.0, false)]
    [InlineData("MaxRiskPercentPerTrade", 0.5, true)]
    [InlineData("MaxRiskPercentPerTrade", 100.0, true)]
    [InlineData("MaxRiskPercentPerTrade", 100.000001, false)]
    [InlineData("MaxRiskPercentPerTrade", double.NegativeInfinity, false)]
    public void Validate_LadderValues_AreChecked(string name, double value, bool valid)
    {
        RiskParameters parameters = name switch
        {
            "LadderDealRiskPercent" => RiskData.Parameters with { LadderDealRiskPercent = value },
            "LadderGrowth" => RiskData.Parameters with { LadderGrowth = value },
            "LadderFirstWeight" => RiskData.Parameters with { LadderFirstWeight = value },
            _ => RiskData.Parameters with { MaxRiskPercentPerTrade = value },
        };

        Exception? full = Record.Exception(() => parameters.Validate());
        Exception? sizing = Record.Exception(() => new LadderSizer(parameters));

        if (valid)
        {
            Assert.Null(full);
            Assert.Null(sizing);
        }
        else
        {
            Assert.Equal(name, Assert.IsType<ArgumentOutOfRangeException>(full).ParamName);
            Assert.Equal(name, Assert.IsType<ArgumentOutOfRangeException>(sizing).ParamName);
        }
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Constructor_NullOrInvalidParameters_Throw_EpochIsNotNeeded()
    {
        Assert.Throws<ArgumentNullException>(() => new LadderSizer(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LadderSizer(new RiskParameters { RiskPercent = 0.0 }));
        Assert.Equal(0.08886, new LadderSizer(new RiskParameters()).NextRiskPercent(Array.Empty<LadderEntry>()), 12);
    }

    [Theory]
    [Trait("Req", "RSK-013")]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(100.000001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void PositionSizer_InvalidLadderPercent_IsAnInvalidInput(double percent)
    {
        var sizer = new PositionSizer(RiskData.Parameters);
        var plan = new TradePlan("EURUSD", Side.Buy, 1.1, 1.098, 1.104);

        SizingResult result = sizer.Size(RiskData.Account(Capital), plan, RiskData.EurUsd, percent);

        Assert.Equal(RiskReason.InvalidInput, result.Reason);
        Assert.Equal(0.0, result.VolumeUnits);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void PositionSizer_LadderPercent_ReplacesRiskPercentInTheFormula()
    {
        var sizer = new PositionSizer(RiskData.Parameters);
        var plan = new TradePlan("EURUSD", Side.Buy, 1.1, 1.098, 1.104);

        SizingResult first = sizer.Size(RiskData.Account(Capital), plan, RiskData.EurUsd, 0.08886);
        SizingResult hundred = sizer.Size(RiskData.Account(Capital), plan, RiskData.EurUsd, 100.0);

        Assert.True(first.IsAllowed);
        Assert.Equal(88.86, first.RiskAmount, 9);
        Assert.Equal(44000.0, first.VolumeUnits, 6);
        Assert.Equal(88.0, first.RiskMoney, 6);
        Assert.Equal(RiskReason.VolumeAboveCap, hundred.Reason);
    }
}
