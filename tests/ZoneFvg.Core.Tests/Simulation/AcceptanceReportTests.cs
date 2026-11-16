using Xunit;
using ZoneFvg.Core.Simulation;
using static ZoneFvg.Core.Tests.SimData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-005: every measure of spec 13.1, computed from trade logs, with pass or fail against each
/// threshold. Inclusive thresholds ("or higher", "or more", "or less", "at least") pass when the
/// value equals the threshold; "above" and "better" need strictly more. A measure without its
/// input (holdout, cost-stress or benchmark log, challenge simulation) is not evaluated, and the
/// overall verdict is then incomplete.
/// </summary>
public class AcceptanceReportTests
{
    private static TradeRecord At(string symbol, int index, double resultR, double riskMoney = 500.0)
    {
        // Trade number index closes on 5 January 2026 at 01:00 + index hours.
        DateTime open = Utc(2026, 1, 5, 0, 0).AddHours(index);
        return Trade(symbol, open, open.AddHours(1.0), resultR, riskMoney);
    }

    private static IReadOnlyList<TradeRecord> Development()
    {
        // R in close order: 2, -1, -1, -1, 2, -1, 2, 2 at risk 500.
        // Average R = (4 x 2 - 4 x 1) / 8 = 0.5. Gross profit 4 x 1,000 = 4,000; gross loss 4 x 500 = 2,000:
        // profit factor 2.0. Net by symbol: EURUSD +1,500, GBPUSD +500, AUDUSD -1,000,
        // USDJPY +1,000: 3 of 4 profitable; total 2,000, largest share 1,500 / 2,000 = 75%.
        // At 0.5% compounded: 1.01, then three losses: drawdown 1 - 0.995^3 = 1.4925125%.
        // Logged balances 101,000; 100,500; 100,000; 99,500; 100,500; 100,000; 101,000; 102,000:
        // drawdown 1,500, return-to-drawdown 2,000 / 1,500 = 1.333.
        return new[]
        {
            At("EURUSD", 1, 2.0),
            At("GBPUSD", 2, -1.0),
            At("AUDUSD", 3, -1.0),
            At("EURUSD", 4, -1.0),
            At("USDJPY", 5, 2.0),
            At("AUDUSD", 6, -1.0),
            At("GBPUSD", 7, 2.0),
            At("EURUSD", 8, 2.0),
        };
    }

    private static AcceptanceRow Row(AcceptanceResult result, string measure, string segment)
    {
        return Assert.Single(result.Rows, r => r.Measure == measure && r.Segment == segment);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Measures_SyntheticLog_GiveTheHandComputedValues()
    {
        IReadOnlyList<TradeRecord> log = Development();

        Assert.Equal(2.0, AcceptanceReport.ProfitFactor(log), 12);
        Assert.Equal(0.5, AcceptanceReport.AverageR(log), 12);
        Assert.Equal(1.4925125, AcceptanceReport.MaxDrawdownPercentAtRisk(log, 0.5), 12);
        Assert.Equal(2000.0 / 1500.0, AcceptanceReport.ReturnToDrawdown(log, 100000.0), 12);
        Assert.Equal(3, AcceptanceReport.ProfitableSymbols(log));
        Assert.Equal(4, AcceptanceReport.SymbolCount(log));
        Assert.Equal(0.75, AcceptanceReport.LargestSymbolShare(log), 12);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_AllInputs_StatesEveryRowOfSpec13Point1()
    {
        // Holdout R: 2, -1, 2, -1: average 0.5 >= 0.5 / 2; profit factor 2,000 / 1,000 = 2;
        // drawdown 0.5%; profitable EURUSD and AUDUSD only: 2 of 4.
        // Cost stress: wins +1.8R (900), losses -1.1R (-550): 3,600 / 2,200 = 1.636 > 1.10.
        // Benchmark: -1,000 then +1,500: net 500, drawdown 1,000, ratio 0.5 < 1.333.
        var holdout = new[] { At("EURUSD", 1, 2.0), At("GBPUSD", 2, -1.0), At("AUDUSD", 3, 2.0), At("USDJPY", 4, -1.0) };
        TradeRecord[] stress = Development().Select(t => At(t.Symbol, t.OpenTimeUtc.Hour, t.ResultR > 0.0 ? 1.8 : -1.1)).ToArray();
        var benchmark = new[] { At("EURUSD", 1, -2.0), At("EURUSD", 2, 3.0) };
        var inputs = new AcceptanceInputs(Development())
        {
            Holdout = holdout,
            DevelopmentCostStress = stress,
            DevelopmentBenchmark = benchmark,
            ChallengePassProbability = 0.35,
            ChallengeBreachProbability = 0.0,
        };

        AcceptanceResult result = AcceptanceReport.Evaluate(inputs);

        Assert.Equal(16, result.Rows.Count);
        Assert.Equal(AcceptanceVerdict.Fail, Row(result, "Trades, all symbols", "Development").Verdict);
        Assert.Equal("8", Row(result, "Trades, all symbols", "Development").Value);
        Assert.Equal(AcceptanceVerdict.Fail, Row(result, "Trades, all symbols", "Holdout").Verdict);
        Assert.Equal(AcceptanceVerdict.Pass, Row(result, "Profit factor after costs", "Development").Verdict);
        Assert.Equal("2.00", Row(result, "Profit factor after costs", "Development").Value);
        Assert.Equal(AcceptanceVerdict.Pass, Row(result, "Profit factor after costs", "Holdout").Verdict);
        Assert.Equal(AcceptanceVerdict.Pass, Row(result, "Average result per trade", "Development").Verdict);
        Assert.Equal("0.500R", Row(result, "Average result per trade", "Development").Value);
        Assert.Equal(AcceptanceVerdict.Pass, Row(result, "Average result per trade", "Holdout").Verdict);
        Assert.Equal("0.250R or higher (half the development figure)", Row(result, "Average result per trade", "Holdout").Threshold);
        Assert.Equal(AcceptanceVerdict.Pass, Row(result, "Maximum drawdown at 0.5% risk", "Development").Verdict);
        Assert.Equal("1.49%", Row(result, "Maximum drawdown at 0.5% risk", "Development").Value);
        Assert.Equal(AcceptanceVerdict.Pass, Row(result, "Maximum drawdown at 0.5% risk", "Holdout").Verdict);
        Assert.Equal(AcceptanceVerdict.Fail, Row(result, "Challenge simulation: chance of passing the Challenge phase", "Development").Verdict);
        Assert.Equal(AcceptanceVerdict.Pass, Row(result, "Challenge simulation: chance of breaching an FTMO floor", "Development").Verdict);
        Assert.Equal(AcceptanceVerdict.Pass, Row(result, "Profitable symbols", "Development").Verdict);
        Assert.Equal("3 of 4", Row(result, "Profitable symbols", "Development").Value);
        Assert.Equal(AcceptanceVerdict.Fail, Row(result, "Profitable symbols", "Holdout").Verdict);
        Assert.Equal(AcceptanceVerdict.Fail, Row(result, "Share of profit from one symbol", "Development").Verdict);
        Assert.Equal("75.0% (EURUSD)", Row(result, "Share of profit from one symbol", "Development").Value);
        Assert.Equal(AcceptanceVerdict.Pass, Row(result, "Profit factor under cost stress", "Development").Verdict);
        Assert.Equal(AcceptanceVerdict.Pass, Row(result, "Versus benchmark (return-to-drawdown)", "Development").Verdict);
        Assert.Equal("1.33 vs 0.50", Row(result, "Versus benchmark (return-to-drawdown)", "Development").Value);
        Assert.Equal(AcceptanceVerdict.NotEvaluated, Row(result, "Versus benchmark (return-to-drawdown)", "Holdout").Verdict);
        Assert.Equal(AcceptanceVerdict.Fail, result.Overall);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_DevelopmentOnly_MarksMissingInputsNotEvaluated()
    {
        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(Development()));

        Assert.Equal(AcceptanceVerdict.NotEvaluated, Row(result, "Trades, all symbols", "Holdout").Verdict);
        Assert.Equal(AcceptanceVerdict.NotEvaluated, Row(result, "Challenge simulation: chance of passing the Challenge phase", "Development").Verdict);
        Assert.Equal(AcceptanceVerdict.NotEvaluated, Row(result, "Profit factor under cost stress", "Development").Verdict);
        Assert.Equal(AcceptanceVerdict.NotEvaluated, Row(result, "Versus benchmark (return-to-drawdown)", "Development").Verdict);
        Assert.Equal(AcceptanceVerdict.Fail, result.Overall);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_EveryRowPassesExceptMissingInputs_IsIncomplete()
    {
        // 300 trades over four symbols in turn, in groups of four alternating +2R and -1R: each
        // symbol alternates too, so all four are profitable with 25% of the profit each; profit
        // factor 2; drawdown 1 - 0.995^4 = 1.99%. Every development row that can be evaluated
        // passes, but nothing for the holdout is given.
        TradeRecord[] trades = Enumerable.Range(0, 300).Select(i => At(new[] { "EURUSD", "GBPUSD", "AUDUSD", "USDJPY" }[i % 4], i, (i / 4) % 2 == 0 ? 2.0 : -1.0)).ToArray();
        var inputs = new AcceptanceInputs(trades)
        {
            DevelopmentCostStress = trades,
            DevelopmentBenchmark = new[] { At("EURUSD", 1, -1.0), At("EURUSD", 2, 1.0) },
            ChallengePassProbability = 0.7,
            ChallengeBreachProbability = 0.1,
        };

        AcceptanceResult result = AcceptanceReport.Evaluate(inputs);

        Assert.All(result.Rows.Where(r => r.Segment == "Development"), r => Assert.Equal(AcceptanceVerdict.Pass, r.Verdict));
        Assert.Equal(AcceptanceVerdict.NotEvaluated, result.Overall);
        Assert.Contains("Overall: INCOMPLETE", AcceptanceReport.ToMarkdown(result));
    }

    [Theory]
    [Trait("Req", "SIM-005")]
    [InlineData(1300.0, AcceptanceVerdict.Pass)]
    [InlineData(1299.99, AcceptanceVerdict.Fail)]
    [InlineData(1300.01, AcceptanceVerdict.Pass)]
    public void Evaluate_ProfitFactorAroundOnePointThree_PassesFromExactlyTheThreshold(double win, AcceptanceVerdict expected)
    {
        // One win of `win` and one loss of 1,000: profit factor win / 1,000.
        var log = new[] { At("EURUSD", 1, win / 500.0), At("EURUSD", 2, -2.0) };

        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(log));

        Assert.Equal(expected, Row(result, "Profit factor after costs", "Development").Verdict);
    }

    [Theory]
    [Trait("Req", "SIM-005")]
    [InlineData(0.3, AcceptanceVerdict.Pass)]
    [InlineData(0.29998, AcceptanceVerdict.Fail)]
    public void Evaluate_AverageRAroundZeroPointOneFive_PassesFromExactlyTheThreshold(double firstR, AcceptanceVerdict expected)
    {
        // Two trades, firstR and 0R: the average is firstR / 2 against 0.15.
        var log = new[] { At("EURUSD", 1, firstR), At("EURUSD", 2, 0.0) };

        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(log));

        Assert.Equal(expected, Row(result, "Average result per trade", "Development").Verdict);
    }

    [Theory]
    [Trait("Req", "SIM-005")]
    [InlineData(-12.0, AcceptanceVerdict.Pass)]
    [InlineData(-12.0002, AcceptanceVerdict.Fail)]
    [InlineData(-11.9998, AcceptanceVerdict.Pass)]
    public void Evaluate_DrawdownAroundSixPercent_PassesUpToExactlyTheThreshold(double lossR, AcceptanceVerdict expected)
    {
        // At 0.5% a loss of 12R takes the balance from 1 to 0.94: a drawdown of exactly 6%.
        var log = new[] { At("EURUSD", 1, lossR) };

        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(log));

        Assert.Equal(expected, Row(result, "Maximum drawdown at 0.5% risk", "Development").Verdict);
    }

    [Theory]
    [Trait("Req", "SIM-005")]
    [InlineData(0.7, 0.1, AcceptanceVerdict.Pass, AcceptanceVerdict.Pass)]
    [InlineData(0.6999, 0.1001, AcceptanceVerdict.Fail, AcceptanceVerdict.Fail)]
    public void Evaluate_ChallengeProbabilitiesAtTheThresholds_PassInclusive(double pass, double breach, AcceptanceVerdict passVerdict, AcceptanceVerdict breachVerdict)
    {
        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(Development()) { ChallengePassProbability = pass, ChallengeBreachProbability = breach });

        Assert.Equal(passVerdict, Row(result, "Challenge simulation: chance of passing the Challenge phase", "Development").Verdict);
        Assert.Equal(breachVerdict, Row(result, "Challenge simulation: chance of breaching an FTMO floor", "Development").Verdict);
    }

    [Theory]
    [Trait("Req", "SIM-005")]
    [InlineData(600.0, AcceptanceVerdict.Pass)]
    [InlineData(600.05, AcceptanceVerdict.Fail)]
    public void Evaluate_ShareOfProfitAroundSixtyPercent_PassesUpToExactlyTheThreshold(double eurUsdWin, AcceptanceVerdict expected)
    {
        // EURUSD nets eurUsdWin, GBPUSD 400: share eurUsdWin / (eurUsdWin + 400).
        var log = new[] { At("EURUSD", 1, eurUsdWin / 500.0), At("GBPUSD", 2, 0.8) };

        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(log));

        Assert.Equal(expected, Row(result, "Share of profit from one symbol", "Development").Verdict);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_NoNetProfit_FailsTheShareOfProfit()
    {
        var log = new[] { At("EURUSD", 1, 1.0), At("GBPUSD", 2, -1.0) };

        AcceptanceRow row = Row(AcceptanceReport.Evaluate(new AcceptanceInputs(log)), "Share of profit from one symbol", "Development");

        Assert.Equal(AcceptanceVerdict.Fail, row.Verdict);
        Assert.Equal("no net profit", row.Value);
    }

    [Theory]
    [Trait("Req", "SIM-005")]
    [InlineData(1.1, AcceptanceVerdict.Fail)]
    [InlineData(1.1002, AcceptanceVerdict.Pass)]
    public void Evaluate_CostStressProfitFactor_MustBeStrictlyAboveOnePointOne(double winR, AcceptanceVerdict expected)
    {
        // One win of winR and one loss of 1R: profit factor winR.
        var stress = new[] { At("EURUSD", 1, winR), At("EURUSD", 2, -1.0) };

        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(Development()) { DevelopmentCostStress = stress });

        Assert.Equal(expected, Row(result, "Profit factor under cost stress", "Development").Verdict);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_BenchmarkWithTheSameRatio_IsNotBetter()
    {
        // The development ratio is 2,000 / 1,500; a benchmark of -1,500 then +3,500 has 2,000 / 1,500 too.
        var benchmark = new[] { At("EURUSD", 1, -3.0), At("EURUSD", 2, 7.0) };

        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(Development()) { DevelopmentBenchmark = benchmark });

        Assert.Equal(AcceptanceVerdict.Fail, Row(result, "Versus benchmark (return-to-drawdown)", "Development").Verdict);
    }

    [Theory]
    [Trait("Req", "SIM-005")]
    [InlineData(299, AcceptanceVerdict.Fail)]
    [InlineData(300, AcceptanceVerdict.Pass)]
    public void Evaluate_TradeCountAroundThreeHundred_PassesFromExactlyTheThreshold(int count, AcceptanceVerdict expected)
    {
        TradeRecord[] trades = Enumerable.Range(0, count).Select(i => At("EURUSD", i, 1.0)).ToArray();

        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(trades));

        Assert.Equal(expected, Row(result, "Trades, all symbols", "Development").Verdict);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Measures_EdgeCases_AreDefined()
    {
        var winsOnly = new[] { At("EURUSD", 1, 1.0) };
        var flat = new[] { At("EURUSD", 1, 0.0) };

        Assert.Equal(double.PositiveInfinity, AcceptanceReport.ProfitFactor(winsOnly));
        Assert.Equal(0.0, AcceptanceReport.ProfitFactor(flat));
        Assert.True(double.IsNaN(AcceptanceReport.ProfitFactor(Array.Empty<TradeRecord>())));
        Assert.True(double.IsNaN(AcceptanceReport.AverageR(Array.Empty<TradeRecord>())));
        Assert.Equal(double.PositiveInfinity, AcceptanceReport.ReturnToDrawdown(winsOnly, 100000.0));
        Assert.Equal(0.0, AcceptanceReport.ReturnToDrawdown(flat, 100000.0));
        Assert.True(double.IsNaN(AcceptanceReport.LargestSymbolShare(flat)));
        Assert.Equal("infinite", Row(AcceptanceReport.Evaluate(new AcceptanceInputs(winsOnly)), "Profit factor after costs", "Development").Value);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_EmptyDevelopmentLog_FailsWithoutThrowing()
    {
        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(Array.Empty<TradeRecord>()));

        Assert.Equal(AcceptanceVerdict.Fail, Row(result, "Profit factor after costs", "Development").Verdict);
        Assert.Equal(AcceptanceVerdict.Fail, Row(result, "Average result per trade", "Development").Verdict);
        Assert.Equal(AcceptanceVerdict.Fail, result.Overall);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void ToMarkdown_StatesAssumptionsTableAndOverallVerdict()
    {
        AcceptanceResult result = AcceptanceReport.Evaluate(new AcceptanceInputs(Development()));

        string text = AcceptanceReport.ToMarkdown(result);

        Assert.Contains("past trade distributions may not repeat", text);
        Assert.Contains("| Measure | Segment | Value | Threshold | Result |", text);
        Assert.Contains("| Profit factor after costs | Development | 2.00 | 1.30 or higher | PASS |", text);
        Assert.Contains("| Trades, all symbols | Development | 8 | 300 or more | FAIL |", text);
        Assert.Contains("| Profit factor under cost stress | Development | no cost-stress log | above 1.10 | NOT EVALUATED |", text);
        Assert.Contains("Overall: FAIL", text);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Evaluate_InvalidInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AcceptanceReport.Evaluate(null!));
        Assert.Throws<ArgumentNullException>(() => new AcceptanceInputs(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcceptanceReport.Evaluate(new AcceptanceInputs(Development()) { ChallengePassProbability = 1.5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcceptanceReport.Evaluate(new AcceptanceInputs(Development()) { ChallengeBreachProbability = -0.1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcceptanceReport.Evaluate(new AcceptanceInputs(Development()) { InitialCapital = 0.0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => AcceptanceReport.Evaluate(new AcceptanceInputs(Development()) { DrawdownRiskPercent = 0.0 }));
    }
}
