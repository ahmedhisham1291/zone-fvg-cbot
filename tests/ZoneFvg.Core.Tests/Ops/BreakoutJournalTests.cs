using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-010: the signal line of a breakout signal has the keys of OPS-001 in the same order, with
/// the breakout id as zone and NA for the zone and gap edges, followed by kind=breakout and
/// level. Expected lines are written by hand. Taken buy: order entry 1.0864, stop 1.0823, target
/// 1.0943 (41 pips), volume 121,000, risk 496.10; spread 1.0 pip and commission 0.7 pip, so
/// cost-to-risk = 1.7 / 41 = 0.0415, written 0.041. The ZoneFvg lines of OPS-001 are pinned in
/// <see cref="JournalTests"/> and stay unchanged.
/// </summary>
public class BreakoutJournalTests
{
    private const string BuyLine =
        "2026-01-14T10:00:00Z signal symbol=EURUSD result=taken bias=Long zone=BH-20260112-2200 zoneProximal=NA zoneDistal=NA"
        + " fvgUpper=NA fvgLower=NA entry=1.0864 stop=1.0823 target=1.0943 stopPips=41.0 volume=121000 spreadPips=1.0 costToRisk=0.041"
        + " news=Available skip=none kind=breakout level=1.085";

    private static readonly SymbolInfo Symbol = ExecutionData.EurUsd(spread: 0.0001, commissionPerUnit: 0.00007);

    private static PlannedOrder TakenBuy(Signal signal)
    {
        return new PlannedOrder(signal, "EURUSD", Side.Buy, 1.0864, 1.0823, 1.0943, 121000.0, 496.1, 41.0, OpsData.T0.AddHours(2.0), 5, ExecutionData.Label(signal.ZoneId), "e=1.08640;s=1.08230;t=1.09430;r=496.10;h=119a3864", OpsData.T0)
        {
            EntryType = EntryOrderType.Stop,
        };
    }

    [Fact]
    [Trait("Req", "OPS-010")]
    public void Signal_TakenBreakoutBuy_WritesTheKeysOfOps001ThenKindAndLevel()
    {
        var sink = new RecordingSink();
        Journal journal = OpsData.NewJournal(sink);

        string line = journal.Signal(SignalJournalEntry.Taken(TakenBuy(ExecutionData.BreakoutBuy()), Bias.Long, null, Symbol, NewsStatus.Available));

        Assert.Equal(BuyLine, line);
        Assert.Equal(new[] { ("signal", BuyLine) }, sink.Lines);
    }

    [Fact]
    [Trait("Req", "OPS-010")]
    public void Signal_SkippedBreakoutSellWithoutSymbolData_WritesTheSignalLevelsThenKindAndLevel()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());
        var skip = new SkipRecord("EURUSD", ExecutionData.BreakoutSellId, SkipReason.BrokerDataInvalid, 0.0, 0.0, "x", OpsData.T0);

        string line = journal.Signal(SignalJournalEntry.Skipped(ExecutionData.BreakoutSell(), "EURUSD", Bias.Short, null, skip, null, null));

        Assert.Equal(
            "2026-01-14T10:00:00Z signal symbol=EURUSD result=skipped bias=Short zone=BL-20260112-2200 zoneProximal=NA zoneDistal=NA"
            + " fvgUpper=NA fvgLower=NA entry=1.0767 stop=1.0807 target=1.0687 stopPips=NA volume=NA spreadPips=NA costToRisk=NA"
            + " news=NA skip=BrokerDataInvalid kind=breakout level=1.078",
            line);
    }

    [Fact]
    [Trait("Req", "OPS-010")]
    public void Signal_SkippedBreakoutWithSymbolData_HasTheSkipReasonBeforeKindAndLevel()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());
        var skip = new SkipRecord("EURUSD", ExecutionData.BreakoutBuyId, SkipReason.EntryAlreadyReached, 1.0864, 1.0864, "x", OpsData.T0);

        string line = journal.Signal(SignalJournalEntry.Skipped(ExecutionData.BreakoutBuy(level: 1.08505), "EURUSD", Bias.Long, null, skip, Symbol, NewsStatus.Available));

        Assert.EndsWith(" news=Available skip=EntryAlreadyReached kind=breakout level=1.08505", line, StringComparison.Ordinal);
        Assert.Contains(" stopPips=40.0 volume=NA spreadPips=1.0 costToRisk=", line, StringComparison.Ordinal);
        Assert.Contains(" fvgUpper=NA fvgLower=NA entry=1.0863 stop=1.0823 target=1.0943 ", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-010")]
    public void Entry_TakenAndSkipped_CarryKindAndLevelFromTheSignal()
    {
        var skip = new SkipRecord("EURUSD", ExecutionData.BreakoutSellId, SkipReason.BrokerDataInvalid, 0.0, 0.0, "x", OpsData.T0);

        SignalJournalEntry taken = SignalJournalEntry.Taken(TakenBuy(ExecutionData.BreakoutBuy()), Bias.Long, null, Symbol, NewsStatus.Available);
        SignalJournalEntry skipped = SignalJournalEntry.Skipped(ExecutionData.BreakoutSell(), "EURUSD", Bias.Short, null, skip, null, null);
        SignalJournalEntry zoneFvg = SignalJournalEntry.Skipped(ExecutionData.Buy(), "EURUSD", Bias.Long, null, skip, null, null);

        Assert.Equal(SignalKind.Breakout, taken.Kind);
        Assert.Equal(1.085, taken.Level);
        Assert.Null(taken.FvgUpper);
        Assert.Null(taken.FvgLower);
        Assert.Equal(SignalKind.Breakout, skipped.Kind);
        Assert.Equal(1.078, skipped.Level);
        Assert.Null(skipped.FvgUpper);
        Assert.Equal(SignalKind.ZoneFvg, zoneFvg.Kind);
        Assert.Null(zoneFvg.Level);
        Assert.Equal(1.1005, zoneFvg.FvgUpper);
        Assert.Equal(1.0995, zoneFvg.FvgLower);
    }

    [Fact]
    [Trait("Req", "OPS-010")]
    public void Signal_ZoneFvgEntry_HasNoKindAndNoLevelKey()
    {
        // Even a ZoneFvg entry that carries a level writes the line of OPS-001 byte for byte.
        Journal journal = OpsData.NewJournal(new RecordingSink());
        var skip = new SkipRecord("EURUSD", ExecutionData.BuyZone, SkipReason.SpreadTooWide, 1.6, 1.5, "x", OpsData.T0);
        SignalJournalEntry entry = SignalJournalEntry.Skipped(ExecutionData.Buy(), "EURUSD", Bias.Long, null, skip, Symbol, NewsStatus.Available) with { Level = 1.2 };

        string line = journal.Signal(entry);

        Assert.EndsWith(" news=Available skip=SpreadTooWide", line, StringComparison.Ordinal);
        Assert.DoesNotContain("kind=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("level=", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-010")]
    public void Signal_BreakoutEntryWithoutLevel_WritesLevelNa()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());
        var skip = new SkipRecord("EURUSD", ExecutionData.BreakoutBuyId, SkipReason.InvalidSignal, 0.0, 0.0, "x", OpsData.T0);

        string line = journal.Signal(SignalJournalEntry.Skipped(ExecutionData.BreakoutBuy(level: null), "EURUSD", Bias.Long, null, skip, null, null));

        Assert.EndsWith(" skip=InvalidSignal kind=breakout level=NA", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-010")]
    public void Signal_UnderACultureWithDecimalComma_StillUsesInvariantFormatting()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaCulture.NumberFormat.NumberDecimalSeparator = ",";
        try
        {
            CultureInfo.CurrentCulture = commaCulture;
            Journal journal = OpsData.NewJournal(new RecordingSink());

            string line = journal.Signal(SignalJournalEntry.Taken(TakenBuy(ExecutionData.BreakoutBuy()), Bias.Long, null, Symbol, NewsStatus.Available));

            Assert.Equal(BuyLine, line);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Req", "OPS-010")]
    public void Signal_UnderThaiCulture_TimeStaysGregorian()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            Journal journal = OpsData.NewJournal(new RecordingSink());

            string line = journal.Signal(SignalJournalEntry.Taken(TakenBuy(ExecutionData.BreakoutBuy()), Bias.Long, null, Symbol, NewsStatus.Available));

            Assert.Equal(BuyLine, line);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
