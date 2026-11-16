using Xunit;
using ZoneFvg.Core.Simulation;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-001: a saved cTrader backtest log becomes a trade log. Trade lines are found after whatever
/// the platform puts before them; other lines are ignored; numbering must run 1, 2, 3 ... without
/// gap; a line pasted twice counts once; two runs in one file and lines the bot marked with a
/// problem are rejected.
/// </summary>
public class BacktestLogExtractTests
{
    internal const string Row1 = "EURUSD,D-20260114-0400,2026-01-14T10:15:00Z,2026-01-14T14:45:30Z,Buy,250000,1.10002,1.098,1.104,1.104,TakeProfit,993,1.986,-7,-0.5,500";
    internal const string Row2 = "EURUSD,S-20260115-0800,2026-01-15T10:00:00Z,2026-01-15T12:00:00Z,Sell,250000,1.1,1.102,1.096,1.102,StopLoss,-507.5,-1.015,-7,0,500";

    internal static string Line(int seq, string row, string problem = "none", string prefix = "")
    {
        return prefix + "2026-01-14T14:45:30Z tradelog seq=" + seq.ToString(System.Globalization.CultureInfo.InvariantCulture) + " row=" + row + " problem=" + problem;
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Extract_LogWithOtherLinesAndPlatformPrefixes_WritesTheRowsInOrderUnderTheHeader()
    {
        string log = "14.01.2026 10:00:00.000 | ZFVG adapter version 0.1.0\r\n"
            + Line(2, Row2, prefix: "15.01.2026 12:00:00.000\t") + "\r\n"
            + "2026-01-14T14:45:30Z trade symbol=EURUSD position=1\r\n"
            + Line(1, Row1, prefix: "14.01.2026 14:45:30.999 | ") + "\r\n";

        BacktestLogResult result = BacktestLogExtract.Extract(log);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(2, result.Trades);
        Assert.Equal(TradeLogCsv.Header + "\n" + Row1 + "\n" + Row2 + "\n", result.TradeLog);
        Assert.Equal(string.Empty, result.Error);
        TradeLogReadResult read = TradeLogCsv.Read(result.TradeLog);
        Assert.True(read.IsValid, read.Error);
        Assert.Equal(2, read.Records.Count);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Extract_LinePastedTwice_IsCountedOnce()
    {
        string log = Line(1, Row1) + "\n" + Line(1, Row1) + "\n" + Line(2, Row2);

        BacktestLogResult result = BacktestLogExtract.Extract(log);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(2, result.Trades);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Extract_SameNumberWithOtherContent_IsRejectedAsTwoRuns()
    {
        BacktestLogResult result = BacktestLogExtract.Extract(Line(1, Row1) + "\n" + Line(1, Row2));

        Assert.False(result.IsValid);
        Assert.Equal("Line 2: trade 1 appears twice with different content, so the file holds more than one backtest run. Save the log of one run only.", result.Error);
        Assert.Equal(string.Empty, result.TradeLog);
        Assert.Equal(0, result.Trades);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData(2, 3, "Trade 1 is missing (the next one found is 2)")]
    [InlineData(1, 3, "Trade 2 is missing (the next one found is 3)")]
    public void Extract_NumberMissing_IsRejectedAsIncomplete(int first, int second, string expected)
    {
        BacktestLogResult result = BacktestLogExtract.Extract(Line(first, Row1) + "\n" + Line(second, Row2));

        Assert.False(result.IsValid);
        Assert.StartsWith(expected, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Extract_LineWithProblem_IsRejectedWithTheBotsReason()
    {
        BacktestLogResult result = BacktestLogExtract.Extract(Line(1, "NA", "the\\sbroker\\sdid\\snot\\sreport"));

        Assert.False(result.IsValid);
        Assert.Equal("Line 1: the bot could not write trade 1: the broker did not report", result.Error);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData("tradelog seq=1 row=x")]
    [InlineData("tradelog seq=1 problem=none row=x")]
    [InlineData("tradelog seq=1 x=y problem=none")]
    [InlineData("tradelog seq=x row=y")]
    public void Extract_MalformedTradeLine_IsRejectedWithItsLineNumber(string line)
    {
        BacktestLogResult result = BacktestLogExtract.Extract("first line\n" + line);

        Assert.False(result.IsValid);
        Assert.Equal("Line 2: a trade line must read \"tradelog seq=<n> row=<row> problem=<text>\".", result.Error);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("x")]
    [InlineData("99999999999")]
    public void Extract_TradeNumberNotAWholeNumberFromOne_IsRejected(string seq)
    {
        BacktestLogResult result = BacktestLogExtract.Extract("tradelog seq=" + seq + " row=" + Row1 + " problem=none");

        Assert.False(result.IsValid);
        Assert.Equal("Line 1: the trade number \"" + seq + "\" is not a whole number from 1.", result.Error);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Extract_RowTheTradeLogRejects_NamesTheTradeAndTheColumn()
    {
        string bad = Row1.Replace(",-7,", ",7,", StringComparison.Ordinal);

        BacktestLogResult result = BacktestLogExtract.Extract(Line(1, bad));

        Assert.False(result.IsValid);
        Assert.StartsWith("Trade 1: commission must be", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData("")]
    [InlineData("ZFVG adapter version 0.1.0\nxtradelog seq=1 row=a problem=none\n")]
    public void Extract_NoTradeLine_IsRejected(string log)
    {
        BacktestLogResult result = BacktestLogExtract.Extract(log);

        Assert.False(result.IsValid);
        Assert.StartsWith("No trade lines (\"tradelog seq=\") were found.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Extract_MarkerInsideAWordThenAsAWord_UsesTheWholeWord()
    {
        BacktestLogResult result = BacktestLogExtract.Extract("xtradelog seq=9 " + Line(1, Row1));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(1, result.Trades);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Extract_Null_IsRejected()
    {
        BacktestLogResult result = BacktestLogExtract.Extract(null);

        Assert.False(result.IsValid);
        Assert.Equal("The log text is null.", result.Error);
    }
}
