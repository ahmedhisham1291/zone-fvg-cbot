using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Risk;

/// <summary>Small shared helpers of the risk module. No state.</summary>
internal static class RiskMath
{
    /// <summary>True for a finite number greater than zero.</summary>
    internal static bool IsPositive(double value)
    {
        return double.IsFinite(value) && value > 0.0;
    }

    /// <summary>True for a finite number that is not negative.</summary>
    internal static bool IsNonNegative(double value)
    {
        return double.IsFinite(value) && value >= 0.0;
    }

    /// <summary>True for <see cref="Side.Buy"/> and <see cref="Side.Sell"/>.</summary>
    internal static bool IsDefined(Side side)
    {
        return side == Side.Buy || side == Side.Sell;
    }

    /// <summary>
    /// True when <paramref name="price"/> is strictly on the loss side of entry: below it for a
    /// buy, above it for a sell. A price equal to entry within the tolerance is on neither side.
    /// </summary>
    internal static bool IsOnLossSide(Side side, double entry, double price)
    {
        if (side == Side.Buy)
        {
            return PriceMath.LessThan(price, entry, PriceMath.DefaultTolerance);
        }

        return PriceMath.GreaterThan(price, entry, PriceMath.DefaultTolerance);
    }

    /// <summary>
    /// True when <paramref name="price"/> is strictly on the profit side of entry: above it for a
    /// buy, below it for a sell.
    /// </summary>
    internal static bool IsOnProfitSide(Side side, double entry, double price)
    {
        if (side == Side.Buy)
        {
            return PriceMath.GreaterThan(price, entry, PriceMath.DefaultTolerance);
        }

        return PriceMath.LessThan(price, entry, PriceMath.DefaultTolerance);
    }

    /// <summary>
    /// Result in money if a trade of <paramref name="volumeUnits"/> entered at
    /// <paramref name="entry"/> closed at <paramref name="exit"/>, before commission: positive
    /// for a profit, negative for a loss.
    /// </summary>
    internal static double ResultAt(Side side, double entry, double exit, double volumeUnits, SymbolInfo symbol)
    {
        double move = side == Side.Buy ? exit - entry : entry - exit;
        return PriceMath.ToPips(move, symbol.PipSize) * symbol.PipValuePerUnit * volumeUnits;
    }

    /// <summary>Returns a description of the first invalid value of the symbol data, or null when it is usable.</summary>
    internal static string? InvalidSymbolData(SymbolInfo symbol)
    {
        if (string.IsNullOrEmpty(symbol.Name) || string.IsNullOrEmpty(symbol.BaseCurrency) || string.IsNullOrEmpty(symbol.QuoteCurrency))
        {
            return "symbol name and currencies must not be empty";
        }

        if (!IsPositive(symbol.PipSize) || !IsPositive(symbol.PipValuePerUnit))
        {
            return "pip size and pip value of " + symbol.Name + " must be finite numbers greater than zero";
        }

        if (!IsPositive(symbol.VolumeMinUnits) || !IsPositive(symbol.VolumeStepUnits))
        {
            return "minimum volume and volume step of " + symbol.Name + " must be finite numbers greater than zero";
        }

        if (!IsNonNegative(symbol.RoundTripCommissionPerUnit))
        {
            return "commission of " + symbol.Name + " must be a finite number that is not negative";
        }

        return null;
    }
}
