using System;
using System.Globalization;

namespace ZoneFvg.Core.Execution;

/// <summary>
/// The automatic symbol profile of spec 7.1 (EXE-030): which set of values the symbol-dependent
/// parameters take when they are 0. The names are written into the start line (EXE-032).
/// </summary>
public enum SymbolProfileKind
{
    /// <summary>No automatic profile: other metals, indices, crypto, gold in another currency.</summary>
    None = 0,

    /// <summary>A currency pair: values in standard pips.</summary>
    Fx = 1,

    /// <summary>Gold quoted in US dollars: values in US dollars of price, converted with the pip size.</summary>
    Gold = 2,
}

/// <summary>
/// The values of the automatic symbol profiles of spec 7.1 (EXE-030): for a currency pair in
/// pips, for gold in US dollars of price; the size cap in units for both. Constants only.
/// </summary>
public static class SymbolProfileValues
{
    /// <summary>The cTrader name of the spread filter (EXE-030).</summary>
    public const string MaxSpreadPips = "MaxSpreadPips";

    /// <summary>The cTrader name of the smallest stop (EXE-030).</summary>
    public const string MinStopPips = "MinStopPips";

    /// <summary>The cTrader name of the smallest Choch stop (EXE-030).</summary>
    public const string ChochMinStopPips = "ChochMinStopPips";

    /// <summary>The cTrader name of the size cap (EXE-030).</summary>
    public const string MaxVolumeUnits = "MaxVolumeUnits";

    /// <summary>
    /// The profile's value of a parameter (EXE-030): Fx 1.5, 12 and 5 pips and 3,000,000 units;
    /// Gold 0.40, 8.00 and 3.00 USD of price and 1,000 units. NaN for profile None.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="parameter">One of the four cTrader names of this class.</param>
    /// <returns>The value in the profile's unit (<see cref="Unit"/>), units for MaxVolumeUnits.</returns>
    /// <exception cref="ArgumentException">The name is not one of the four.</exception>
    public static double Of(SymbolProfileKind profile, string parameter)
    {
        bool gold = profile == SymbolProfileKind.Gold;
        if (profile != SymbolProfileKind.Fx && !gold)
        {
            return double.NaN;
        }

        return parameter switch
        {
            MaxSpreadPips => gold ? 0.4 : 1.5,
            MinStopPips => gold ? 8.0 : 12.0,
            ChochMinStopPips => gold ? 3.0 : 5.0,
            MaxVolumeUnits => gold ? 1000.0 : 3000000.0,
            _ => throw new ArgumentException("Unknown symbol-dependent parameter " + parameter + ".", nameof(parameter)),
        };
    }

    /// <summary>The unit of the profile's price values (EXE-032): <c>USD</c> for Gold, <c>pips</c> otherwise.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The unit text.</returns>
    public static string Unit(SymbolProfileKind profile)
    {
        return profile == SymbolProfileKind.Gold ? "USD" : "pips";
    }
}

/// <summary>
/// The result of the symbol profile at start (EXE-030, EXE-031, EXE-032): the profile, the pip size
/// the broker reported, the effective values of the four symbol-dependent parameters, which of them
/// were automatic (0 in the cBot), and the spread note. Immutable.
/// </summary>
public sealed record SymbolProfileResult
{
    /// <summary>The profile (EXE-030).</summary>
    public SymbolProfileKind Profile { get; init; }

    /// <summary>The symbol's pip size at start, in price units (EXE-030).</summary>
    public double PipSize { get; init; }

    /// <summary>Effective MaxSpreadPips, in pips (EXE-030).</summary>
    public double MaxSpreadPips { get; init; }

    /// <summary>Effective MinStopPips, in pips (EXE-030).</summary>
    public double MinStopPips { get; init; }

    /// <summary>Effective ChochMinStopPips, in pips (EXE-030).</summary>
    public double ChochMinStopPips { get; init; }

    /// <summary>Effective MaxVolumeUnits, in units (EXE-030).</summary>
    public double MaxVolumeUnits { get; init; }

    /// <summary>True when MaxSpreadPips was 0, automatic (EXE-030).</summary>
    public bool MaxSpreadPipsAuto { get; init; }

    /// <summary>True when MinStopPips was 0, automatic (EXE-030).</summary>
    public bool MinStopPipsAuto { get; init; }

    /// <summary>True when ChochMinStopPips was 0, automatic (EXE-030).</summary>
    public bool ChochMinStopPipsAuto { get; init; }

    /// <summary>True when MaxVolumeUnits was 0, automatic (EXE-030).</summary>
    public bool MaxVolumeUnitsAuto { get; init; }

    /// <summary>The Log line about a spread at start above MaxSpreadPips, or null (EXE-031).</summary>
    public string? SpreadNote { get; init; }

    /// <summary>
    /// The text of an automatic value in the parameter hash (EXE-032): <c>auto:</c>, the profile's
    /// value in the round-trip format and its unit, for example <c>auto:0.4USD</c> or
    /// <c>auto:1.5pips</c>. Null when the value was set by hand or the profile is None, so the
    /// number is written as before.
    /// </summary>
    /// <param name="parameter">MaxSpreadPips, MinStopPips or ChochMinStopPips.</param>
    /// <returns>The text, or null.</returns>
    /// <exception cref="ArgumentException">The name is not one of the three.</exception>
    public string? HashText(string parameter)
    {
        bool auto = parameter switch
        {
            SymbolProfileValues.MaxSpreadPips => MaxSpreadPipsAuto,
            SymbolProfileValues.MinStopPips => MinStopPipsAuto,
            SymbolProfileValues.ChochMinStopPips => ChochMinStopPipsAuto,
            _ => throw new ArgumentException("Only MaxSpreadPips, MinStopPips and ChochMinStopPips are in the hash.", nameof(parameter)),
        };
        if (!auto || Profile == SymbolProfileKind.None)
        {
            return null;
        }

        return "auto:" + SymbolProfileValues.Of(Profile, parameter).ToString("R", CultureInfo.InvariantCulture) + SymbolProfileValues.Unit(Profile);
    }
}
