using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-011: the signal line of a Choch signal has the keys of OPS-001 in the same order, with the
/// zone id and the zone's edges, NA for the gap edges, followed by kind=choch, level, sweep, target
/// (the source of SIG-040) and rr (the reward on the signal's own levels). Expected lines are written
/// by hand. The lines of the other kinds are pinned in <see cref="JournalTests"/> and
/// <see cref="BreakoutJournalTests"/> and stay unchanged.
/// </summary>
public class ChochJournalTests
{
    private static readonly Zone Supply = new("S-20260114-0400", ZoneType.Supply, 1.0995, 1.1015, OpsData.T0.AddHours(-6.0), 0.002, ZoneState.Touched, OpsData.T0.AddHours(-1.0), 1.101);

    private static readonly SymbolInfo Symbol = ExecutionData.EurUsd(spread: 0.0001);

    [Fact]
    [Trait("Req", "OPS-011")]
    public void Signal_TakenChochSell_WritesTheKeysOfOps001ThenKindLevelSweepTargetAndRr()
    {
        // The order's stop 1.1011 is the signal's 1.101 plus the spread (EXE-007); rr is measured on the
        // signal's own levels: (1.1 - 1.098) / (1.101 - 1.1) = 2.
        var order = new PlannedOrder(ChochExecutionData.Sell(), "EURUSD", Side.Sell, 1.1, 1.1011, 1.098, 90000.0, 495.0, 11.0, OpsData.T0.AddHours(2.0), 5, ExecutionData.Label(ExecutionData.SellZone), "c", OpsData.T0)
        {
            EntryType = EntryOrderType.Limit,
        };
        var sink = new RecordingSink();

        string line = OpsData.NewJournal(sink).Signal(SignalJournalEntry.Taken(order, Bias.Short, Supply, Symbol, NewsStatus.Available));

        Assert.Equal(
            "2026-01-14T10:00:00Z signal symbol=EURUSD result=taken bias=Short zone=S-20260114-0400 zoneProximal=1.0995 zoneDistal=1.1015"
            + " fvgUpper=NA fvgLower=NA entry=1.1 stop=1.1011 target=1.098 stopPips=11.0 volume=90000 spreadPips=1.0 costToRisk=0.091"
            + " news=Available skip=none kind=choch level=1.1 sweep=yes target=zone:D-20260113-1200 rr=2",
            line);
    }

    [Fact]
    [Trait("Req", "OPS-011")]
    public void Signal_SkippedChochBuy_TheRewardBelowTheMinimumIsWritten()
    {
        var skip = new SkipRecord("EURUSD", ExecutionData.BuyZone, SkipReason.RewardRiskTooLow, 1.5, 2.0, "x", OpsData.T0);

        string line = OpsData.NewJournal(new RecordingSink()).Signal(SignalJournalEntry.Skipped(ChochExecutionData.Buy(target: 1.1015, source: "swing:20260113-0800"), "EURUSD", Bias.Long, null, skip, null, null));

        Assert.Equal(
            "2026-01-14T10:00:00Z signal symbol=EURUSD result=skipped bias=Long zone=D-20260114-0400 zoneProximal=NA zoneDistal=NA"
            + " fvgUpper=NA fvgLower=NA entry=1.1 stop=1.099 target=1.1015 stopPips=NA volume=NA spreadPips=NA costToRisk=NA"
            + " news=NA skip=RewardRiskTooLow kind=choch level=1.1 sweep=no target=swing:20260113-0800 rr=1.5",
            line);
    }

    [Fact]
    [Trait("Req", "OPS-011")]
    public void Entry_OtherKinds_HaveNoChochFields_ChochWithoutStopDistance_RrNa()
    {
        var skip = new SkipRecord("EURUSD", ExecutionData.BuyZone, SkipReason.InvalidSignal, 0.0, 0.0, "x", OpsData.T0);

        SignalJournalEntry zoneFvg = SignalJournalEntry.Skipped(ExecutionData.Buy(), "EURUSD", Bias.Long, null, skip, null, null);
        SignalJournalEntry flat = SignalJournalEntry.Skipped(ChochExecutionData.Buy(stop: 1.1), "EURUSD", Bias.Long, null, skip, null, null);

        Assert.Null(zoneFvg.Swept);
        Assert.Null(zoneFvg.TargetSource);
        Assert.Null(zoneFvg.RewardRisk);
        Assert.Null(flat.RewardRisk);
        Assert.EndsWith(" kind=choch level=1.1 sweep=no target=default rr=NA", OpsData.NewJournal(new RecordingSink()).Signal(flat), StringComparison.Ordinal);
        Assert.EndsWith(" skip=InvalidSignal", OpsData.NewJournal(new RecordingSink()).Signal(zoneFvg), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-011")]
    public void Signal_ChochWithoutSweepOrSource_WritesNa()
    {
        var skip = new SkipRecord("EURUSD", ExecutionData.BuyZone, SkipReason.InvalidSignal, 0.0, 0.0, "x", OpsData.T0);
        Signal bare = ChochExecutionData.Buy() with { Swept = null, TargetSource = null };

        string line = OpsData.NewJournal(new RecordingSink()).Signal(SignalJournalEntry.Skipped(bare, "EURUSD", Bias.Long, null, skip, null, null));

        Assert.EndsWith(" kind=choch level=1.1 sweep=NA target=NA rr=2", line, StringComparison.Ordinal);
    }
}
