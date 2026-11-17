using Xunit;
using ZoneFvg.Core.Simulation;
using static ZoneFvg.Core.Tests.SimData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-005 as changed on 2026-11-17 (owner decision D): the profitable-symbols threshold is
/// max(3, N - 1) of N, N being the number of distinct symbols in the development log, for both
/// segments: 3 of 4 for the four forex pairs, 4 of 5 with gold. The threshold text reads
/// "&lt;threshold&gt; of &lt;N&gt;"; the value reads "&lt;profitable&gt; of &lt;symbols of that log&gt;".
/// </summary>
public class AcceptanceProfitableSymbolsTests
{
    private const string Measure = "Profitable symbols";

    private static readonly string[] Five = { "EURUSD", "GBPUSD", "USDJPY", "AUDUSD", "XAUUSD" };

    /// <summary>One trade per symbol, the first <paramref name="profitable"/> winning 2R and the others losing 1R.</summary>
    private static IReadOnlyList<TradeRecord> Log(int symbols, int profitable)
    {
        var trades = new List<TradeRecord>();
        for (int i = 0; i < symbols; i++)
        {
            DateTime open = Utc(2026, 1, 5, 0, 0).AddHours(i);
            trades.Add(Trade(Five[i], open, open.AddHours(1.0), i < profitable ? 2.0 : -1.0, 500.0));
        }

        return trades;
    }

    private static AcceptanceRow Row(AcceptanceResult result, string segment)
    {
        return Assert.Single(result.Rows, r => r.Measure == Measure && r.Segment == segment);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_FiveSymbolsFourProfitable_PassesFourOfFive()
    {
        AcceptanceRow row = Row(AcceptanceReport.Evaluate(new AcceptanceInputs(Log(5, 4))), "Development");

        Assert.Equal(AcceptanceVerdict.Pass, row.Verdict);
        Assert.Equal("4 of 5", row.Value);
        Assert.Equal("4 of 5", row.Threshold);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_FiveSymbolsThreeProfitable_FailsAgainstFourOfFive()
    {
        AcceptanceRow row = Row(AcceptanceReport.Evaluate(new AcceptanceInputs(Log(5, 3))), "Development");

        Assert.Equal(AcceptanceVerdict.Fail, row.Verdict);
        Assert.Equal("3 of 5", row.Value);
        Assert.Equal("4 of 5", row.Threshold);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_FourSymbolsThreeProfitable_PassesThreeOfFourAsBefore()
    {
        AcceptanceRow row = Row(AcceptanceReport.Evaluate(new AcceptanceInputs(Log(4, 3))), "Development");

        Assert.Equal(AcceptanceVerdict.Pass, row.Verdict);
        Assert.Equal("3 of 4", row.Value);
        Assert.Equal("3 of 4", row.Threshold);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_FourSymbolsTwoProfitable_FailsAgainstThreeOfFour()
    {
        AcceptanceRow row = Row(AcceptanceReport.Evaluate(new AcceptanceInputs(Log(4, 2))), "Development");

        Assert.Equal(AcceptanceVerdict.Fail, row.Verdict);
        Assert.Equal("3 of 4", row.Threshold);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_ThreeSymbolsThreeProfitable_ThresholdStaysThree()
    {
        // max(3, 3 - 1) = 3.
        AcceptanceRow row = Row(AcceptanceReport.Evaluate(new AcceptanceInputs(Log(3, 3))), "Development");

        Assert.Equal(AcceptanceVerdict.Pass, row.Verdict);
        Assert.Equal("3 of 3", row.Value);
        Assert.Equal("3 of 3", row.Threshold);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_DevelopmentFiveSymbolsHoldoutFourAllProfitable_HoldoutThresholdFourPasses()
    {
        var inputs = new AcceptanceInputs(Log(5, 4)) { Holdout = Log(4, 4) };

        AcceptanceRow row = Row(AcceptanceReport.Evaluate(inputs), "Holdout");

        Assert.Equal(AcceptanceVerdict.Pass, row.Verdict);
        Assert.Equal("4 of 4", row.Value);
        Assert.Equal("4 of 5", row.Threshold);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_DevelopmentFiveSymbolsHoldoutThreeProfitable_HoldoutFails()
    {
        var inputs = new AcceptanceInputs(Log(5, 4)) { Holdout = Log(5, 3) };

        AcceptanceRow row = Row(AcceptanceReport.Evaluate(inputs), "Holdout");

        Assert.Equal(AcceptanceVerdict.Fail, row.Verdict);
        Assert.Equal("4 of 5", row.Threshold);
    }

    [Theory]
    [Trait("Req", "SIM-005")]
    [InlineData(4, "3 of 4")]
    [InlineData(5, "4 of 5")]
    public void Evaluate_NoHoldoutLog_ThresholdTextFollowsTheDevelopmentSymbols(int symbols, string threshold)
    {
        AcceptanceRow row = Row(AcceptanceReport.Evaluate(new AcceptanceInputs(Log(symbols, symbols))), "Holdout");

        Assert.Equal(AcceptanceVerdict.NotEvaluated, row.Verdict);
        Assert.Equal(threshold, row.Threshold);
    }

    [Theory]
    [Trait("Req", "SIM-005")]
    [InlineData(1, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    [InlineData(6, 5)]
    public void ProfitableSymbolsThreshold_IsMaxOfThreeAndNMinusOne(int symbols, int expected)
    {
        Assert.Equal(expected, AcceptanceReport.ProfitableSymbolsThreshold(symbols));
    }
}
