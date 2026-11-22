using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-010 (hardening of WP29): the signal line of a breakout signal writes NA for zoneProximal
/// and zoneDistal whatever zone the caller passes, so it equals, character for character, the line
/// written with no zone. A ZoneFvg signal with a zone still writes the zone's edges. The zone is
/// the demand zone of <see cref="JournalTests"/>: proximal 1.099, distal 1.098.
/// </summary>
public class BreakoutJournalZoneEdgeTests
{
    private static readonly SymbolInfo Symbol = ExecutionData.EurUsd(spread: 0.0001, commissionPerUnit: 0.00007);

    private static readonly Zone DemandZone = ExecutionData.Zone(ExecutionData.BuyZone, ZoneType.Demand, 1.099);

    private static readonly Zone SupplyZone = ExecutionData.Zone(ExecutionData.SellZone, ZoneType.Supply, 1.0775);

    private static PlannedOrder TakenBreakoutBuy()
    {
        return new PlannedOrder(ExecutionData.BreakoutBuy(), "EURUSD", Side.Buy, 1.0864, 1.0823, 1.0943, 121000.0, 496.1, 41.0, OpsData.T0.AddHours(2.0), 5, ExecutionData.Label(ExecutionData.BreakoutBuyId), "e=1.08640;s=1.08230;t=1.09430;r=496.10;h=119a3864", OpsData.T0)
        {
            EntryType = EntryOrderType.Stop,
        };
    }

    private static string Line(SignalJournalEntry entry)
    {
        return OpsData.NewJournal(new RecordingSink()).Signal(entry);
    }

    [Fact]
    [Trait("Req", "OPS-010")]
    public void Taken_BreakoutSignalWithAZonePassed_ZoneEdgesNaAndTheLineOfNoZone()
    {
        SignalJournalEntry withZone = SignalJournalEntry.Taken(TakenBreakoutBuy(), Bias.Long, DemandZone, Symbol, NewsStatus.Available);
        SignalJournalEntry withoutZone = SignalJournalEntry.Taken(TakenBreakoutBuy(), Bias.Long, null, Symbol, NewsStatus.Available);

        string line = Line(withZone);

        Assert.Null(withZone.ZoneProximal);
        Assert.Null(withZone.ZoneDistal);
        Assert.Contains(" zone=BH-20260112-2200 zoneProximal=NA zoneDistal=NA fvgUpper=NA ", line, StringComparison.Ordinal);
        Assert.Equal(Line(withoutZone), line);
        Assert.Equal(withoutZone, withZone);
    }

    [Theory]
    [Trait("Req", "OPS-010")]
    [InlineData(false)]
    [InlineData(true)]
    public void Skipped_BreakoutSignalWithAZonePassed_ZoneEdgesNaAndTheLineOfNoZone(bool sell)
    {
        Signal signal = sell ? ExecutionData.BreakoutSell() : ExecutionData.BreakoutBuy();
        Zone zone = sell ? SupplyZone : DemandZone;
        var skip = new SkipRecord("EURUSD", signal.ZoneId, SkipReason.SpreadTooWide, 1.6, 1.5, "x", OpsData.T0);

        SignalJournalEntry withZone = SignalJournalEntry.Skipped(signal, "EURUSD", sell ? Bias.Short : Bias.Long, zone, skip, Symbol, NewsStatus.Available);
        SignalJournalEntry withoutZone = SignalJournalEntry.Skipped(signal, "EURUSD", sell ? Bias.Short : Bias.Long, null, skip, Symbol, NewsStatus.Available);

        string line = Line(withZone);

        Assert.Null(withZone.ZoneProximal);
        Assert.Null(withZone.ZoneDistal);
        Assert.Contains(" zone=" + signal.ZoneId + " zoneProximal=NA zoneDistal=NA fvgUpper=NA ", line, StringComparison.Ordinal);
        Assert.Equal(Line(withoutZone), line);
        Assert.Equal(withoutZone, withZone);
    }

    [Fact]
    [Trait("Req", "OPS-010")]
    public void TakenAndSkipped_ZoneFvgSignalWithAZone_StillWriteTheZoneEdges()
    {
        var order = new PlannedOrder(ExecutionData.Buy(), "EURUSD", Side.Buy, 1.1, 1.098, 1.104, 250000.0, 500.0, 20.0, OpsData.T0.AddHours(2.0), 5, ExecutionData.Label(ExecutionData.BuyZone), "e=1.10000;s=1.09800;t=1.10400;r=500.00;h=0123abcd", OpsData.T0);
        var skip = new SkipRecord("EURUSD", ExecutionData.BuyZone, SkipReason.SpreadTooWide, 1.6, 1.5, "x", OpsData.T0);

        SignalJournalEntry taken = SignalJournalEntry.Taken(order, Bias.Long, DemandZone, Symbol, NewsStatus.Available);
        SignalJournalEntry skipped = SignalJournalEntry.Skipped(ExecutionData.Buy(), "EURUSD", Bias.Long, DemandZone, skip, Symbol, NewsStatus.Available);

        Assert.Equal((DemandZone.Proximal, DemandZone.Distal), (taken.ZoneProximal!.Value, taken.ZoneDistal!.Value));
        Assert.Equal((DemandZone.Proximal, DemandZone.Distal), (skipped.ZoneProximal!.Value, skipped.ZoneDistal!.Value));
        Assert.Contains(" zone=D-20260114-0400 zoneProximal=1.099 zoneDistal=1.098 fvgUpper=1.1005 fvgLower=1.0995 ", Line(taken), StringComparison.Ordinal);
        Assert.Contains(" zone=D-20260114-0400 zoneProximal=1.099 zoneDistal=1.098 fvgUpper=1.1005 fvgLower=1.0995 ", Line(skipped), StringComparison.Ordinal);
    }
}
