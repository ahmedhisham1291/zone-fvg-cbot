using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Simulation;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-001: one row per closed trade with symbol, zone id, open and close time (UTC), side,
/// volume, entry, stop, target, exit price, exit reason, result in money and in R, commission,
/// swap, and the risk in money that defines R. Row 1 is the header; the first trade is row 2.
/// </summary>
public class TradeLogCsvTests
{
    private const string Header = "symbol,zone_id,open_time_utc,close_time_utc,side,volume_units,entry,stop,target,exit_price,exit_reason,result_money,result_r,commission,swap,risk_money";

    // 995 / 500 = 1.99 R; a 20-pip buy with a 40-pip target.
    private const string BuyRow = "EURUSD,D-20260105-0800,2026-01-05T08:15:00Z,2026-01-05T14:30:00Z,Buy,250000,1.1,1.098,1.104,1.104,Target,995,1.99,-5,0,500";

    // -512.25 / 500 = -1.0245 R; a sell stopped out overnight with swap.
    private const string SellRow = "USDJPY,S-20260106-1200,2026-01-06T13:00:00Z,2026-01-07T02:45:00Z,Sell,100000,150.5,150.9,149.7,150.9,Stop,-512.25,-1.0245,-4.5,-7.75,500";

    private static string WithField(int index, string value)
    {
        string[] fields = BuyRow.Split(',');
        fields[index] = value;
        return string.Join(",", fields);
    }

    private static string Log(params string[] rows)
    {
        return Header + "\n" + string.Join("\n", rows) + "\n";
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Read_ValidLog_ReturnsEveryFieldOfEveryRow()
    {
        TradeLogReadResult result = TradeLogCsv.Read(Log(BuyRow, SellRow));

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(string.Empty, result.Error);
        Assert.Equal(2, result.Records.Count);
        TradeRecord buy = result.Records[0];
        Assert.Equal("EURUSD", buy.Symbol);
        Assert.Equal("EUR", buy.BaseCurrency);
        Assert.Equal("USD", buy.QuoteCurrency);
        Assert.Equal("D-20260105-0800", buy.ZoneId);
        Assert.Equal(SimData.Utc(2026, 1, 5, 8, 15), buy.OpenTimeUtc);
        Assert.Equal(DateTimeKind.Utc, buy.OpenTimeUtc.Kind);
        Assert.Equal(SimData.Utc(2026, 1, 5, 14, 30), buy.CloseTimeUtc);
        Assert.Equal(DateTimeKind.Utc, buy.CloseTimeUtc.Kind);
        Assert.Equal(Side.Buy, buy.Side);
        Assert.Equal(250000.0, buy.VolumeUnits);
        Assert.Equal(1.1, buy.Entry);
        Assert.Equal(1.098, buy.Stop);
        Assert.Equal(1.104, buy.Target);
        Assert.Equal(1.104, buy.ExitPrice);
        Assert.Equal("Target", buy.ExitReason);
        Assert.Equal(995.0, buy.ResultMoney);
        Assert.Equal(1.99, buy.ResultR);
        Assert.Equal(-5.0, buy.Commission);
        Assert.Equal(0.0, buy.Swap);
        Assert.Equal(500.0, buy.RiskMoney);

        TradeRecord sell = result.Records[1];
        Assert.Equal("USDJPY", sell.Symbol);
        Assert.Equal("USD", sell.BaseCurrency);
        Assert.Equal("JPY", sell.QuoteCurrency);
        Assert.Equal(Side.Sell, sell.Side);
        Assert.Equal(150.9, sell.Stop);
        Assert.Equal(149.7, sell.Target);
        Assert.Equal(-512.25, sell.ResultMoney);
        Assert.Equal(-1.0245, sell.ResultR);
        Assert.Equal(-4.5, sell.Commission);
        Assert.Equal(-7.75, sell.Swap);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void WriteThenRead_RecordsWithAwkwardNumbers_ReturnsEqualRecords()
    {
        var records = new List<TradeRecord>
        {
            SimData.Trade("EURUSD", SimData.Utc(2026, 3, 29, 0, 45), SimData.Utc(2026, 3, 30, 22, 0), 0.1 + 0.2, riskMoney: 333.33, commission: -0.1),
            SimData.Trade("GBPUSD.x", SimData.Utc(2026, 10, 25, 1, 0), SimData.Utc(2026, 10, 25, 1, 0), -1.0 / 3.0, riskMoney: 512.5, side: Side.Sell, zoneId: "S-20261020-0400"),
            SimData.Trade("AUDUSD", SimData.Utc(2026, 1, 2, 3, 4), SimData.Utc(2026, 1, 9, 5, 6), 1e-7),
        };

        string text = TradeLogCsv.Write(records);
        TradeLogReadResult result = TradeLogCsv.Read(text);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(records, result.Records);
        Assert.Equal(text, TradeLogCsv.Write(result.Records));
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Write_OneRecord_WritesHeaderAndInvariantRowWithLineFeeds()
    {
        TradeLogReadResult read = TradeLogCsv.Read(Log(SellRow));

        string text = TradeLogCsv.Write(read.Records);

        Assert.Equal(Header + "\n" + SellRow + "\n", text);
        Assert.Equal(Header, TradeLogCsv.Header);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Write_NoRecords_WritesTheHeaderOnly()
    {
        Assert.Equal(Header + "\n", TradeLogCsv.Write(Array.Empty<TradeRecord>()));
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Read_CrLfLineEndingsByteOrderMarkAndNoFinalNewline_AreAccepted()
    {
        TradeLogReadResult result = TradeLogCsv.Read("\uFEFF" + Header + "\r\n" + BuyRow + "\r\n" + SellRow);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(2, result.Records.Count);
        Assert.Equal("Target", result.Records[0].ExitReason);
        Assert.Equal(500.0, result.Records[1].RiskMoney);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Read_HeaderOnly_IsValidAndEmpty()
    {
        TradeLogReadResult result = TradeLogCsv.Read(Header + "\n");

        Assert.True(result.IsValid, result.Error);
        Assert.Empty(result.Records);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("symbol,zone_id\n")]
    [InlineData("SYMBOL,zone_id,open_time_utc,close_time_utc,side,volume_units,entry,stop,target,exit_price,exit_reason,result_money,result_r,commission,swap,risk_money\n")]
    public void Read_MissingOrWrongHeader_IsRejectedAtRowOne(string text)
    {
        TradeLogReadResult result = TradeLogCsv.Read(text);

        Assert.False(result.IsValid);
        Assert.StartsWith("Row 1: ", result.Error);
        Assert.Empty(result.Records);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Read_Null_IsRejected()
    {
        TradeLogReadResult result = TradeLogCsv.Read(null);

        Assert.False(result.IsValid);
        Assert.NotEqual(string.Empty, result.Error);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Read_EmptyLineBetweenRows_IsRejectedWithItsRowNumber()
    {
        TradeLogReadResult result = TradeLogCsv.Read(Header + "\n" + BuyRow + "\n\n" + SellRow + "\n");

        Assert.False(result.IsValid);
        Assert.StartsWith("Row 3: ", result.Error);
        Assert.Empty(result.Records);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData(BuyRow + ",extra")]
    [InlineData("EURUSD,D-20260105-0800")]
    [InlineData("EURUSD,D-20260105-0800,2026-01-05T08:15:00Z,2026-01-05T14:30:00Z,Buy,250,000,1.1,1.098,1.104,1.104,Target,995,1.99,-5,0,500")]
    public void Read_WrongColumnCount_IsRejectedWithItsRowNumber(string badRow)
    {
        TradeLogReadResult result = TradeLogCsv.Read(Log(SellRow, badRow, BuyRow));

        Assert.False(result.IsValid);
        Assert.StartsWith("Row 3: expected 16 columns", result.Error);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData(0, "eurusd", "symbol")]
    [InlineData(0, "EUR", "symbol")]
    [InlineData(0, "EURUS1", "symbol")]
    [InlineData(0, "EURUSD x", "symbol")]
    [InlineData(1, "", "zone_id")]
    [InlineData(1, "D 1", "zone_id")]
    [InlineData(1, "D\"1", "zone_id")]
    [InlineData(2, "2026-01-05 08:15:00", "open_time_utc")]
    [InlineData(2, "2026-01-05T08:15:00", "open_time_utc")]
    [InlineData(2, "2026-01-05T08:15:00.500Z", "open_time_utc")]
    [InlineData(2, "2026-02-30T08:15:00Z", "open_time_utc")]
    [InlineData(3, "2026-01-05T08:14:59Z", "close_time_utc")]
    [InlineData(3, "", "close_time_utc")]
    [InlineData(4, "buy", "side")]
    [InlineData(4, "Long", "side")]
    [InlineData(5, "0", "volume_units")]
    [InlineData(5, "-250000", "volume_units")]
    [InlineData(6, "abc", "entry")]
    [InlineData(6, "NaN", "entry")]
    [InlineData(6, "Infinity", "entry")]
    [InlineData(6, " 1.1", "entry")]
    [InlineData(6, "", "entry")]
    [InlineData(7, "1.1", "stop")]
    [InlineData(7, "1.101", "stop")]
    [InlineData(8, "1.1", "target")]
    [InlineData(8, "1.099", "target")]
    [InlineData(9, "0", "exit_price")]
    [InlineData(10, "", "exit_reason")]
    [InlineData(10, "Take profit", "exit_reason")]
    [InlineData(11, "x", "result_money")]
    [InlineData(12, "2.5", "result_r")]
    [InlineData(12, "-1.99", "result_r")]
    [InlineData(13, "5", "commission")]
    [InlineData(13, "0.01", "commission")]
    [InlineData(14, "1e400", "swap")]
    [InlineData(15, "0", "risk_money")]
    [InlineData(15, "-500", "risk_money")]
    public void Read_MalformedField_IsRejectedWithItsRowNumberAndColumn(int index, string value, string column)
    {
        TradeLogReadResult result = TradeLogCsv.Read(Log(SellRow, WithField(index, value), SellRow));

        Assert.False(result.IsValid);
        Assert.StartsWith("Row 3: " + column + " ", result.Error);
        Assert.Empty(result.Records);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData("1.9909")]
    [InlineData("1.9891")]
    [InlineData("1.99")]
    public void Read_ResultRWithinOneThousandthOfMoneyOverRisk_IsAccepted(string resultR)
    {
        // 995 / 500 = 1.99; a difference of 0.0009 R is rounding in the export.
        TradeLogReadResult result = TradeLogCsv.Read(Log(WithField(12, resultR)));

        Assert.True(result.IsValid, result.Error);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData("1.9911")]
    [InlineData("1.9889")]
    public void Read_ResultROffByMoreThanOneThousandth_IsRejected(string resultR)
    {
        TradeLogReadResult result = TradeLogCsv.Read(Log(WithField(12, resultR)));

        Assert.False(result.IsValid);
        Assert.StartsWith("Row 2: result_r ", result.Error);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData(0, "EURUSD.r")]
    [InlineData(1, "zone_1-A")]
    [InlineData(3, "2026-01-05T08:15:00Z")]
    [InlineData(13, "0")]
    [InlineData(14, "3.5")]
    [InlineData(6, "1.1E0")]
    public void Read_AllowedEdgeValues_AreAccepted(int index, string value)
    {
        TradeLogReadResult result = TradeLogCsv.Read(Log(WithField(index, value)));

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Constructor_InvalidValue_ThrowsAndNamesTheColumn()
    {
        DateTime open = SimData.Utc(2026, 1, 5, 8, 0);

        ArgumentException wrongSide = Assert.Throws<ArgumentException>(() => new TradeRecord("EURUSD", "Z", open, open, Side.Buy, 1000.0, 1.1, 1.2, 1.3, 1.2, "Stop", -10.0, -1.0, 0.0, 0.0, 10.0));
        ArgumentException localTime = Assert.Throws<ArgumentException>(() => new TradeRecord("EURUSD", "Z", new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Local), open, Side.Buy, 1000.0, 1.1, 1.0, 1.3, 1.2, "Stop", -10.0, -1.0, 0.0, 0.0, 10.0));
        ArgumentException nullSymbol = Assert.Throws<ArgumentException>(() => new TradeRecord(null!, "Z", open, open, Side.Buy, 1000.0, 1.1, 1.0, 1.3, 1.2, "Stop", -10.0, -1.0, 0.0, 0.0, 10.0));
        ArgumentException badSide = Assert.Throws<ArgumentException>(() => new TradeRecord("EURUSD", "Z", open, open, (Side)7, 1000.0, 1.1, 1.0, 1.3, 1.2, "Stop", -10.0, -1.0, 0.0, 0.0, 10.0));
        ArgumentException subSecond = Assert.Throws<ArgumentException>(() => new TradeRecord("EURUSD", "Z", open, open.AddMilliseconds(1.0), Side.Buy, 1000.0, 1.1, 1.0, 1.3, 1.2, "Stop", -10.0, -1.0, 0.0, 0.0, 10.0));

        Assert.Contains("stop", wrongSide.Message);
        Assert.Contains("open_time_utc", localTime.Message);
        Assert.Contains("symbol", nullSymbol.Message);
        Assert.Contains("side", badSide.Message);
        Assert.Contains("close_time_utc", subSecond.Message);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Constructor_SellWithMirroredLevels_IsValid()
    {
        DateTime open = SimData.Utc(2026, 1, 5, 8, 0);

        var record = new TradeRecord("GBPJPY", "S-1", open, open.AddHours(3.0), Side.Sell, 1000.0, 190.0, 190.5, 189.0, 189.0, "Target", 20.0, 2.0, -0.5, 0.0, 10.0);

        Assert.Equal("GBP", record.BaseCurrency);
        Assert.Equal("JPY", record.QuoteCurrency);
        Assert.Equal(2.0, record.ResultR);
    }
}
