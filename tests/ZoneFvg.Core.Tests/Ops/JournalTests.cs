using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-001 and OPS-002: golden-line tests of the journal. Every expected line is written out by
/// hand from the inputs; the values are chosen so that the numbers can be checked on paper.
/// </summary>
/// <remarks>
/// Reference order (ExecutionData): EURUSD buy at 1.1, stop 1.098 (20 pips), target 1.104, gap
/// 1.0995 to 1.1005, volume 250,000, risk money 500. Symbol spread 0.0001 (1.0 pip) and a
/// round-trip commission of 0.00007 USD per unit (0.7 pip), so cost-to-risk = 1.7 / 20 = 0.085.
/// </remarks>
public class JournalTests
{
    private const string ZoneId = ExecutionData.BuyZone;
    private const string Comment = "e=1.10000;s=1.09800;t=1.10400;r=500.00;h=0123abcd";

    private static readonly SymbolInfo Symbol = ExecutionData.EurUsd(spread: 0.0001, commissionPerUnit: 0.00007);

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Signal_TakenOrder_WritesEveryFieldOfSpec11InFixedOrder()
    {
        var sink = new RecordingSink();
        Journal journal = OpsData.NewJournal(sink);
        Signal signal = ExecutionData.Buy();
        var order = new PlannedOrder(signal, "EURUSD", Side.Buy, 1.1, 1.098, 1.104, 250000.0, 500.0, 20.0, OpsData.T0.AddHours(2.0), 5, ExecutionData.Label(ZoneId), Comment, OpsData.T0);
        Zone zone = ExecutionData.Zone(ZoneId, ZoneType.Demand, 1.099);

        string line = journal.Signal(SignalJournalEntry.Taken(order, Bias.Long, zone, Symbol, NewsStatus.Available));

        const string expected = "2026-01-14T10:00:00Z signal symbol=EURUSD result=taken bias=Long zone=D-20260114-0400 zoneProximal=1.099 zoneDistal=1.098"
            + " fvgUpper=1.1005 fvgLower=1.0995 entry=1.1 stop=1.098 target=1.104 stopPips=20.0 volume=250000 spreadPips=1.0 costToRisk=0.085 news=Available skip=none";
        Assert.Equal(expected, line);
        Assert.Equal(new[] { ("signal", expected) }, sink.Lines);
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Signal_SkippedWithSymbolData_WritesReasonAndSignalLevels()
    {
        var sink = new RecordingSink();
        Journal journal = OpsData.NewJournal(sink);
        var skip = new SkipRecord("EURUSD", ZoneId, SkipReason.SpreadTooWide, 1.6, 1.5, "spread 1.6 pips above 1.5", OpsData.T0.AddSeconds(30.0));

        string line = journal.Signal(SignalJournalEntry.Skipped(ExecutionData.Buy(), "EURUSD", Bias.Long, null, skip, Symbol, NewsStatus.Unavailable));

        Assert.Equal(
            "2026-01-14T10:00:30Z signal symbol=EURUSD result=skipped bias=Long zone=D-20260114-0400 zoneProximal=NA zoneDistal=NA"
            + " fvgUpper=1.1005 fvgLower=1.0995 entry=1.1 stop=1.098 target=1.104 stopPips=20.0 volume=NA spreadPips=1.0 costToRisk=0.085 news=Unavailable skip=SpreadTooWide",
            line);
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Signal_SkippedWithoutSymbolData_WritesNaForUnknownValues()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());
        var skip = new SkipRecord("EURUSD", ZoneId, SkipReason.BrokerDataInvalid, 0.0, 0.0, "x", OpsData.T0);

        string line = journal.Signal(SignalJournalEntry.Skipped(ExecutionData.Sell(), "EURUSD", Bias.Short, null, skip, null, null));

        Assert.Equal(
            "2026-01-14T10:00:00Z signal symbol=EURUSD result=skipped bias=Short zone=S-20260114-0400 zoneProximal=NA zoneDistal=NA"
            + " fvgUpper=1.1005 fvgLower=1.0995 entry=1.1 stop=1.102 target=1.096 stopPips=NA volume=NA spreadPips=NA costToRisk=NA news=NA skip=BrokerDataInvalid",
            line);
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Signal_SymbolWithZeroPipValue_GivesNaCostToRisk()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());
        var skip = new SkipRecord("EURUSD", ZoneId, SkipReason.MissingSymbolInfo, 0.0, 0.0, "x", OpsData.T0);
        SymbolInfo broken = Symbol with { PipValuePerUnit = 0.0 };

        string line = journal.Signal(SignalJournalEntry.Skipped(ExecutionData.Buy(), "EURUSD", Bias.Neutral, null, skip, broken, null));

        Assert.Contains(" stopPips=20.0 volume=NA spreadPips=1.0 costToRisk=NA ", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Skip_Record_WritesValuesEscapedAndCountsIt()
    {
        var sink = new RecordingSink();
        Journal journal = OpsData.NewJournal(sink);
        var skip = new SkipRecord("EURUSD", ZoneId, SkipReason.SpreadTooWide, 1.6, 1.5, "spread 1.6 pips above 1.5", OpsData.T0);

        string line = journal.Skip(skip);

        Assert.Equal("2026-01-14T10:00:00Z skip symbol=EURUSD zone=D-20260114-0400 reason=SpreadTooWide measured=1.6000 limit=1.5000 detail=spread\\s1.6\\spips\\sabove\\s1.5", line);
        Assert.Equal("skip", sink.Lines.Single().Category);
        DateTime dayStart = new(2026, 1, 13, 23, 0, 0, DateTimeKind.Utc);
        Assert.Equal(1, journal.CountersFor(dayStart).Skips);
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void State_Change_WritesFromToAndReason()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());

        Assert.Equal("2026-01-14T10:00:00Z state from=Running to=Halted reason=kill\\sswitch", journal.State(OpsData.T0, BotState.Running, BotState.Halted, "kill switch"));
        Assert.Equal("2026-01-14T10:00:00Z state from=Halted to=Running reason=", journal.State(OpsData.T0, BotState.Halted, BotState.Running, null));
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Recovery_Report_WritesEveryCountInFixedOrder()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());
        var entry = new RecoveryJournalEntry(OpsData.T0, "EURUSD", "start", 1, 2, 3, 0, 7, TimeSpan.FromMinutes(95.5), BotState.Running);

        Assert.Equal(
            "2026-01-14T10:00:00Z recovery symbol=EURUSD trigger=start adopted=1 kept=2 cancelled=3 strangers=0 zones=7 downtime=01:35:30 state=Running",
            journal.Recovery(entry));
        Assert.Contains(" downtime=NA ", journal.Recovery(entry with { Downtime = null }), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Error_Exception_WritesTypeAndMessage()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());

        string line = journal.Error(OpsData.T0, "OnBarClosed", new InvalidOperationException("bad bar"));

        Assert.Equal("2026-01-14T10:00:00Z error source=OnBarClosed error=InvalidOperationException:\\sbad\\sbar", line);
        Assert.Equal("2026-01-14T10:00:00Z error source= error=", journal.Error(OpsData.T0, null, (string?)null));
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Write_SinkThrows_NeverThrowsAndCountsFailure()
    {
        var sink = new RecordingSink { ThrowMessage = "disk full" };
        Journal journal = OpsData.NewJournal(sink);

        string line = journal.State(OpsData.T0, BotState.Running, BotState.Blocked, "calendar");

        Assert.StartsWith("2026-01-14T10:00:00Z state ", line, StringComparison.Ordinal);
        Assert.Equal(1, journal.WriteFailures);
        Assert.Equal("InvalidOperationException: disk full", journal.LastWriteFailure);
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Write_NullArguments_Throw()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());

        Assert.Throws<ArgumentNullException>(() => journal.Signal(null!));
        Assert.Throws<ArgumentNullException>(() => journal.Skip(null!));
        Assert.Throws<ArgumentNullException>(() => journal.Trade(null!));
        Assert.Throws<ArgumentNullException>(() => journal.Recovery(null!));
        Assert.Throws<ArgumentNullException>(() => journal.Error(OpsData.T0, "x", (Exception)null!));
        Assert.Throws<ArgumentNullException>(() => journal.Write(OpsData.T0, null!, Array.Empty<(string, string?)>()));
        Assert.Throws<ArgumentNullException>(() => journal.Write(OpsData.T0, "x", null!));
        Assert.Throws<ArgumentNullException>(() => new Journal(null!, new SecretMasker(Array.Empty<string>())));
        Assert.Throws<ArgumentNullException>(() => new Journal(new RecordingSink(), null!));
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Escape_SpecialCharacters_AreWrittenAsEscapes()
    {
        Assert.Equal("a\\sb\\\\c\\nd\\te\\rf=g", JournalFormat.Escape("a b\\c\nd\te\rf=g"));
        Assert.Equal("x\\u0001y\\u00A0z", JournalFormat.Escape("x\u0001y z"));
        Assert.Equal("NA", JournalFormat.Escape(null));
        Assert.Equal(string.Empty, JournalFormat.Escape(string.Empty));
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void Numbers_FormattedInvariantWithoutNegativeZero()
    {
        Assert.Equal("0.00", JournalFormat.Fixed(-0.001, 2));
        Assert.Equal("-0.01", JournalFormat.Fixed(-0.01, 2));
        Assert.Equal("1234.50", JournalFormat.Fixed(1234.5, 2));
        Assert.Equal("NA", JournalFormat.Fixed(null, 2));
        Assert.Equal("NA", JournalFormat.Fixed(double.NaN, 2));
        Assert.Equal("NA", JournalFormat.Fixed(double.PositiveInfinity, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => JournalFormat.Fixed(1.0, 11));
        Assert.Throws<ArgumentOutOfRangeException>(() => JournalFormat.Fixed(1.0, -1));
        Assert.Equal("1.1", JournalFormat.Price(1.10000));
        Assert.Equal("0", JournalFormat.Price(-0.0));
        Assert.Equal("123.45678901", JournalFormat.Price(123.456789012));
        Assert.Equal("NA", JournalFormat.Price(null));
        Assert.Equal("NA", JournalFormat.Price(double.NaN));
        Assert.Equal("-5", JournalFormat.Integer(-5));
    }

    [Fact]
    [Trait("Req", "OPS-001")]
    public void TimeAndDuration_FormattedToWholeSeconds()
    {
        Assert.Equal("2026-03-29T00:59:59Z", JournalFormat.Time(new DateTime(2026, 3, 29, 0, 59, 59, 999, DateTimeKind.Utc)));
        Assert.Equal("1.02:03:04", JournalFormat.Duration(new TimeSpan(1, 2, 3, 4, 900)));
        Assert.Equal("00:00:00", JournalFormat.Duration(TimeSpan.Zero));
        Assert.Equal("NA", JournalFormat.Duration(null));
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Hash8_KnownText_GivesFirstEightHexOfSha256()
    {
        // SHA-256("abc") = ba7816bf 8f01cfea ... (FIPS 180-2 test vector).
        Assert.Equal("ba7816bf", JournalFormat.Hash8("abc"));
        // SHA-256("") = e3b0c442 98fc1c14 ...
        Assert.Equal("e3b0c442", JournalFormat.Hash8(null));
    }

    // ---------------------------------------------------------------- OPS-002 trades

    private static ClosedTradeInfo Closed(double net, double commission = -7.0, double swap = 0.0, long id = 1001, string label = "ZFVG|v1|EURUSD|D-20260114-0400")
    {
        return new ClosedTradeInfo(id, "EURUSD", label, OpsData.T0.AddMinutes(15.0), new DateTime(2026, 1, 14, 14, 45, 30, DateTimeKind.Utc), net, commission, swap, 100000.0 + net);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Trade_WinnerWithComment_WritesEveryFieldOfSpec11()
    {
        var sink = new RecordingSink();
        Journal journal = OpsData.NewJournal(sink);
        var entry = new TradeJournalEntry(Closed(993.0), Comment, Side.Buy, 1.09998, 0.0001, TradeExitReason.TakeProfit, 0.25, 2.1);

        string line = journal.Trade(entry);

        // r = 993 / 500 = 1.986; slippage (1.09998 - 1.1) / 0.0001 = -0.2 (better than requested); 10:15:00 to 14:45:30.
        Assert.Equal(
            "2026-01-14T14:45:30Z trade symbol=EURUSD position=1001 label=ZFVG|v1|EURUSD|D-20260114-0400 side=Buy requested=1.1 filled=1.09998"
            + " slippagePips=-0.2 exit=TakeProfit r=1.99 money=993.00 commission=-7.00 swap=0.00 duration=04:30:30 mae=0.25 mfe=2.10",
            line);
        Assert.Equal("trade", sink.Lines.Single().Category);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Trade_LoserSell_ComputesRAndSlippageFromComment()
    {
        var entry = new TradeJournalEntry(Closed(-507.0, -7.0, -1.5), Comment, Side.Sell, 1.09997, 0.0001, TradeExitReason.StopLoss, null, null);

        Assert.Equal(-1.014, entry.ResultR!.Value, 9);
        // Sell: (requested - filled) / pip = (1.1 - 1.09997) / 0.0001 = +0.3, worse for a sell.
        Assert.Equal(0.3, entry.SlippagePips!.Value, 9);
        string line = OpsData.NewJournal(new RecordingSink()).Trade(entry);
        Assert.Contains(" side=Sell requested=1.1 filled=1.09997 slippagePips=0.3 exit=StopLoss r=-1.01 money=-507.00 commission=-7.00 swap=-1.50 ", line, StringComparison.Ordinal);
        Assert.EndsWith(" mae=NA mfe=NA", line, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "OPS-002")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("manual trade")]
    [InlineData("e=1.10000;s=1.09800;t=1.10400;r=0.00;h=0123abcd")]
    [InlineData("e=1.10000;s=1.09800;t=1.10400;h=0123abcd")]
    public void Trade_CommentMissingOrWithoutRisk_WritesRNaWithoutCrash(string? comment)
    {
        var entry = new TradeJournalEntry(Closed(993.0), comment, Side.Buy, 1.1, 0.0001, TradeExitReason.Unknown, null, null);

        string line = OpsData.NewJournal(new RecordingSink()).Trade(entry);

        Assert.Null(entry.ResultR);
        Assert.Contains(" exit=Unknown r=NA money=993.00 ", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Trade_UnknownSideFillOrPip_WritesNaSlippage()
    {
        Assert.Null(new TradeJournalEntry(Closed(1.0), Comment, null, 1.1, 0.0001, TradeExitReason.ClosedByBot, null, null).SlippagePips);
        Assert.Null(new TradeJournalEntry(Closed(1.0), Comment, Side.Buy, null, 0.0001, TradeExitReason.ClosedByBot, null, null).SlippagePips);
        Assert.Null(new TradeJournalEntry(Closed(1.0), Comment, Side.Buy, 1.1, null, TradeExitReason.ClosedByBot, null, null).SlippagePips);
        Assert.Null(new TradeJournalEntry(Closed(1.0), Comment, Side.Buy, 1.1, 0.0, TradeExitReason.ClosedByBot, null, null).SlippagePips);
        Assert.Null(new TradeJournalEntry(Closed(1.0), null, Side.Buy, 1.1, 0.0001, TradeExitReason.ClosedByBot, null, null).SlippagePips);
        string line = OpsData.NewJournal(new RecordingSink()).Trade(new TradeJournalEntry(Closed(1.0), Comment, null, null, null, TradeExitReason.ClosedOutside, null, null));
        Assert.Contains(" side=NA requested=1.1 filled=NA slippagePips=NA exit=ClosedOutside ", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Trade_NotFiniteResult_GivesRNa()
    {
        Assert.Null(new TradeJournalEntry(Closed(double.NaN), Comment, Side.Buy, 1.1, 0.0001, TradeExitReason.StopOut, null, null).ResultR);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Trade_Lines_AreCountedPerCestDayOfClose()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());
        journal.Trade(new TradeJournalEntry(Closed(993.0, id: 1), Comment, Side.Buy, 1.1, 0.0001, TradeExitReason.TakeProfit, null, null));
        journal.Trade(new TradeJournalEntry(Closed(-507.0, id: 2), Comment, Side.Buy, 1.1, 0.0001, TradeExitReason.StopLoss, null, null));
        journal.Trade(new TradeJournalEntry(Closed(10.0, id: 3), null, Side.Buy, 1.1, 0.0001, TradeExitReason.ClosedOutside, null, null));

        // Closed at 14:45:30 UTC on 14 January: the CE(S)T day starting 2026-01-13 23:00 UTC.
        JournalDayCounters day = journal.CountersFor(new DateTime(2026, 1, 13, 23, 0, 0, DateTimeKind.Utc));
        Assert.Equal(3, day.Trades);
        Assert.Equal(2, day.TradesWithR);
        Assert.Equal(1.986 - 1.014, day.NetR, 9);
        Assert.Equal(0, journal.CountersFor(new DateTime(2026, 1, 14, 23, 0, 0, DateTimeKind.Utc)).Trades);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Counters_KeepOnlyTheMostRecentEightDays()
    {
        Journal journal = OpsData.NewJournal(new RecordingSink());
        for (int day = 1; day <= 10; day++)
        {
            journal.Skip(new SkipRecord("EURUSD", ZoneId, SkipReason.NewsWindow, 0.0, 0.0, "x", new DateTime(2026, 1, day, 12, 0, 0, DateTimeKind.Utc)));
        }

        // Day starts are 23:00 UTC of the previous date (CET). Days 1 and 2 were dropped, day 3 kept.
        Assert.Equal(0, journal.CountersFor(new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc)).Skips);
        Assert.Equal(1, journal.CountersFor(new DateTime(2026, 1, 2, 23, 0, 0, DateTimeKind.Utc)).Skips);
        Assert.Equal(1, journal.CountersFor(new DateTime(2026, 1, 9, 23, 0, 0, DateTimeKind.Utc)).Skips);
        // A record with a time that is not UTC is journaled but not counted.
        journal.Skip(new SkipRecord("EURUSD", ZoneId, SkipReason.NewsWindow, 0.0, 0.0, "x", new DateTime(2026, 1, 10, 13, 0, 0, DateTimeKind.Unspecified)));
        Assert.Equal(1, journal.CountersFor(new DateTime(2026, 1, 9, 23, 0, 0, DateTimeKind.Utc)).Skips);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Excursion_BuyAndSell_InRFromBars()
    {
        DateTime open = OpsData.T0;
        var bars = new[]
        {
            BarBuilder.One(TimeFrame.M15, open, 1.1, 1.1010, 1.0995, 1.1005),
            BarBuilder.One(TimeFrame.M15, open.AddMinutes(15.0), 1.1005, 1.1042, 1.1000, 1.1040),
        };

        // Buy at 1.1, stop 1.098 (0.002): adverse 1.1 - 1.0995 = 0.0005 = 0.25 R; favourable 1.1042 - 1.1 = 2.1 R.
        (double AdverseR, double FavourableR) buy = TradeExcursion.InR(Side.Buy, 1.1, 1.098, bars)!.Value;
        Assert.Equal(0.25, buy.AdverseR, 9);
        Assert.Equal(2.1, buy.FavourableR, 9);

        // Sell at 1.1, stop 1.102: adverse 1.1042 - 1.1 = 2.1 R; favourable 1.1 - 1.0995 = 0.25 R.
        (double AdverseR, double FavourableR) sell = TradeExcursion.InR(Side.Sell, 1.1, 1.102, bars)!.Value;
        Assert.Equal(2.1, sell.AdverseR, 9);
        Assert.Equal(0.25, sell.FavourableR, 9);
    }

    [Fact]
    [Trait("Req", "OPS-002")]
    public void Excursion_NoMoveNoBarsOrBadStop_GivesZeroOrNull()
    {
        var above = new[] { BarBuilder.One(TimeFrame.M15, OpsData.T0, 1.101, 1.102, 1.1005, 1.1015) };
        (double AdverseR, double FavourableR) buy = TradeExcursion.InR(Side.Buy, 1.1, 1.098, above)!.Value;
        Assert.Equal(0.0, buy.AdverseR);
        Assert.Equal(1.0, buy.FavourableR, 9);
        Assert.Null(TradeExcursion.InR(Side.Buy, 1.1, 1.098, Array.Empty<Bar>()));
        Assert.Null(TradeExcursion.InR(Side.Buy, 1.1, 1.1, above));
        Assert.Null(TradeExcursion.InR(Side.Sell, 1.1, 1.098, above));
        Assert.Throws<ArgumentNullException>(() => TradeExcursion.InR(Side.Buy, 1.1, 1.098, null!));
    }
}
