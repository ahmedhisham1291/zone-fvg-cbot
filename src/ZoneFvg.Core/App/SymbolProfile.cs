using System;
using System.Globalization;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.App;

/// <summary>
/// The automatic symbol profile of spec 7.1 (EXE-030, EXE-031): picks the profile from the symbol's
/// asset names, resolves the four symbol-dependent cBot values (0 = automatic) to effective values,
/// checks values set by hand against the profile and writes the spread note. The adapter calls it
/// at start before any of the four values is checked or used. Pure: no clock, no culture other than
/// the invariant one; a refusal is an <see cref="ArgumentException"/> whose parameter name is the
/// cTrader name, or <c>PipSize</c> for the pip-size refusals.
/// </summary>
public static class SymbolProfile
{
    // EXE-030: the currencies of a currency pair, spec 7.1, compared ignoring case. Never written.
    private static readonly string[] Currencies =
    {
        "USD", "EUR", "GBP", "JPY", "AUD", "NZD", "CAD", "CHF", "SEK", "NOK",
        "DKK", "SGD", "HKD", "MXN", "ZAR", "PLN", "HUF", "CZK", "TRY", "CNH",
    };

    /// <summary>Relative tolerance of a currency pair's pip size against the standard one (EXE-030).</summary>
    public const double PipSizeTolerance = 1e-9;

    /// <summary>A value set by hand is accepted from the profile value divided by this to the profile value times this (EXE-031).</summary>
    public const double RangeFactor = 10.0;

    private const int Decimals = 9;

    /// <summary>
    /// Resolves the four symbol-dependent values (EXE-030, EXE-031): the pip size is checked first,
    /// then MaxSpreadPips, MinStopPips, ChochMinStopPips and MaxVolumeUnits in this order; the first
    /// refusal is thrown.
    /// </summary>
    /// <param name="symbol">The symbol name, for the messages.</param>
    /// <param name="baseAsset">The base asset name (cTrader <c>Symbol.BaseAsset.Name</c>).</param>
    /// <param name="quoteAsset">The quote asset name (cTrader <c>Symbol.QuoteAsset.Name</c>).</param>
    /// <param name="pipSize">The pip size at start, in price units.</param>
    /// <param name="spread">The spread at start, in price units, for the spread note only.</param>
    /// <param name="maxSpreadPips">MaxSpreadPips as the cBot has it; 0 = automatic.</param>
    /// <param name="minStopPips">MinStopPips; 0 = automatic.</param>
    /// <param name="chochMinStopPips">ChochMinStopPips; 0 = automatic.</param>
    /// <param name="maxVolumeUnits">MaxVolumeUnits; 0 = automatic.</param>
    /// <returns>The profile and the effective values.</returns>
    /// <exception cref="ArgumentException">A refusal of spec 7.1; the message says why.</exception>
    public static SymbolProfileResult Resolve(string symbol, string baseAsset, string quoteAsset, double pipSize, double spread, double maxSpreadPips, double minStopPips, double chochMinStopPips, double maxVolumeUnits)
    {
        SymbolProfileKind profile = KindOf(baseAsset, quoteAsset);
        CheckPipSize(symbol, quoteAsset, pipSize, profile);
        var context = new Context(symbol, baseAsset, quoteAsset, pipSize, profile);
        double spreadLimit = context.Effective(SymbolProfileValues.MaxSpreadPips, maxSpreadPips);
        double minStop = context.Effective(SymbolProfileValues.MinStopPips, minStopPips);
        double chochMinStop = context.Effective(SymbolProfileValues.ChochMinStopPips, chochMinStopPips);
        double maxVolume = context.Effective(SymbolProfileValues.MaxVolumeUnits, maxVolumeUnits);
        return new SymbolProfileResult
        {
            Profile = profile,
            PipSize = pipSize,
            MaxSpreadPips = spreadLimit,
            MinStopPips = minStop,
            ChochMinStopPips = chochMinStop,
            MaxVolumeUnits = maxVolume,
            MaxSpreadPipsAuto = IsAuto(maxSpreadPips),
            MinStopPipsAuto = IsAuto(minStopPips),
            ChochMinStopPipsAuto = IsAuto(chochMinStopPips),
            MaxVolumeUnitsAuto = IsAuto(maxVolumeUnits),
            SpreadNote = SpreadNote(spread, spreadLimit, pipSize),
        };
    }

    /// <summary>The profile of the asset names (EXE-030): Gold for XAU against USD, Fx for two listed currencies, otherwise None.</summary>
    /// <param name="baseAsset">The base asset name.</param>
    /// <param name="quoteAsset">The quote asset name.</param>
    /// <returns>The profile.</returns>
    public static SymbolProfileKind KindOf(string? baseAsset, string? quoteAsset)
    {
        if (Same(baseAsset, "XAU") && Same(quoteAsset, "USD"))
        {
            return SymbolProfileKind.Gold;
        }

        return IsCurrency(baseAsset) && IsCurrency(quoteAsset) ? SymbolProfileKind.Fx : SymbolProfileKind.None;
    }

    private static bool IsAuto(double value)
    {
        return value.Equals(0.0);
    }

    private static bool Same(string? a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCurrency(string? name)
    {
        foreach (string currency in Currencies)
        {
            if (Same(name, currency))
            {
                return true;
            }
        }

        return false;
    }

    private static void CheckPipSize(string symbol, string quoteAsset, double pipSize, SymbolProfileKind profile)
    {
        if (!double.IsFinite(pipSize) || pipSize <= 0.0)
        {
            throw new ArgumentException(symbol + " reports a pip size of " + Text(pipSize) + ", which is not a finite number greater than 0; the bot does not start.", "PipSize");
        }

        if (profile != SymbolProfileKind.Fx)
        {
            return;
        }

        double standard = Same(quoteAsset, "JPY") ? 0.01 : 0.0001;
        if (Math.Abs(pipSize - standard) > PipSizeTolerance * standard)
        {
            throw new ArgumentException(symbol + " reports a pip size of " + Text(pipSize) + "; the currency-pair values are in pips of " + Text(standard) + "; the bot does not start.", "PipSize");
        }
    }

    private static string? SpreadNote(double spread, double spreadLimitPips, double pipSize)
    {
        if (!double.IsFinite(spread) || spread < 0.0 || !PriceMath.GreaterThan(spread, spreadLimitPips * pipSize, PriceMath.DefaultTolerance))
        {
            return null;
        }

        return "spread at start " + Text(Round(spread / pipSize)) + " pips (" + Text(Round(spread)) + " in price) is above MaxSpreadPips "
            + Text(spreadLimitPips) + "; entries are skipped while it lasts.";
    }

    private static double Round(double value)
    {
        return Math.Round(value, Decimals, MidpointRounding.AwayFromZero);
    }

    private static string Text(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    // The symbol's data, shared by the four values.
    private sealed class Context
    {
        private readonly string _symbol;
        private readonly string _base;
        private readonly string _quote;
        private readonly double _pipSize;
        private readonly SymbolProfileKind _profile;

        public Context(string symbol, string baseAsset, string quoteAsset, double pipSize, SymbolProfileKind profile)
        {
            _symbol = symbol;
            _base = baseAsset;
            _quote = quoteAsset;
            _pipSize = pipSize;
            _profile = profile;
        }

        // EXE-030, EXE-031: the effective value of one parameter, or a refusal.
        public double Effective(string name, double value)
        {
            if (!double.IsFinite(value) || value < 0.0)
            {
                throw new ArgumentException(name + " must be 0 (automatic) or a finite number greater than 0; got " + Text(value) + ".", name);
            }

            if (_profile == SymbolProfileKind.None)
            {
                if (IsAuto(value))
                {
                    throw new ArgumentException(name + " is 0 (automatic), but " + _symbol + " (base " + _base + ", quote " + _quote + ") has no automatic profile; set " + name + " by hand.", name);
                }

                return value;
            }

            double reference = SymbolProfileValues.Of(_profile, name);
            bool inPrice = _profile == SymbolProfileKind.Gold && !string.Equals(name, SymbolProfileValues.MaxVolumeUnits, StringComparison.Ordinal);
            double automatic = inPrice ? Round(reference / _pipSize) : reference;
            if (IsAuto(value))
            {
                return automatic;
            }

            double measured = inPrice ? value * _pipSize : value;
            double low = reference / RangeFactor;
            double high = reference * RangeFactor;
            if (PriceMath.LessThan(measured, low, PriceMath.DefaultTolerance) || PriceMath.GreaterThan(measured, high, PriceMath.DefaultTolerance))
            {
                throw new ArgumentException(OutOfRange(name, value, measured, low, high, automatic, reference, inPrice), name);
            }

            return value;
        }

        private string OutOfRange(string name, double value, double measured, double low, double high, double automatic, double reference, bool inPrice)
        {
            string set = "; set 0 for the automatic value " + Text(automatic);
            if (inPrice)
            {
                return name + " " + Text(value) + " is " + Text(Round(measured)) + " USD of price for " + _symbol + " at its pip size " + Text(_pipSize)
                    + "; accepted for gold: " + Text(Round(low)) + " to " + Text(Round(high)) + " USD" + set + " (" + Text(reference) + " USD).";
            }

            string unit = string.Equals(name, SymbolProfileValues.MaxVolumeUnits, StringComparison.Ordinal) ? " units" : " pips";
            string where = _profile == SymbolProfileKind.Gold ? "gold (" + _symbol + ")" : "the currency pair " + _symbol;
            return name + " " + Text(value) + " is outside the range for " + where + ": " + Text(Round(low)) + " to " + Text(Round(high)) + unit + set + ".";
        }
    }
}
