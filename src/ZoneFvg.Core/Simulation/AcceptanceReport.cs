using System;
using System.Collections.Generic;
using System.Text;
using ZoneFvg.Core.Domain;

namespace ZoneFvg.Core.Simulation;

/// <summary>
/// Acceptance report (SIM-005): computes every measure of spec 13.1 from trade logs and states
/// pass or fail against each threshold.
/// </summary>
/// <remarks>
/// <para>Each log is used as given; the console command first combines the per-symbol logs with
/// <see cref="PortfolioCombiner"/>. Measures:</para>
/// <list type="bullet">
/// <item>Profit factor: sum of winning result_money over the size of the sum of losing
/// result_money (net of costs); infinite with no loss and some profit; 0 with neither.</item>
/// <item>Average result: mean result_r.</item>
/// <item>Maximum drawdown at a risk: result_r compounded at that percentage of the balance, in
/// close order, as <see cref="MonteCarlo.MaxDrawdownPercent"/>.</item>
/// <item>Profitable symbols: symbols whose summed result_money is above zero. Share of profit:
/// the largest symbol total divided by the total of all symbols, defined only when that total is
/// above zero.</item>
/// <item>Return-to-drawdown: total result_money divided by the largest fall of the logged
/// balance from its running peak (from the initial capital, in close order).</item>
/// </list>
/// <para>Comparisons use a tolerance of 1e-9. "Or higher", "or more", "or less" and "at least"
/// include the threshold; "above" and "better" do not.</para>
/// </remarks>
public static class AcceptanceReport
{
    private const string Development = "Development";
    private const string Holdout = "Holdout";

    /// <summary>Tolerance of every threshold comparison (the default of PriceMath).</summary>
    private const double Tolerance = PriceMath.DefaultTolerance;

    /// <summary>Evaluates every row of spec 13.1 (SIM-005).</summary>
    /// <param name="inputs">The logs and the challenge simulation results.</param>
    /// <returns>The rows and the overall verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A value of <paramref name="inputs"/> is out of range.</exception>
    public static AcceptanceResult Evaluate(AcceptanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        inputs.Validate();
        var rows = new List<AcceptanceRow>();
        IReadOnlyList<TradeRecord> dev = inputs.Development;
        IReadOnlyList<TradeRecord>? hold = inputs.Holdout;
        double devAverage = AverageR(dev);

        rows.Add(AtLeast("Trades, all symbols", Development, dev.Count, 300.0, SimText.Int(dev.Count), "300 or more"));
        rows.Add(hold == null ? Missing("Trades, all symbols", Holdout, "60 or more", "no holdout log") : AtLeast("Trades, all symbols", Holdout, hold.Count, 60.0, SimText.Int(hold.Count), "60 or more"));
        rows.Add(ProfitFactorRow(dev, Development, 1.30));
        rows.Add(hold == null ? Missing("Profit factor after costs", Holdout, "1.15 or higher", "no holdout log") : ProfitFactorRow(hold, Holdout, 1.15));
        rows.Add(AtLeast("Average result per trade", Development, devAverage, 0.15, RText(devAverage), "+0.15R or higher"));
        rows.Add(hold == null ? Missing("Average result per trade", Holdout, "at least half the development figure", "no holdout log") : HoldoutAverageRow(hold, devAverage));
        rows.Add(DrawdownRow(dev, Development, inputs.DrawdownRiskPercent));
        rows.Add(hold == null ? Missing(DrawdownMeasure(inputs.DrawdownRiskPercent), Holdout, "6% or less", "no holdout log") : DrawdownRow(hold, Holdout, inputs.DrawdownRiskPercent));
        rows.Add(ProbabilityRow("Challenge simulation: chance of passing the Challenge phase", inputs.ChallengePassProbability, 0.70, true, "70% or higher"));
        rows.Add(ProbabilityRow("Challenge simulation: chance of breaching an FTMO floor", inputs.ChallengeBreachProbability, 0.10, false, "10% or lower"));
        rows.Add(ProfitableSymbolsRow(dev, Development));
        rows.Add(hold == null ? Missing("Profitable symbols", Holdout, "3 of 4", "no holdout log") : ProfitableSymbolsRow(hold, Holdout));
        rows.Add(ShareRow(dev));
        rows.Add(StressRow(inputs.DevelopmentCostStress));
        rows.Add(BenchmarkRow(dev, inputs.DevelopmentBenchmark, Development, inputs.InitialCapital));
        rows.Add(hold == null ? Missing(BenchmarkMeasure, Holdout, "better return-to-drawdown ratio", "no holdout log") : BenchmarkRow(hold, inputs.HoldoutBenchmark, Holdout, inputs.InitialCapital));
        return new AcceptanceResult(rows);
    }

    /// <summary>The result as Markdown, with the assumptions first and the overall verdict last (SIM-005).</summary>
    /// <param name="result">The evaluated rows.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    public static string ToMarkdown(AcceptanceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var text = new StringBuilder();
        text.Append("# Acceptance report, spec 13.1 (SIM-005)\n\n");
        text.Append("Assumptions:\n\n");
        text.Append("- The logs are a sample of the past: past trade distributions may not repeat.\n");
        text.Append("- Every log was combined across symbols with the account caps replayed (SIM-002); results are net of commission and swap.\n");
        text.Append("- Drawdown at a risk compounds result_r on the balance and is measured on closed results; open-trade swings are not in the logs.\n");
        text.Append("- A measure whose input was not given is NOT EVALUATED, and the overall verdict is then INCOMPLETE unless a row fails.\n\n");
        text.Append("| Measure | Segment | Value | Threshold | Result |\n");
        text.Append("|---|---|---|---|---|\n");
        foreach (AcceptanceRow row in result.Rows)
        {
            text.Append("| ").Append(row.Measure).Append(" | ").Append(row.Segment).Append(" | ").Append(row.Value)
                .Append(" | ").Append(row.Threshold).Append(" | ").Append(VerdictText(row.Verdict)).Append(" |\n");
        }

        string overall = result.Overall == AcceptanceVerdict.NotEvaluated ? "INCOMPLETE" : VerdictText(result.Overall);
        text.Append("\nOverall: ").Append(overall).Append('\n');
        return text.ToString();
    }

    /// <summary>Profit factor after costs (SIM-005): NaN without trades, infinite with profit and no loss, 0 with neither.</summary>
    /// <param name="trades">The trades.</param>
    /// <returns>Gross profit over gross loss of result_money.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trades"/> is null.</exception>
    public static double ProfitFactor(IReadOnlyList<TradeRecord> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        if (trades.Count == 0)
        {
            return double.NaN;
        }

        double profit = 0.0;
        double loss = 0.0;
        foreach (TradeRecord trade in trades)
        {
            profit += Math.Max(trade.ResultMoney, 0.0);
            loss -= Math.Min(trade.ResultMoney, 0.0);
        }

        if (loss > 0.0)
        {
            return profit / loss;
        }

        return profit > 0.0 ? double.PositiveInfinity : 0.0;
    }

    /// <summary>Mean result_r (SIM-005); NaN without trades.</summary>
    /// <param name="trades">The trades.</param>
    /// <returns>The average result in R.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trades"/> is null.</exception>
    public static double AverageR(IReadOnlyList<TradeRecord> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        if (trades.Count == 0)
        {
            return double.NaN;
        }

        double sum = 0.0;
        foreach (TradeRecord trade in trades)
        {
            sum += trade.ResultR;
        }

        return sum / trades.Count;
    }

    /// <summary>
    /// Maximum drawdown in percent when result_r is compounded at <paramref name="riskPercent"/>
    /// of the balance in close order (SIM-005, as <see cref="MonteCarlo.MaxDrawdownPercent"/>).
    /// </summary>
    /// <param name="trades">The trades, in any order.</param>
    /// <param name="riskPercent">Risk per trade in percent of the balance.</param>
    /// <returns>The maximum drawdown in percent of the running peak.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trades"/> is null.</exception>
    public static double MaxDrawdownPercentAtRisk(IReadOnlyList<TradeRecord> trades, double riskPercent)
    {
        List<TradeRecord> ordered = ByClose(trades);
        var results = new double[ordered.Count];
        for (int i = 0; i < results.Length; i++)
        {
            results[i] = ordered[i].ResultR;
        }

        return MonteCarlo.MaxDrawdownPercent(results, riskPercent);
    }

    /// <summary>
    /// Total result_money divided by the largest fall of the logged balance from its running peak
    /// (SIM-005). Infinite with no drawdown and a profit; 0 with no drawdown and no profit.
    /// </summary>
    /// <param name="trades">The trades, in any order.</param>
    /// <param name="initialCapital">Starting balance.</param>
    /// <returns>The return-to-drawdown ratio.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trades"/> is null.</exception>
    public static double ReturnToDrawdown(IReadOnlyList<TradeRecord> trades, double initialCapital)
    {
        double balance = initialCapital;
        double peak = initialCapital;
        double drawdown = 0.0;
        foreach (TradeRecord trade in ByClose(trades))
        {
            balance += trade.ResultMoney;
            peak = Math.Max(peak, balance);
            drawdown = Math.Max(drawdown, peak - balance);
        }

        double net = balance - initialCapital;
        if (drawdown > 0.0)
        {
            return net / drawdown;
        }

        return net > 0.0 ? double.PositiveInfinity : 0.0;
    }

    /// <summary>Number of symbols whose summed result_money is above zero (SIM-005).</summary>
    /// <param name="trades">The trades.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trades"/> is null.</exception>
    public static int ProfitableSymbols(IReadOnlyList<TradeRecord> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        int count = 0;
        foreach (KeyValuePair<string, PortfolioResult.SymbolTotal> pair in PortfolioResult.BySymbol(trades))
        {
            count += pair.Value.Net > 0.0 ? 1 : 0;
        }

        return count;
    }

    /// <summary>Number of distinct symbols (SIM-005).</summary>
    /// <param name="trades">The trades.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trades"/> is null.</exception>
    public static int SymbolCount(IReadOnlyList<TradeRecord> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        return PortfolioResult.BySymbol(trades).Count;
    }

    /// <summary>
    /// The largest symbol total of result_money divided by the total of all symbols (SIM-005);
    /// NaN when that total is not above zero.
    /// </summary>
    /// <param name="trades">The trades.</param>
    /// <returns>The share as a fraction.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trades"/> is null.</exception>
    public static double LargestSymbolShare(IReadOnlyList<TradeRecord> trades)
    {
        return LargestShare(trades, out _);
    }

    private static double LargestShare(IReadOnlyList<TradeRecord> trades, out string symbol)
    {
        ArgumentNullException.ThrowIfNull(trades);
        symbol = string.Empty;
        double total = 0.0;
        double largest = double.NegativeInfinity;
        foreach (KeyValuePair<string, PortfolioResult.SymbolTotal> pair in PortfolioResult.BySymbol(trades))
        {
            total += pair.Value.Net;
            if (pair.Value.Net > largest)
            {
                largest = pair.Value.Net;
                symbol = pair.Key;
            }
        }

        return total > 0.0 ? largest / total : double.NaN;
    }

    private static List<TradeRecord> ByClose(IReadOnlyList<TradeRecord> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var indexed = new List<KeyValuePair<int, TradeRecord>>(trades.Count);
        for (int i = 0; i < trades.Count; i++)
        {
            indexed.Add(new KeyValuePair<int, TradeRecord>(i, trades[i]));
        }

        // Close time, then open time, then symbol, then input position.
        indexed.Sort((a, b) =>
        {
            int result = a.Value.CloseTimeUtc.CompareTo(b.Value.CloseTimeUtc);
            result = result != 0 ? result : a.Value.OpenTimeUtc.CompareTo(b.Value.OpenTimeUtc);
            result = result != 0 ? result : string.CompareOrdinal(a.Value.Symbol, b.Value.Symbol);
            return result != 0 ? result : a.Key.CompareTo(b.Key);
        });

        var ordered = new List<TradeRecord>(indexed.Count);
        foreach (KeyValuePair<int, TradeRecord> pair in indexed)
        {
            ordered.Add(pair.Value);
        }

        return ordered;
    }

    private const string BenchmarkMeasure = "Versus benchmark (return-to-drawdown)";

    private static string DrawdownMeasure(double riskPercent)
    {
        return "Maximum drawdown at " + SimText.Short(riskPercent) + "% risk";
    }

    private static AcceptanceRow ProfitFactorRow(IReadOnlyList<TradeRecord> trades, string segment, double threshold)
    {
        double pf = ProfitFactor(trades);
        return AtLeast("Profit factor after costs", segment, pf, threshold, PfText(pf), SimText.F2(threshold) + " or higher");
    }

    private static AcceptanceRow HoldoutAverageRow(IReadOnlyList<TradeRecord> holdout, double devAverage)
    {
        double average = AverageR(holdout);
        double threshold = devAverage / 2.0;
        return AtLeast("Average result per trade", Holdout, average, threshold, RText(average), RText(threshold) + " or higher (half the development figure)");
    }

    private static AcceptanceRow DrawdownRow(IReadOnlyList<TradeRecord> trades, string segment, double riskPercent)
    {
        double drawdown = MaxDrawdownPercentAtRisk(trades, riskPercent);
        return AtMost(DrawdownMeasure(riskPercent), segment, drawdown, 6.0, SimText.F2(drawdown) + "%", "6% or less");
    }

    private static AcceptanceRow ProbabilityRow(string measure, double? probability, double threshold, bool atLeast, string thresholdText)
    {
        if (probability == null)
        {
            return Missing(measure, Development, thresholdText, "no challenge simulation");
        }

        string value = SimText.Percent(probability.Value);
        return atLeast ? AtLeast(measure, Development, probability.Value, threshold, value, thresholdText) : AtMost(measure, Development, probability.Value, threshold, value, thresholdText);
    }

    private static AcceptanceRow ProfitableSymbolsRow(IReadOnlyList<TradeRecord> trades, string segment)
    {
        int profitable = ProfitableSymbols(trades);
        string value = SimText.Int(profitable) + " of " + SimText.Int(SymbolCount(trades));
        return AtLeast("Profitable symbols", segment, profitable, 3.0, value, "3 of 4");
    }

    private static AcceptanceRow ShareRow(IReadOnlyList<TradeRecord> trades)
    {
        const string measure = "Share of profit from one symbol";
        double share = LargestShare(trades, out string symbol);
        if (double.IsNaN(share))
        {
            return new AcceptanceRow(measure, Development, "no net profit", "60% or less", AcceptanceVerdict.Fail);
        }

        return AtMost(measure, Development, share, 0.60, SimText.Percent(share) + " (" + symbol + ")", "60% or less");
    }

    private static AcceptanceRow StressRow(IReadOnlyList<TradeRecord>? stress)
    {
        const string measure = "Profit factor under cost stress";
        if (stress == null)
        {
            return Missing(measure, Development, "above 1.10", "no cost-stress log");
        }

        double pf = ProfitFactor(stress);
        return Above(measure, Development, pf, 1.10, PfText(pf), "above 1.10");
    }

    private static AcceptanceRow BenchmarkRow(IReadOnlyList<TradeRecord> trades, IReadOnlyList<TradeRecord>? benchmark, string segment, double initialCapital)
    {
        if (benchmark == null)
        {
            return Missing(BenchmarkMeasure, segment, "better return-to-drawdown ratio", "no benchmark log");
        }

        double strategy = ReturnToDrawdown(trades, initialCapital);
        double other = ReturnToDrawdown(benchmark, initialCapital);
        return Above(BenchmarkMeasure, segment, strategy, other, PfText(strategy) + " vs " + PfText(other), "better return-to-drawdown ratio");
    }

    private static AcceptanceRow AtLeast(string measure, string segment, double value, double threshold, string valueText, string thresholdText)
    {
        // Written so that NaN (no trades) fails and an infinite value compares as larger than any threshold.
        bool pass = value >= threshold - Tolerance;
        return new AcceptanceRow(measure, segment, valueText, thresholdText, pass ? AcceptanceVerdict.Pass : AcceptanceVerdict.Fail);
    }

    private static AcceptanceRow AtMost(string measure, string segment, double value, double threshold, string valueText, string thresholdText)
    {
        bool pass = value <= threshold + Tolerance;
        return new AcceptanceRow(measure, segment, valueText, thresholdText, pass ? AcceptanceVerdict.Pass : AcceptanceVerdict.Fail);
    }

    private static AcceptanceRow Above(string measure, string segment, double value, double threshold, string valueText, string thresholdText)
    {
        // An infinite value is above any finite threshold but not above an infinite one.
        bool pass = value > threshold + Tolerance;
        return new AcceptanceRow(measure, segment, valueText, thresholdText, pass ? AcceptanceVerdict.Pass : AcceptanceVerdict.Fail);
    }

    private static AcceptanceRow Missing(string measure, string segment, string thresholdText, string reason)
    {
        return new AcceptanceRow(measure, segment, reason, thresholdText, AcceptanceVerdict.NotEvaluated);
    }

    private static string PfText(double value)
    {
        if (double.IsNaN(value))
        {
            return "no trades";
        }

        return double.IsPositiveInfinity(value) ? "infinite" : SimText.F2(value);
    }

    private static string RText(double value)
    {
        return double.IsNaN(value) ? "no trades" : SimText.F3(value) + "R";
    }

    private static string VerdictText(AcceptanceVerdict verdict)
    {
        switch (verdict)
        {
            case AcceptanceVerdict.Pass:
                return "PASS";
            case AcceptanceVerdict.Fail:
                return "FAIL";
            default:
                return "NOT EVALUATED";
        }
    }
}

/// <summary>Verdict of one row of the acceptance report, and of the report as a whole (SIM-005).</summary>
public enum AcceptanceVerdict
{
    /// <summary>The threshold is met.</summary>
    Pass,

    /// <summary>The threshold is not met, or the measure cannot be computed from the given data.</summary>
    Fail,

    /// <summary>The input for the measure was not given. For the whole report: no row fails but some were not evaluated.</summary>
    NotEvaluated,
}

/// <summary>One row of spec 13.1 for one segment (SIM-005).</summary>
/// <param name="Measure">The measure, as named in spec 13.1.</param>
/// <param name="Segment">Development or Holdout.</param>
/// <param name="Value">The computed value, or why it is missing.</param>
/// <param name="Threshold">The threshold of spec 13.1.</param>
/// <param name="Verdict">Pass, fail or not evaluated.</param>
public sealed record AcceptanceRow(string Measure, string Segment, string Value, string Threshold, AcceptanceVerdict Verdict);

/// <summary>Inputs of <see cref="AcceptanceReport.Evaluate"/> (SIM-005). Only the development log is required.</summary>
public sealed record AcceptanceInputs
{
    /// <summary>Creates the inputs with the development log (SIM-005).</summary>
    /// <param name="development">The combined development trade list.</param>
    /// <exception cref="ArgumentNullException"><paramref name="development"/> is null.</exception>
    public AcceptanceInputs(IReadOnlyList<TradeRecord> development)
    {
        ArgumentNullException.ThrowIfNull(development);
        Development = development;
    }

    /// <summary>Combined trade list of the development period (SIM-005).</summary>
    public IReadOnlyList<TradeRecord> Development { get; }

    /// <summary>Combined trade list of the holdout period, or null (SIM-005).</summary>
    public IReadOnlyList<TradeRecord>? Holdout { get; init; }

    /// <summary>Development trades re-run under cost stress (spec 12.1 step 6), or null (SIM-005).</summary>
    public IReadOnlyList<TradeRecord>? DevelopmentCostStress { get; init; }

    /// <summary>Benchmark trades on the development period (spec 12.1 step 10), or null (SIM-005).</summary>
    public IReadOnlyList<TradeRecord>? DevelopmentBenchmark { get; init; }

    /// <summary>Benchmark trades on the holdout period, or null (SIM-005).</summary>
    public IReadOnlyList<TradeRecord>? HoldoutBenchmark { get; init; }

    /// <summary>Chance of passing the Challenge phase from the challenge simulation, 0 to 1, or null (SIM-005).</summary>
    public double? ChallengePassProbability { get; init; }

    /// <summary>Chance of breaching an FTMO floor in the Challenge phase, 0 to 1, or null (SIM-005).</summary>
    public double? ChallengeBreachProbability { get; init; }

    /// <summary>Starting balance for the return-to-drawdown ratio (SIM-005). Default 100,000.</summary>
    public double InitialCapital { get; init; } = 100000.0;

    /// <summary>Risk per trade for the drawdown row, in percent (spec 13.1). Default 0.5.</summary>
    public double DrawdownRiskPercent { get; init; } = 0.5;

    /// <summary>Checks the values (SIM-005).</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range; the parameter name is the property name.</exception>
    public void Validate()
    {
        RequireProbability(ChallengePassProbability, nameof(ChallengePassProbability));
        RequireProbability(ChallengeBreachProbability, nameof(ChallengeBreachProbability));
        if (!double.IsFinite(InitialCapital) || InitialCapital <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialCapital), "InitialCapital must be a finite number greater than zero.");
        }

        if (!double.IsFinite(DrawdownRiskPercent) || DrawdownRiskPercent <= 0.0 || DrawdownRiskPercent > 100.0)
        {
            throw new ArgumentOutOfRangeException(nameof(DrawdownRiskPercent), "DrawdownRiskPercent must be greater than 0 and at most 100.");
        }
    }

    private static void RequireProbability(double? value, string name)
    {
        if (value.HasValue && !(value.Value >= 0.0 && value.Value <= 1.0))
        {
            throw new ArgumentOutOfRangeException(name, name + " must be between 0 and 1.");
        }
    }
}

/// <summary>Rows and overall verdict of the acceptance report (SIM-005).</summary>
public sealed class AcceptanceResult
{
    internal AcceptanceResult(IReadOnlyList<AcceptanceRow> rows)
    {
        Rows = rows;
        bool anyFail = false;
        bool anyMissing = false;
        foreach (AcceptanceRow row in rows)
        {
            anyFail |= row.Verdict == AcceptanceVerdict.Fail;
            anyMissing |= row.Verdict == AcceptanceVerdict.NotEvaluated;
        }

        Overall = anyFail ? AcceptanceVerdict.Fail : anyMissing ? AcceptanceVerdict.NotEvaluated : AcceptanceVerdict.Pass;
    }

    /// <summary>Every row of spec 13.1 that applies to a segment, in the order of the spec (SIM-005).</summary>
    public IReadOnlyList<AcceptanceRow> Rows { get; }

    /// <summary>Fail if any row fails; otherwise NotEvaluated if any row was not evaluated; otherwise Pass (SIM-005).</summary>
    public AcceptanceVerdict Overall { get; }
}
