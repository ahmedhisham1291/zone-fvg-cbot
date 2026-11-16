using System;
using System.Collections.Generic;

namespace ZoneFvg.Core.Simulation;

/// <summary>
/// Percentiles by the nearest-rank method (SIM-003, SIM-004): the P-th percentile of n values is
/// the value at rank ceil(P x n / 100) in ascending order, ranks counted from 1. The result is
/// always one of the values.
/// </summary>
public static class NearestRank
{
    /// <summary>The P-th percentile of the values by nearest rank (SIM-003, SIM-004).</summary>
    /// <param name="values">The values, in any order. Not changed.</param>
    /// <param name="percent">P, a whole number from 1 to 100.</param>
    /// <returns>The value at rank ceil(P x n / 100).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percent"/> is outside 1 to 100.</exception>
    public static double Percentile(IReadOnlyList<double> values, int percent)
    {
        return OfSorted(SortedCopy(values), percent);
    }

    /// <summary>The 1st, 5th, 50th and 95th percentiles of the values by nearest rank (SIM-003).</summary>
    /// <param name="values">The values, in any order. Not changed.</param>
    /// <returns>The four percentiles.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> is empty.</exception>
    public static PercentileSet Set(IReadOnlyList<double> values)
    {
        double[] sorted = SortedCopy(values);
        return new PercentileSet(OfSorted(sorted, 1), OfSorted(sorted, 5), OfSorted(sorted, 50), OfSorted(sorted, 95));
    }

    internal static double OfSorted(double[] sorted, int percent)
    {
        if (percent < 1 || percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), "The percentile must be a whole number from 1 to 100.");
        }

        long rank = ((percent * (long)sorted.Length) + 99L) / 100L;
        return sorted[rank - 1];
    }

    private static double[] SortedCopy(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("A percentile needs at least one value.", nameof(values));
        }

        var sorted = new double[values.Count];
        for (int i = 0; i < sorted.Length; i++)
        {
            sorted[i] = values[i];
        }

        Array.Sort(sorted);
        return sorted;
    }
}

/// <summary>The 1st, 5th, 50th and 95th percentiles of a distribution, by nearest rank (SIM-003).</summary>
/// <param name="P1">1st percentile.</param>
/// <param name="P5">5th percentile.</param>
/// <param name="P50">50th percentile (median).</param>
/// <param name="P95">95th percentile.</param>
public sealed record PercentileSet(double P1, double P5, double P50, double P95);
