using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Simulation;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-001: the backtest trade-log line built from a closed bot trade. The reference trade is a
/// EURUSD buy filled at 1.10002 with comment stop 1.098, target 1.104 and risk 500.00; it closes at
/// its target 1.104 for a net 993.00 after a commission of -7.00 and swap -0.50, which is 1.986 R.
/// </summary>
public class TradeLogLineTests
{
    private const string Label = "ZFVG|v1|EURUSD|D-20260114-0400";
    private const string Comment = "e=1.10000;s=1.09800;t=1.10400;r=500.00;h=0123abcd";

    private static readonly DateTime Open = new(2026, 1, 14, 10, 15, 0, 250, DateTimeKind.Utc);
    private static readonly DateTime Close = new(2026, 1, 14, 14, 45, 30, 999, DateTimeKind.Utc);

    private static ClosedTradeInfo Trade(double net = 993.0, double commission = -7.0, string label = Label)
    {
        return new ClosedTradeInfo(1001, "EURUSD", label, Open, Close, net, commission, -0.5, 100000.0 + net)
        {
            VolumeUnits = 250000.0,
            ClosingPrice = 1.104,
            ExitReason = TradeExitReason.TakeProfit,
        };
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Build_ClosedBotTrade_GivesEveryColumnOfTheTradeLog()
    {
        TradeLogRow row = TradeLogLine.Build(Trade(), Side.Buy, 1.10002, Comment);

        Assert.Null(row.Problem);
        TradeRecord record = row.Record!;
        Assert.Equal("EURUSD", record.Symbol);
        Assert.Equal("D-20260114-0400", record.ZoneId);
        Assert.Equal(new DateTime(2026, 1, 14, 10, 15, 0, DateTimeKind.Utc), record.OpenTimeUtc);
        Assert.Equal(new DateTime(2026, 1, 14, 14, 45, 30, DateTimeKind.Utc), record.CloseTimeUtc);
        Assert.Equal(Side.Buy, record.Side);
        Assert.Equal(250000.0, record.VolumeUnits);
        Assert.Equal(1.10002, record.Entry);
        Assert.Equal(1.098, record.Stop);
        Assert.Equal(1.104, record.Target);
        Assert.Equal(1.104, record.ExitPrice);
        Assert.Equal("TakeProfit", record.ExitReason);
        Assert.Equal(993.0, record.ResultMoney);
        Assert.Equal(1.986, record.ResultR, 12);
        Assert.Equal(-7.0, record.Commission);
        Assert.Equal(-0.5, record.Swap);
        Assert.Equal(500.0, record.RiskMoney);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Fields_ValidRow_WritesSeqRowAndNoProblemThatTheTradeLogReaderAccepts()
    {
        TradeLogRow row = TradeLogLine.Build(Trade(), Side.Buy, 1.10002, Comment);

        IReadOnlyList<(string Key, string? Value)> fields = TradeLogLine.Fields(7, row);

        Assert.Equal(new[] { "seq", "row", "problem" }, fields.Select(f => f.Key).ToArray());
        Assert.Equal("7", fields[0].Value);
        Assert.Equal(
            "EURUSD,D-20260114-0400,2026-01-14T10:15:00Z,2026-01-14T14:45:30Z,Buy,250000,1.10002,1.098,1.104,1.104,TakeProfit,993,1.986,-7,-0.5,500",
            fields[1].Value);
        Assert.Equal("none", fields[2].Value);
        TradeLogReadResult read = TradeLogCsv.Read(TradeLogCsv.Header + "\n" + fields[1].Value + "\n");
        Assert.True(read.IsValid, read.Error);
        Assert.Equal(row.Record, Assert.Single(read.Records));
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Build_SellLoser_UsesTheCommentStopAndANegativeR()
    {
        const string sellComment = "e=1.10000;s=1.10200;t=1.09600;r=500.00;h=0123abcd";
        ClosedTradeInfo trade = Trade(-507.5) with { ClosingPrice = 1.102, ExitReason = TradeExitReason.StopLoss };

        TradeLogRow row = TradeLogLine.Build(trade, Side.Sell, 1.1, sellComment);

        TradeRecord record = row.Record!;
        Assert.Equal(Side.Sell, record.Side);
        Assert.Equal(1.102, record.Stop);
        Assert.Equal(1.096, record.Target);
        Assert.Equal("StopLoss", record.ExitReason);
        Assert.Equal(-1.015, record.ResultR, 12);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Build_LabelNotOfTheBot_GivesAProblemAndNoRecord()
    {
        TradeLogRow row = TradeLogLine.Build(Trade(label: "manual"), Side.Buy, 1.10002, Comment);

        Assert.Null(row.Record);
        Assert.Contains("is not a bot label", row.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Build_NoPlanKeptAtTheOpen_GivesAProblem(bool hasSide, bool hasEntry)
    {
        TradeLogRow row = TradeLogLine.Build(Trade(), hasSide ? Side.Buy : null, hasEntry ? 1.10002 : null, Comment);

        Assert.Null(row.Record);
        Assert.Contains("no plan was kept", row.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData(null)]
    [InlineData("manual trade")]
    [InlineData("e=1.10000;s=1.09800;t=1.10400;r=0.00;h=0123abcd")]
    public void Build_CommentMissingOrWithoutRisk_GivesAProblem(string? comment)
    {
        TradeLogRow row = TradeLogLine.Build(Trade(), Side.Buy, 1.10002, comment);

        Assert.Null(row.Record);
        Assert.Contains("order comment", row.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Build_ClosingDataNotReported_GivesAProblem(bool hasVolume, bool hasPrice)
    {
        ClosedTradeInfo trade = Trade() with { VolumeUnits = hasVolume ? 250000.0 : null, ClosingPrice = hasPrice ? 1.104 : null };

        TradeLogRow row = TradeLogLine.Build(trade, Side.Buy, 1.10002, Comment);

        Assert.Null(row.Record);
        Assert.Contains("closed volume or the closing price", row.Problem, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Build_ValueTheTradeLogRejects_GivesTheColumnProblem()
    {
        TradeLogRow row = TradeLogLine.Build(Trade(commission: 7.0), Side.Buy, 1.10002, Comment);

        Assert.Null(row.Record);
        Assert.StartsWith("commission must be", row.Problem, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Fields_Problem_WritesRowNaAndTheProblem()
    {
        IReadOnlyList<(string Key, string? Value)> fields = TradeLogLine.Fields(2, new TradeLogRow(null, "no data"));

        Assert.Equal("2", fields[0].Value);
        Assert.Equal("NA", fields[1].Value);
        Assert.Equal("no data", fields[2].Value);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void BuildAndFields_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => TradeLogLine.Build(null!, Side.Buy, 1.1, Comment));
        Assert.Throws<ArgumentNullException>(() => TradeLogLine.Fields(1, null!));
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void JournalLine_ValidRow_IsOneLineWithoutEscapes()
    {
        var sink = new RecordingSink();
        Journal journal = OpsData.NewJournal(sink);

        string line = journal.Write(Close, TradeLogLine.Category, TradeLogLine.Fields(1, TradeLogLine.Build(Trade(), Side.Buy, 1.10002, Comment)));

        Assert.Equal(
            "2026-01-14T14:45:30Z tradelog seq=1 row=EURUSD,D-20260114-0400,2026-01-14T10:15:00Z,2026-01-14T14:45:30Z,Buy,250000,1.10002,1.098,1.104,1.104,TakeProfit,993,1.986,-7,-0.5,500 problem=none",
            line);
        Assert.Equal("tradelog", Assert.Single(sink.Lines).Category);
    }
}
