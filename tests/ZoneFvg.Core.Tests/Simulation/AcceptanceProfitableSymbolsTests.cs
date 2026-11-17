using Xunit;
using ZoneFvg.Core.Simulation;
using static ZoneFvg.Core.Tests.SimData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-005 as changed on 2026-11-17 (owner decision D): the profitable-symbols threshold is
/// max(3, N - 1) of N, N being the number of distinct symbols in the development log, for both
/// segments: 3 of 4 for the four forex pairs, 4 of 5 with gold. The threshold text reads
/// "&lt;threshold&gt; of &lt;N&gt;"; the value reads "&lt;profitable&gt; of &lt;symbols of that segment&gt;".
/// Owner answer of 2026-11-17 on pull request 50: N is every symbol the run covers, that is every
/// symbol whose log is given; a covered symbol without trades is not profitable and does not lower N.
/// No symbol at all never reads "3 of 0".
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

    // ------------------------------------------------------------------ owner answer of 2026-11-17: N is every covered symbol

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_GoldCoveredWithoutTrades_CountsInNAndIsNotProfitable()
    {
        // Four forex symbols with trades, gold covered but without a trade: N = 5, so 4 of 5 is needed.
        AcceptanceRow fourOfFive = Row(AcceptanceReport.Evaluate(new AcceptanceInputs(Log(4, 4)) { DevelopmentSymbolCount = 5 }), "Development");
        AcceptanceRow threeOfFive = Row(AcceptanceReport.Evaluate(new AcceptanceInputs(Log(4, 3)) { DevelopmentSymbolCount = 5 }), "Development");

        Assert.Equal(AcceptanceVerdict.Pass, fourOfFive.Verdict);
        Assert.Equal("4 of 5", fourOfFive.Value);
        Assert.Equal("4 of 5", fourOfFive.Threshold);
        Assert.Equal(AcceptanceVerdict.Fail, threeOfFive.Verdict);
        Assert.Equal("3 of 5", threeOfFive.Value);
        Assert.Equal("4 of 5", threeOfFive.Threshold);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_HoldoutSymbolCount_IsTheHoldoutValueDenominator()
    {
        var inputs = new AcceptanceInputs(Log(5, 4)) { Holdout = Log(3, 3), HoldoutSymbolCount = 5 };

        AcceptanceRow row = Row(AcceptanceReport.Evaluate(inputs), "Holdout");

        Assert.Equal(AcceptanceVerdict.Fail, row.Verdict);
        Assert.Equal("3 of 5", row.Value);
        Assert.Equal("4 of 5", row.Threshold);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_NoSymbolAtAll_FailsAndNeverReadsThreeOfZero()
    {
        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(Array.Empty<TradeRecord>()));

        AcceptanceRow development = Row(result, "Development");
        Assert.Equal(AcceptanceVerdict.Fail, development.Verdict);
        Assert.Equal("no symbol", development.Value);
        Assert.Equal("max(3, N - 1) of N symbols", development.Threshold);
        Assert.Equal("max(3, N - 1) of N symbols", Row(result, "Holdout").Threshold);
        string text = AcceptanceReport.ToMarkdown(result);
        Assert.DoesNotContain("3 of 0", text, StringComparison.Ordinal);
        Assert.Contains("| Profitable symbols | Development | no symbol | max(3, N - 1) of N symbols | FAIL |", text, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIM-005")]
    [InlineData(0, null)]
    [InlineData(4, null)]
    [InlineData(null, 0)]
    [InlineData(null, 2)]
    public void Validate_SymbolCountBelowOneOrBelowTheTradedSymbols_Throws(int? development, int? holdout)
    {
        // The development log trades five symbols, the holdout log three.
        var inputs = new AcceptanceInputs(Log(5, 5)) { Holdout = Log(3, 3), DevelopmentSymbolCount = development, HoldoutSymbolCount = holdout };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => AcceptanceReport.Evaluate(inputs));
        Assert.Equal(development.HasValue ? "DevelopmentSymbolCount" : "HoldoutSymbolCount", exception.ParamName);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Validate_SymbolCountsEqualToTheTradedSymbols_AreAccepted()
    {
        var inputs = new AcceptanceInputs(Log(5, 5)) { Holdout = Log(3, 3), DevelopmentSymbolCount = 5, HoldoutSymbolCount = 3 };

        Assert.Equal("5 of 5", Row(AcceptanceReport.Evaluate(inputs), "Development").Value);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void SymbolsCovered_NamedSymbolsOncePlusOnePerLogWithoutTrades()
    {
        IReadOnlyList<TradeRecord> eurUsd = Log(1, 1);
        IReadOnlyList<TradeRecord> twoSymbols = Log(2, 1);
        IReadOnlyList<TradeRecord> empty = Array.Empty<TradeRecord>();

        Assert.Equal(1, AcceptanceReport.SymbolsCovered(new[] { eurUsd }));
        Assert.Equal(2, AcceptanceReport.SymbolsCovered(new[] { eurUsd, twoSymbols }));
        Assert.Equal(4, AcceptanceReport.SymbolsCovered(new[] { eurUsd, twoSymbols, empty, empty }));
        Assert.Equal(1, AcceptanceReport.SymbolsCovered(new[] { empty }));
        Assert.Equal(0, AcceptanceReport.SymbolsCovered(Array.Empty<IReadOnlyList<TradeRecord>>()));
        Assert.Throws<ArgumentNullException>(() => AcceptanceReport.SymbolsCovered(null!));
        Assert.Throws<ArgumentNullException>(() => AcceptanceReport.SymbolsCovered(new IReadOnlyList<TradeRecord>[] { eurUsd, null! }));
    }
}
