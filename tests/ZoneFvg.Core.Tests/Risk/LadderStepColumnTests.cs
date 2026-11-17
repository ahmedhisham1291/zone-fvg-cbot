using Xunit;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-013 step columns and win reset (WP18, owner's rule of 2026-11-17): a win ends every open
/// deal; steps live in the columns C, D, E, F of the 1st to 4th entry after the last win (5th and
/// later use step 0); a win raises its position's column, not past a column still at 0; once an
/// entry's raw percent is above 0.5 no step rises; all columns go back to 0
/// only with a new RiskEpochDate (owner change of 2026-11-17: no monthly reset). Percent of balance, defaults (deal 0.30, cap 0.5), tolerance 1e-6.
/// </summary>
/// <remarks>
/// The expected values were checked against the owner's reference implementation (each trade
/// closed before the next is placed). W = win (+100), L = loss (-100), B = break-even (0).
/// </remarks>
public class LadderStepColumnTests
{
    private const double Tolerance = 1e-6;

    /// <summary>Monday 2026-01-05 00:00 UTC, after the test epoch of 1 January 2026.</summary>
    private static readonly DateTime T0 = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>1 February 2026 00:00 CET (winter time).</summary>
    private static readonly DateTime FebruaryUtc = new(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc);

    /// <summary>1 July 2026 00:00 CEST (summer time).</summary>
    private static readonly DateTime JulyUtc = new(2026, 6, 30, 22, 0, 0, DateTimeKind.Utc);

    private static readonly double[] OwnersExample =
    {
        0.08886, 0.141501, 0.225326, 0.201523, 0.302284, 0.453426, 0.225326, 0.358809, 0.320905, 0.428717,
        0.358809, 0.320905, 0.481357, 0.5, 0.398604, 0.359123,
    };

    private static RiskParameters Ladder => RiskData.Parameters with { LadderEnabled = true };

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

    /// <summary>Trades one after the other from <paramref name="start"/>: entry i is placed at 2i hours and closes at 2i + 1 hours.</summary>
    private static List<LadderEntry> Sequence(string pattern, DateTime start, string prefix = "E")
    {
        var entries = new List<LadderEntry>();
        for (int i = 0; i < pattern.Length; i++)
        {
            double net = pattern[i] switch
            {
                'W' => 100.0,
                'L' => -100.0,
                _ => 0.0,
            };
            DateTime placed = start.AddHours(2.0 * i);
            entries.Add(new LadderEntry(prefix + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), placed, placed.AddHours(1.0), net));
        }

        return entries;
    }

    private static List<LadderEntry> Sequence(string pattern)
    {
        return Sequence(pattern, T0);
    }

    /// <summary>The sizing of entry k (0-based) of a sequence: the entries before it, at its placement time.</summary>
    private static LadderDecision Entry(List<LadderEntry> entries, int k, LadderSizer? sizer = null)
    {
        return (sizer ?? Sizer()).Next(entries.Take(k).ToList(), entries[k].PlacedUtc);
    }

    /// <summary>The sizing of the entry after the whole list, placed one hour after <paramref name="after"/>.</summary>
    private static LadderDecision NextAfter(List<LadderEntry> entries, DateTime after, LadderSizer? sizer = null)
    {
        return (sizer ?? Sizer()).Next(entries, after.AddHours(1.0));
    }

    private static int[] Columns(LadderDecision decision)
    {
        return decision.Columns.ToArray();
    }

    // ------------------------------------------------------------------ the owner's example

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_OwnersExample_GivesTheOwnersPercents()
    {
        List<LadderEntry> entries = Sequence("WWLLLWWLLWLLLLLL");

        for (int k = 0; k < entries.Count; k++)
        {
            Assert.Equal(OwnersExample[k], Entry(entries, k).RiskPercent, Tolerance);
        }
    }

    [Theory]
    [Trait("Req", "RSK-013")]
    [InlineData(0, 88.86)]
    [InlineData(1, 141.50)]
    [InlineData(2, 225.33)]
    [InlineData(3, 201.52)]
    [InlineData(4, 302.28)]
    [InlineData(5, 453.43)]
    [InlineData(6, 225.33)]
    [InlineData(7, 358.81)]
    [InlineData(8, 320.90)]
    [InlineData(9, 428.72)]
    public void Next_OwnersExampleOn100000_GivesTheOwnersDollars(int k, double dollars)
    {
        List<LadderEntry> entries = Sequence("WWLLLWWLLW");

        double percent = Entry(entries, k).RiskPercent;

        Assert.Equal(dollars, Math.Round(100000.0 * percent / 100.0, 2, MidpointRounding.AwayFromZero), 9);
    }

    [Theory]
    [Trait("Req", "RSK-013")]
    [InlineData(2, 2, 0, 0, 0)]
    [InlineData(6, 2, 1, 0, 0)]
    [InlineData(7, 3, 1, 0, 0)]
    [InlineData(10, 3, 1, 1, 0)]
    public void Next_OwnersExample_ColumnsAfterTrade(int trades, int c, int d, int e, int f)
    {
        List<LadderEntry> entries = Sequence("WWLLLWWLLW");

        LadderDecision next = NextAfter(entries.Take(trades).ToList(), entries[trades - 1].ClosedUtc!.Value);

        Assert.Equal(new[] { c, d, e, f }, Columns(next));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_OwnersExample_PositionsAndSteps()
    {
        List<LadderEntry> entries = Sequence("WWLLLWWLLW");
        int[] positions = { 1, 1, 1, 2, 3, 4, 1, 1, 2, 3 };
        int[] steps = { 0, 1, 2, 0, 0, 0, 2, 3, 1, 0 };

        for (int k = 0; k < entries.Count; k++)
        {
            LadderDecision decision = Entry(entries, k);
            Assert.Equal(positions[k], decision.Position);
            Assert.Equal(steps[k], decision.Step);
            Assert.Equal(decision.RiskPercent, decision.RawPercent);
        }
    }

    // ------------------------------------------------------------------ raising and freezing

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_SevenWins_FreezeAfterTheFifth()
    {
        List<LadderEntry> entries = Sequence("WWWWWWW");
        double[] expected = { 0.08886, 0.141501, 0.225326, 0.358809, 0.5, 0.5, 0.5 };

        for (int k = 0; k < entries.Count; k++)
        {
            Assert.Equal(expected[k], Entry(entries, k).RiskPercent, Tolerance);
        }

        LadderDecision fifth = Entry(entries, 4);
        Assert.Equal(0.571367, fifth.RawPercent, Tolerance);
        Assert.False(fifth.StepsFrozen);
        Assert.Equal(new[] { 4, 0, 0, 0 }, Columns(NextAfter(entries.Take(5).ToList(), entries[4].ClosedUtc!.Value)));
        Assert.True(NextAfter(entries.Take(5).ToList(), entries[4].ClosedUtc!.Value).StepsFrozen);
        Assert.Equal(new[] { 4, 0, 0, 0 }, Columns(NextAfter(entries, entries[6].ClosedUtc!.Value)));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_RawExactlyAtTheCap_DoesNotFreeze_AboveIt_Does()
    {
        List<LadderEntry> entries = Sequence("WWWWW");
        double raw = Entry(entries, 4, Sizer(Ladder with { MaxRiskPercentPerTrade = 100.0 })).RawPercent;
        DateTime after = entries[4].ClosedUtc!.Value;

        LadderDecision atCap = NextAfter(entries, after, Sizer(Ladder with { MaxRiskPercentPerTrade = raw }));
        LadderDecision belowRaw = NextAfter(entries, after, Sizer(Ladder with { MaxRiskPercentPerTrade = raw - 1e-6 }));

        Assert.Equal(new[] { 5, 0, 0, 0 }, Columns(atCap));
        Assert.False(atCap.StepsFrozen);
        Assert.Equal(new[] { 4, 0, 0, 0 }, Columns(belowRaw));
        Assert.True(belowRaw.StepsFrozen);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_LongLosingRun_FifthAndLaterUseStep0()
    {
        List<LadderEntry> entries = Sequence("LLLLLL");
        double[] expected = { 0.08886, 0.13329, 0.199935, 0.299903, 0.299903, 0.299903 };

        for (int k = 0; k < entries.Count; k++)
        {
            Assert.Equal(expected[k], Entry(entries, k).RiskPercent, Tolerance);
        }

        Assert.Equal(5, Entry(entries, 4).Position);
        Assert.Equal(0, Entry(entries, 4).Step);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_WinFromPosition5_RaisesFOnlyWhenEIsNotZero()
    {
        // C, D, E raised by wins at positions 1, 2, 3; then L L L L W: the win at position 5 raises F.
        List<LadderEntry> entries = Sequence("WLWLLWLLLLW");

        Assert.Equal(new[] { 1, 1, 1, 1 }, Columns(NextAfter(entries, entries[10].ClosedUtc!.Value)));

        // Without E the same win moves back to E.
        List<LadderEntry> noE = Sequence("WLWLLLLW");
        Assert.Equal(new[] { 1, 1, 1, 0 }, Columns(NextAfter(noE, noE[7].ClosedUtc!.Value)));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_BreakEven_IsNotAWin_DealsGoOnAndThePositionRises()
    {
        LadderDecision breakEven = NextAfter(Sequence("WB"), H(3.0));
        LadderDecision win = NextAfter(Sequence("WW"), H(3.0));

        Assert.Equal(0.159610, breakEven.RiskPercent, Tolerance);
        Assert.Equal(2, breakEven.Position);
        Assert.Equal(new[] { 1, 0, 0, 0 }, Columns(breakEven));
        Assert.Equal(0.225326, win.RiskPercent, Tolerance);
        Assert.Equal(1, win.Position);
    }

    [Theory]
    [Trait("Req", "RSK-013")]
    [InlineData(1e-9, false)]
    [InlineData(2e-9, true)]
    [InlineData(0.01, true)]
    [InlineData(-0.01, false)]
    public void Next_WinIsANetAboveZeroBeyond1e9(double net, bool isWin)
    {
        var entries = new List<LadderEntry> { Closed("A", 0.0, 1.0, net) };

        LadderDecision next = Sizer().Next(entries, H(2.0));

        Assert.Equal(isWin ? 1 : 0, next.Columns[0]);
        Assert.Equal(isWin ? 1 : 2, next.Position);
    }

    // ------------------------------------------------------------------ reset only by RiskEpochDate

    /// <summary>Bot history for a pattern: trade i opens at 2i hours from <paramref name="start"/> and closes one hour later.</summary>
    private static List<ClosedTradeInfo> History(string pattern, DateTime start, string zonePrefix)
    {
        var history = new List<ClosedTradeInfo>();
        for (int i = 0; i < pattern.Length; i++)
        {
            string label = "ZFVG|v1|EURUSD|" + zonePrefix + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            double net = pattern[i] == 'W' ? 100.0 : -100.0;
            DateTime open = start.AddHours(2.0 * i);
            history.Add(RiskData.Closed(i + 1, "EURUSD", open, open.AddHours(1.0), net, 100000.0 + net, label));
        }

        return history;
    }

    /// <summary>The sizing at <paramref name="placedUtc"/> from the bot history closed by then, counted since <paramref name="epochStartUtc"/>.</summary>
    private static LadderDecision FromBroker(IEnumerable<ClosedTradeInfo> history, DateTime epochStartUtc, DateTime placedUtc)
    {
        ClosedTradeInfo[] known = history.Where(trade => trade.CloseTimeUtc <= placedUtc).ToArray();
        return Sizer().Next(LadderSizer.EntriesFromBroker(Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), known, epochStartUtc), placedUtc);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_ThreeWinsThenANewRiskEpochDate_StartAgainFromStep0()
    {
        // Three wins in January, the owner sets RiskEpochDate to 10 February, three wins from 10 February.
        DateTime newEpoch = CestClock.StartOfCestDateUtc(new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc));
        var history = History("WWW", T0, "A");
        history.AddRange(History("WWW", newEpoch.AddHours(10.0), "B"));
        double[] expected = { 0.08886, 0.141501, 0.225326, 0.08886, 0.141501, 0.225326 };
        double[] withoutNewEpoch = { 0.08886, 0.141501, 0.225326, 0.358809, 0.5, 0.5 };

        for (int k = 0; k < history.Count; k++)
        {
            DateTime placed = history[k].EntryTimeUtc;
            DateTime epoch = k < 3 ? RiskData.EpochStart : newEpoch;
            Assert.Equal(expected[k], FromBroker(history, epoch, placed).RiskPercent, Tolerance);
            Assert.Equal(withoutNewEpoch[k], FromBroker(history, RiskData.EpochStart, placed).RiskPercent, Tolerance);
        }
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_FreezeLiftsOnlyWithANewRiskEpochDate()
    {
        // W x 5 in January: the 5th (raw 0.571) freezes C at 4. A win on 10 February raises nothing; with
        // RiskEpochDate 10 February the next entry starts at step 0 and the win raises C again.
        DateTime newEpoch = CestClock.StartOfCestDateUtc(new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc));
        var history = History("WWWWW", T0, "A");
        history.AddRange(History("W", newEpoch.AddHours(10.0), "B"));
        DateTime later = newEpoch.AddHours(20.0);

        LadderDecision sameEpoch = FromBroker(history, RiskData.EpochStart, later);
        LadderDecision firstOfNewEpoch = FromBroker(history, newEpoch, newEpoch.AddHours(10.0));
        LadderDecision afterNewEpochWin = FromBroker(history, newEpoch, later);

        Assert.True(sameEpoch.StepsFrozen);
        Assert.Equal(new[] { 4, 0, 0, 0 }, Columns(sameEpoch));
        Assert.Equal(0.5, sameEpoch.RiskPercent);
        Assert.False(firstOfNewEpoch.StepsFrozen);
        Assert.Equal(0.08886, firstOfNewEpoch.RiskPercent, Tolerance);
        Assert.Equal(new[] { 1, 0, 0, 0 }, Columns(afterNewEpochWin));
        Assert.Equal(0.141501, afterNewEpochWin.RiskPercent, Tolerance);
    }

    public static IEnumerable<object[]> MonthStarts()
    {
        yield return new object[] { FebruaryUtc };
        yield return new object[] { JulyUtc };
    }

    [Theory]
    [Trait("Req", "RSK-013")]
    [MemberData(nameof(MonthStarts))]
    public void Next_MonthBoundaryAlone_ChangesNothing(DateTime monthStartUtc)
    {
        // W W W before 00:00 CE(S)T on the 1st (winter and summer time): C = 3 on both sides of the boundary.
        var entries = Sequence("WWW", monthStartUtc.AddHours(-10.0));

        LadderDecision before = Sizer().Next(entries, monthStartUtc.AddTicks(-1));
        LadderDecision at = Sizer().Next(entries, monthStartUtc);
        LadderDecision after = Sizer().Next(entries, monthStartUtc.AddDays(3.0));

        Assert.Equal(0.358809, before.RiskPercent, Tolerance);
        Assert.Equal(before.RiskPercent, at.RiskPercent);
        Assert.Equal(before.RiskPercent, after.RiskPercent);
        Assert.Equal(new[] { 3, 0, 0, 0 }, Columns(at));
        Assert.Equal(new[] { 3, 0, 0, 0 }, Columns(after));
    }

    [Theory]
    [Trait("Req", "RSK-013")]
    [MemberData(nameof(MonthStarts))]
    public void Next_FreezeAndOpenDeals_CarryAcrossTheMonthBoundary(DateTime monthStartUtc)
    {
        // W W W W, then the 5th entry (raw 0.571) is placed before the 1st and wins after it: frozen, C stays 4.
        var entries = Sequence("WWWW", monthStartUtc.AddHours(-20.0));
        entries.Add(new LadderEntry("X", monthStartUtc.AddHours(-10.0), monthStartUtc.AddHours(5.0), 100.0));

        LadderDecision next = NextAfter(entries, monthStartUtc.AddHours(5.0));

        Assert.True(next.StepsFrozen);
        Assert.Equal(new[] { 4, 0, 0, 0 }, Columns(next));
        Assert.Equal(0.5, next.RiskPercent);

        // W W (C = 2), then X at position 1 with step 2 is placed before the 1st and still open: the next
        // entry after the 1st is position 2 with D = 0 and carries part 2 of X's deal.
        var open = Sequence("WW", monthStartUtc.AddHours(-10.0));
        open.Add(new LadderEntry("X", monthStartUtc.AddHours(-2.0), null, 0.0));

        LadderDecision afterOpen = NextAfter(open, monthStartUtc);

        Assert.Equal(0.201523, afterOpen.RiskPercent, Tolerance);
        Assert.Equal(new[] { 2, 0, 0, 0 }, Columns(afterOpen));
    }

    // ------------------------------------------------------------------ overlapping entries (other instances)

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_AnotherInstancesWin_EndsTheDealsAndRaisesAColumn()
    {
        // EURUSD entry A is open; GBPUSD entry B (position 2) wins: C = 1 (D moved back), A's deal ends.
        var won = new List<LadderEntry> { Open("EURUSD-A", 0.0), Closed("GBPUSD-B", 1.0, 2.0, 50.0) };
        var lost = new List<LadderEntry> { Open("EURUSD-A", 0.0), Closed("GBPUSD-B", 1.0, 2.0, -50.0) };

        LadderDecision afterWin = Sizer().Next(won, H(3.0));
        LadderDecision afterLoss = Sizer().Next(lost, H(3.0));

        Assert.Equal(0.141501, afterWin.RiskPercent, Tolerance);
        Assert.Equal(1, afterWin.Position);
        Assert.Equal(new[] { 1, 0, 0, 0 }, Columns(afterWin));
        Assert.Equal(0.199935, afterLoss.RiskPercent, Tolerance);
        Assert.Equal(3, afterLoss.Position);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_WinClosingAfterAnEntryIsPlaced_DoesNotChangeThatEntry()
    {
        // B is placed at 1 while A is open: position 2, step 0, its own sizing 0.13329.
        Assert.Equal(0.13329, Sizer().Next(new[] { Open("A", 0.0) }, H(1.0)).RiskPercent, Tolerance);

        // A then wins at 2 (C = 1); B keeps position 2, so its win at 3 raises D, not C.
        var entries = new List<LadderEntry> { Closed("A", 0.0, 2.0, 50.0), Closed("B", 1.0, 3.0, 50.0) };

        LadderDecision next = Sizer().Next(entries, H(4.0));

        Assert.Equal(new[] { 1, 1, 0, 0 }, Columns(next));
        Assert.Equal(0.141501, next.RiskPercent, Tolerance);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_CloseAtTheSameInstantAsAPlacement_CountsAsBeforeIt()
    {
        // A wins at hour 2. B placed exactly at 2 comes after the win: position 1, step 1, its deal goes on.
        var atWin = new List<LadderEntry> { Closed("A", 0.0, 2.0, 50.0), Open("B", 2.0) };
        var tickBefore = new List<LadderEntry> { Closed("A", 0.0, 2.0, 50.0), new LadderEntry("B", H(2.0).AddTicks(-1), null, 0.0) };

        Assert.Equal(1, Sizer().Next(new[] { Closed("A", 0.0, 2.0, 50.0) }, H(2.0)).Position);
        Assert.Equal(0.141501, Sizer().Next(new[] { Closed("A", 0.0, 2.0, 50.0) }, H(2.0)).RiskPercent, Tolerance);
        Assert.Equal(0.159610, Sizer().Next(atWin, H(3.0)).RiskPercent, Tolerance);
        Assert.Equal(2, Sizer().Next(atWin, H(3.0)).Position);
        Assert.Equal(0.141501, Sizer().Next(tickBefore, H(3.0)).RiskPercent, Tolerance);
        Assert.Equal(1, Sizer().Next(tickBefore, H(3.0)).Position);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_TwoWinsAtTheSameInstant_RaiseInEntryOrder()
    {
        // A (position 1) first: C = 1; then B (position 2): D = 1. The other order would give C = 2.
        var entries = new List<LadderEntry> { Closed("A", 0.0, 2.0, 50.0), Closed("B", 1.0, 2.0, 50.0) };

        Assert.Equal(new[] { 1, 1, 0, 0 }, Columns(Sizer().Next(entries, H(3.0))));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_EntryClosingAtItsOwnPlacementInstant_ClosesRightAfterIt()
    {
        // B is placed and wins at hour 1 (position 2, moved back to C); A's and B's deals end.
        var entries = new List<LadderEntry> { Open("A", 0.0), Closed("B", 1.0, 1.0, 50.0) };

        LadderDecision next = Sizer().Next(entries, H(1.0));

        Assert.Equal(new[] { 1, 0, 0, 0 }, Columns(next));
        Assert.Equal(0.141501, next.RiskPercent, Tolerance);
    }

    // ------------------------------------------------------------------ restart, disabled, invalid

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_SameBrokerDataAfterARestart_GivesTheSamePercentAndColumns()
    {
        string pattern = "WWLLLWWLLW";
        var history = new List<ClosedTradeInfo>();
        for (int i = 0; i < pattern.Length; i++)
        {
            string label = "ZFVG|v1|EURUSD|Z" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            double net = pattern[i] == 'W' ? 100.0 : -100.0;
            history.Add(RiskData.Closed(i + 1, "EURUSD", H(2.0 * i), H((2.0 * i) + 1.0), net, 100000.0 + net, label));
        }

        LadderDecision first = Sizer().Next(LadderSizer.EntriesFromBroker(Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart), H(20.0));
        history.Reverse();
        LadderDecision restarted = new LadderSizer(Ladder).Next(LadderSizer.EntriesFromBroker(Array.Empty<PositionInfo>(), Array.Empty<PendingOrderInfo>(), history, RiskData.EpochStart), H(20.0));

        Assert.Equal(OwnersExample[10], first.RiskPercent, Tolerance);
        Assert.Equal(new[] { 3, 1, 1, 0 }, Columns(first));
        Assert.Equal(first.RiskPercent, restarted.RiskPercent);
        Assert.Equal(first.RawPercent, restarted.RawPercent);
        Assert.Equal(first.Position, restarted.Position);
        Assert.Equal(first.Step, restarted.Step);
        Assert.Equal(Columns(first), Columns(restarted));
        Assert.Equal(first.StepsFrozen, restarted.StepsFrozen);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void NextRiskPercent_LadderDisabled_IsTheFixedRiskPercent()
    {
        RiskParameters fixedRisk = Ladder with { LadderEnabled = false, RiskPercent = 0.43 };
        List<LadderEntry> entries = Sequence("WWLLLWWLLW");

        Assert.Equal(0.43, Sizer(fixedRisk).NextRiskPercent(entries, H(20.0)));
        Assert.Equal(OwnersExample[10], Sizer(fixedRisk).Next(entries, H(20.0)).RiskPercent, Tolerance);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void Next_InvalidTimes_Throw()
    {
        LadderSizer sizer = Sizer();
        var entries = new List<LadderEntry> { Closed("A", 0.0, 2.0, 50.0) };

        Assert.Throws<ArgumentException>(() => sizer.Next(entries, DateTime.SpecifyKind(H(3.0), DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => sizer.Next(entries, H(1.0)));
        Assert.Throws<ArgumentException>(() => sizer.Next(new[] { Open("A", 2.0) }, H(1.0)));
        Assert.Throws<ArgumentException>(() => sizer.NextRiskPercent(entries, H(1.0)));
        Assert.Equal(0.141501, sizer.Next(entries, H(2.0)).RiskPercent, Tolerance);
        Assert.Equal(0.13329, sizer.Next(new[] { Open("A", 2.0) }, H(2.0)).RiskPercent, Tolerance);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void State_EventsOutOfOrderOrABadPosition_Throw()
    {
        LadderState state = Sizer().StartState().Place(H(1.0), out LadderDecision placed);

        Assert.Equal(1, placed.Position);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Close(0, H(2.0), 50.0));
        Assert.Throws<InvalidOperationException>(() => state.Close(1, H(0.5), 50.0));
        Assert.Throws<InvalidOperationException>(() => state.Place(H(0.5), out _));
        Assert.Throws<InvalidOperationException>(() => state.Preview(H(0.5)));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void State_IsImmutable_EveryEventGivesANewState()
    {
        LadderState start = Sizer().StartState();
        LadderState placed = start.Place(H(0.0), out LadderDecision first);
        LadderState won = placed.Close(first.Position, H(1.0), 50.0);
        LadderState lost = placed.Close(first.Position, H(1.0), -50.0);

        Assert.Equal(0.08886, start.Preview(H(2.0)).RiskPercent, Tolerance);
        Assert.Equal(0.13329, placed.Preview(H(2.0)).RiskPercent, Tolerance);
        Assert.Equal(0.13329, lost.Preview(H(2.0)).RiskPercent, Tolerance);
        Assert.Equal(0.141501, won.Preview(H(2.0)).RiskPercent, Tolerance);
        Assert.Equal(new[] { 0, 0, 0, 0 }, Columns(placed.Preview(H(2.0))));
        Assert.Equal(new[] { 1, 0, 0, 0 }, Columns(won.Preview(H(2.0))));
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void State_InfiniteRawPercent_IsCappedAndFreezes()
    {
        // B (position 2) wins first and moves back to C; A (position 1) then raises C to 2 with no entry in
        // between, so the next deal is 0.30 x (1e300)^2, which is infinite.
        LadderState state = Sizer(Ladder with { LadderGrowth = 1e300, MaxRiskPercentPerTrade = 100.0 }).StartState();
        state = state.Place(H(0.0), out LadderDecision a);
        state = state.Place(H(1.0), out LadderDecision b);
        state = state.Close(b.Position, H(2.0), 50.0);
        state = state.Close(a.Position, H(3.0), 50.0);

        state = state.Place(H(4.0), out LadderDecision decision);

        Assert.Equal(2, decision.Step);
        Assert.True(double.IsPositiveInfinity(decision.RawPercent));
        Assert.Equal(100.0, decision.RiskPercent);
        Assert.True(state.Preview(H(5.0)).StepsFrozen);
    }

    [Fact]
    [Trait("Req", "RSK-013")]
    public void LadderDecision_CopiesTheColumns()
    {
        int[] columns = { 1, 2, 3, 4 };
        var decision = new LadderDecision(0.1, 0.2, 1, 1, columns, false);
        columns[0] = 9;

        Assert.Equal(new[] { 1, 2, 3, 4 }, Columns(decision));
        Assert.Throws<ArgumentNullException>(() => new LadderDecision(0.1, 0.2, 1, 1, null!, false));
    }
}
