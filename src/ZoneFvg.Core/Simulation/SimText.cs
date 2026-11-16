using System.Globalization;

namespace ZoneFvg.Core.Simulation;

/// <summary>Invariant number formatting shared by the simulation reports (SIM-002 to SIM-005).</summary>
internal static class SimText
{
    /// <summary>Two decimals, for money, percentages and drawdowns in R.</summary>
    internal static string F2(double value)
    {
        // Adding zero turns a negative zero into zero, so "-0.00" is never printed.
        return (value + 0.0).ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>Three decimals, for average results in R.</summary>
    internal static string F3(double value)
    {
        return (value + 0.0).ToString("0.000", CultureInfo.InvariantCulture);
    }

    /// <summary>Up to three decimals without trailing zeros, for parameters such as 0.5 or 10.</summary>
    internal static string Short(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>A whole number.</summary>
    internal static string Int(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A seed.</summary>
    internal static string Seed(ulong value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A probability as a percentage with one decimal, for example 72.4%.</summary>
    internal static string Percent(double probability)
    {
        return (probability * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }
}
