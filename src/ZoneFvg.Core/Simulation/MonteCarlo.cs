using System;
using System.Collections.Generic;
using System.Text;

namespace ZoneFvg.Core.Simulation;

/// <summary>
/// Monte Carlo of the trade order (SIM-003, spec 12.1 step 8): the results in R are reshuffled
/// into <see cref="MonteCarloParameters.Sequences"/> sequences with a seeded generator, and the
/// distributions of the maximum drawdown and of the rolling average R over a window of trades
/// are reported with the 1st, 5th, 50th and 95th percentiles (nearest rank). Spec 13.3 compares
/// the live 30-trade average with the 1st and 5th percentiles of the rolling average.
/// </summary>
/// <remarks>
/// Each sequence is a Fisher-Yates shuffle of the input in its original order, drawn from one
/// <see cref="Xoshiro256StarStar"/> generator in sequence order. The rolling distribution pools
/// every window of every sequence.
/// </remarks>
public static class MonteCarlo
{
    /// <summary>
    /// Runs the Monte Carlo (SIM-003). The same results, parameters and seed always give the
    /// same output.
    /// </summary>
    /// <param name="resultsR">Results of the trades in R. At least one; every value finite and above -100 / RiskPercent.</param>
    /// <param name="parameters">Number of sequences, risk per trade and window.</param>
    /// <param name="seed">Seed of the generator.</param>
    /// <returns>The distributions.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    /// <exception cref="ArgumentException">The results are empty or contain a value that is not finite or would take the balance to zero or below.</exception>
    public static MonteCarloResult Run(IReadOnlyList<double> resultsR, MonteCarloParameters parameters, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(resultsR);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        RequireResults(resultsR, parameters.RiskPercent);

        var generator = new Xoshiro256StarStar(seed);
        var sequences = new List<IReadOnlyList<double>>(parameters.Sequences);
        var drawdownR = new double[parameters.Sequences];
        var drawdownPercent = new double[parameters.Sequences];
        var windows = new List<double>();
        for (int s = 0; s < parameters.Sequences; s++)
        {
            double[] sequence = Shuffled(resultsR, generator);
            sequences.Add(sequence);
            drawdownR[s] = MaxDrawdownR(sequence);
            drawdownPercent[s] = MaxDrawdownPercent(sequence, parameters.RiskPercent);
            windows.AddRange(RollingAverages(sequence, parameters.WindowTrades));
        }

        PercentileSet? rolling = windows.Count == 0 ? null : NearestRank.Set(windows);
        return new MonteCarloResult(seed, parameters, resultsR.Count, sequences, NearestRank.Set(drawdownR), NearestRank.Set(drawdownPercent), rolling, windows.Count);
    }

    /// <summary>
    /// Largest fall of the cumulative R from its running peak, the peak starting at 0 before the
    /// first trade (SIM-003).
    /// </summary>
    /// <param name="resultsR">Results in R, in trade order.</param>
    /// <returns>The maximum drawdown in R, zero or positive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resultsR"/> is null.</exception>
    public static double MaxDrawdownR(IReadOnlyList<double> resultsR)
    {
        ArgumentNullException.ThrowIfNull(resultsR);
        double cumulative = 0.0;
        double peak = 0.0;
        double worst = 0.0;
        foreach (double r in resultsR)
        {
            cumulative += r;
            peak = Math.Max(peak, cumulative);
            worst = Math.Max(worst, peak - cumulative);
        }

        return worst;
    }

    /// <summary>
    /// Maximum drawdown in percent when every trade risks <paramref name="riskPercent"/> of the
    /// current balance (RSK-001): the balance is multiplied by 1 + R x risk / 100 after each
    /// trade, and the drawdown is the fall from the running peak in percent of that peak, as for
    /// the kill switch (RSK-008). The peak starts at the initial balance (SIM-003).
    /// </summary>
    /// <param name="resultsR">Results in R, in trade order.</param>
    /// <param name="riskPercent">Risk per trade in percent of the balance.</param>
    /// <returns>The maximum drawdown in percent, zero or positive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resultsR"/> is null.</exception>
    public static double MaxDrawdownPercent(IReadOnlyList<double> resultsR, double riskPercent)
    {
        ArgumentNullException.ThrowIfNull(resultsR);
        double balance = 1.0;
        double peak = 1.0;
        double worst = 0.0;
        foreach (double r in resultsR)
        {
            balance *= 1.0 + (r * riskPercent / 100.0);
            peak = Math.Max(peak, balance);
            worst = Math.Max(worst, (peak - balance) / peak * 100.0);
        }

        return worst;
    }

    /// <summary>
    /// The average R of every run of <paramref name="window"/> consecutive trades, in order; empty
    /// when there are fewer trades than the window (SIM-003).
    /// </summary>
    /// <param name="resultsR">Results in R, in trade order.</param>
    /// <param name="window">Number of trades in a window, at least 1.</param>
    /// <returns>One average per window.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resultsR"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="window"/> is below 1.</exception>
    public static IReadOnlyList<double> RollingAverages(IReadOnlyList<double> resultsR, int window)
    {
        ArgumentNullException.ThrowIfNull(resultsR);
        if (window < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "The window must be at least 1 trade.");
        }

        var averages = new List<double>(Math.Max(0, resultsR.Count - window + 1));
        for (int start = 0; start + window <= resultsR.Count; start++)
        {
            // Each window is summed afresh, so no rounding error carries from one window to the next.
            double sum = 0.0;
            for (int i = start; i < start + window; i++)
            {
                sum += resultsR[i];
            }

            averages.Add(sum / window);
        }

        return averages;
    }

    private static void RequireResults(IReadOnlyList<double> resultsR, double riskPercent)
    {
        if (resultsR.Count == 0)
        {
            throw new ArgumentException("The Monte Carlo needs at least one trade.", nameof(resultsR));
        }

        double lowest = -100.0 / riskPercent;
        for (int i = 0; i < resultsR.Count; i++)
        {
            double r = resultsR[i];
            if (!double.IsFinite(r) || r <= lowest)
            {
                throw new ArgumentException("Result " + SimText.Int(i + 1) + " must be a finite number above " + SimText.Short(lowest) + " R, but was " + r.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ".", nameof(resultsR));
            }
        }
    }

    private static double[] Shuffled(IReadOnlyList<double> resultsR, Xoshiro256StarStar generator)
    {
        var sequence = new double[resultsR.Count];
        for (int i = 0; i < sequence.Length; i++)
        {
            sequence[i] = resultsR[i];
        }

        for (int i = sequence.Length - 1; i > 0; i--)
        {
            int j = generator.NextInt(i + 1);
            (sequence[i], sequence[j]) = (sequence[j], sequence[i]);
        }

        return sequence;
    }
}

/// <summary>Parameters of <see cref="MonteCarlo.Run"/> (SIM-003). Immutable; derive changes with <c>with</c>.</summary>
public sealed record MonteCarloParameters
{
    /// <summary>Number of reshuffled sequences (SIM-003, spec 12.1 step 8). Default 1,000.</summary>
    public int Sequences { get; init; } = 1000;

    /// <summary>Risk per trade in percent of the balance for the drawdown in percent (SIM-003). Default 0.5.</summary>
    public double RiskPercent { get; init; } = 0.5;

    /// <summary>Number of trades in a rolling window (SIM-003, spec 13.3). Default 30.</summary>
    public int WindowTrades { get; init; } = 30;

    /// <summary>Checks every value (SIM-003).</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range; the parameter name is the property name.</exception>
    public void Validate()
    {
        if (Sequences < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Sequences), "Sequences must be at least 1.");
        }

        if (!double.IsFinite(RiskPercent) || RiskPercent <= 0.0 || RiskPercent > 100.0)
        {
            throw new ArgumentOutOfRangeException(nameof(RiskPercent), "RiskPercent must be greater than 0 and at most 100.");
        }

        if (WindowTrades < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(WindowTrades), "WindowTrades must be at least 1.");
        }
    }
}

/// <summary>Output of <see cref="MonteCarlo.Run"/> (SIM-003).</summary>
public sealed class MonteCarloResult
{
    internal MonteCarloResult(ulong seed, MonteCarloParameters parameters, int tradeCount, IReadOnlyList<IReadOnlyList<double>> shuffledSequences, PercentileSet maxDrawdownR, PercentileSet maxDrawdownPercent, PercentileSet? rollingAverageR, int rollingWindowCount)
    {
        Seed = seed;
        Parameters = parameters;
        TradeCount = tradeCount;
        ShuffledSequences = shuffledSequences;
        MaxDrawdownR = maxDrawdownR;
        MaxDrawdownPercent = maxDrawdownPercent;
        RollingAverageR = rollingAverageR;
        RollingWindowCount = rollingWindowCount;
    }

    /// <summary>The seed used (SIM-003).</summary>
    public ulong Seed { get; }

    /// <summary>The parameters used (SIM-003).</summary>
    public MonteCarloParameters Parameters { get; }

    /// <summary>Number of sequences (SIM-003).</summary>
    public int Sequences => ShuffledSequences.Count;

    /// <summary>Number of trades in each sequence (SIM-003).</summary>
    public int TradeCount { get; }

    /// <summary>Every reshuffled sequence, in the order drawn (SIM-003).</summary>
    public IReadOnlyList<IReadOnlyList<double>> ShuffledSequences { get; }

    /// <summary>Percentiles of the maximum drawdown in R over the sequences (SIM-003).</summary>
    public PercentileSet MaxDrawdownR { get; }

    /// <summary>Percentiles of the maximum drawdown in percent at the risk per trade (SIM-003).</summary>
    public PercentileSet MaxDrawdownPercent { get; }

    /// <summary>Percentiles of the rolling average R over all windows; null when there are fewer trades than the window (SIM-003).</summary>
    public PercentileSet? RollingAverageR { get; }

    /// <summary>Number of windows in the rolling distribution (SIM-003).</summary>
    public int RollingWindowCount { get; }

    /// <summary>The result as Markdown, with the assumptions first (SIM-003).</summary>
    /// <returns>The report.</returns>
    public string ToMarkdown()
    {
        string window = SimText.Int(Parameters.WindowTrades) + "-trade average R";
        var text = new StringBuilder();
        text.Append("# Monte Carlo of the trade order (SIM-003)\n\n");
        text.Append("Assumptions:\n\n");
        text.Append("- The trades are a sample of the past: past trade distributions may not repeat.\n");
        text.Append("- Each sequence uses every trade exactly once in a random order; trades are treated as independent of each other and of time.\n");
        text.Append("- Drawdown in percent compounds ").Append(SimText.Short(Parameters.RiskPercent)).Append("% risk on the current balance and is measured from the running peak of closed results; open-trade swings are not included.\n");
        text.Append("- Percentiles use the nearest-rank method.\n\n");
        text.Append("Seed: ").Append(SimText.Seed(Seed)).Append(". Sequences: ").Append(SimText.Int(Sequences)).Append(". Trades: ").Append(SimText.Int(TradeCount)).Append(".\n\n");
        text.Append("| Measure | P1 | P5 | P50 | P95 |\n");
        text.Append("|---|---|---|---|---|\n");
        AppendRow(text, "Maximum drawdown (R)", MaxDrawdownR, SimText.F2);
        AppendRow(text, "Maximum drawdown (% at " + SimText.Short(Parameters.RiskPercent) + "% risk)", MaxDrawdownPercent, SimText.F2);
        if (RollingAverageR == null)
        {
            text.Append("| ").Append(window).Append(" | not available: fewer than ").Append(SimText.Int(Parameters.WindowTrades)).Append(" trades | | | |\n");
        }
        else
        {
            AppendRow(text, window, RollingAverageR, SimText.F3);
        }

        return text.ToString();
    }

    private static void AppendRow(StringBuilder text, string measure, PercentileSet set, Func<double, string> format)
    {
        text.Append("| ").Append(measure)
            .Append(" | ").Append(format(set.P1))
            .Append(" | ").Append(format(set.P5))
            .Append(" | ").Append(format(set.P50))
            .Append(" | ").Append(format(set.P95))
            .Append(" |\n");
    }
}
