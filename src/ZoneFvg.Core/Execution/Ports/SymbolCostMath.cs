using System;

namespace ZoneFvg.Core.Execution.Ports;

/// <summary>How <see cref="SymbolCostMath.Convert"/> turns an amount into another asset (RSK-001, EXE-004).</summary>
public enum AssetConversionPath
{
    /// <summary>Same asset: the amount is kept.</summary>
    Same,

    /// <summary>From the symbol's quote asset to its base asset: the amount is divided by the price.</summary>
    DivideByPrice,

    /// <summary>From the symbol's base asset to its quote asset: the amount is multiplied by the price.</summary>
    MultiplyByPrice,

    /// <summary>Any other pair of assets: the platform's asset converter.</summary>
    Converter,
}

/// <summary>
/// Pip value and commission of one unit of volume in the account currency (RSK-001, EXE-002,
/// EXE-004), for the adapter's symbol data. The platform's asset converter returned 0 in the
/// owner's backtest of 2026-11-17, even for USD to USD, so the converter is used only when the
/// result does not follow from the symbol's own price: an amount in the quote currency is the
/// same amount when the account currency is the quote currency, and that amount divided by the
/// price when the account currency is the base currency.
/// </summary>
/// <remarks>
/// Fail closed: a missing or invalid value (an empty asset name, a price or a conversion rate
/// that is not a finite number greater than zero) gives NaN, which the core rejects
/// (<c>RiskMath.InvalidSymbolData</c>, <c>PreTradeFilters</c>), so no order is placed. No value
/// is ever guessed.
/// </remarks>
public static class SymbolCostMath
{
    private const string Usd = "USD";
    private const double Million = 1000000.0;

    /// <summary>
    /// Tells how an amount in <paramref name="fromAsset"/> is turned into <paramref name="toAsset"/>
    /// for a symbol with the given base and quote assets (RSK-001).
    /// </summary>
    /// <param name="fromAsset">Asset of the amount.</param>
    /// <param name="toAsset">Asset wanted.</param>
    /// <param name="baseAsset">The symbol's base asset, for example EUR for EURUSD.</param>
    /// <param name="quoteAsset">The symbol's quote asset, for example USD for EURUSD.</param>
    /// <returns>The conversion path.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static AssetConversionPath PathOf(string fromAsset, string toAsset, string baseAsset, string quoteAsset)
    {
        ArgumentNullException.ThrowIfNull(fromAsset);
        ArgumentNullException.ThrowIfNull(toAsset);
        ArgumentNullException.ThrowIfNull(baseAsset);
        ArgumentNullException.ThrowIfNull(quoteAsset);
        if (string.Equals(fromAsset, toAsset, StringComparison.Ordinal))
        {
            return AssetConversionPath.Same;
        }

        if (string.Equals(fromAsset, quoteAsset, StringComparison.Ordinal) && string.Equals(toAsset, baseAsset, StringComparison.Ordinal))
        {
            return AssetConversionPath.DivideByPrice;
        }

        if (string.Equals(fromAsset, baseAsset, StringComparison.Ordinal) && string.Equals(toAsset, quoteAsset, StringComparison.Ordinal))
        {
            return AssetConversionPath.MultiplyByPrice;
        }

        return AssetConversionPath.Converter;
    }

    /// <summary>
    /// Turns <paramref name="amount"/> in <paramref name="fromAsset"/> into
    /// <paramref name="toAsset"/> along <see cref="PathOf"/> (RSK-001, EXE-004). The converter is
    /// asked for the rate of one unit and must give a finite number greater than zero.
    /// </summary>
    /// <param name="amount">The amount.</param>
    /// <param name="fromAsset">Asset of the amount.</param>
    /// <param name="toAsset">Asset wanted.</param>
    /// <param name="baseAsset">The symbol's base asset.</param>
    /// <param name="quoteAsset">The symbol's quote asset.</param>
    /// <param name="price">The symbol's price: quote currency per unit of base currency.</param>
    /// <param name="convert">The platform's converter: value, from asset, to asset.</param>
    /// <returns>The amount in <paramref name="toAsset"/>, or NaN when a needed value is invalid.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static double Convert(double amount, string fromAsset, string toAsset, string baseAsset, string quoteAsset, double price, Func<double, string, string, double> convert)
    {
        ArgumentNullException.ThrowIfNull(convert);
        AssetConversionPath path = PathOf(fromAsset, toAsset, baseAsset, quoteAsset);
        if (fromAsset.Length == 0 || toAsset.Length == 0 || baseAsset.Length == 0 || quoteAsset.Length == 0)
        {
            return double.NaN;
        }

        switch (path)
        {
            case AssetConversionPath.Same:
                return amount;
            case AssetConversionPath.DivideByPrice:
                return IsPositive(price) ? amount / price : double.NaN;
            case AssetConversionPath.MultiplyByPrice:
                return IsPositive(price) ? amount * price : double.NaN;
            default:
                double rate = convert(1.0, fromAsset, toAsset);
                return IsPositive(rate) ? amount * rate : double.NaN;
        }
    }

    /// <summary>
    /// Value of one pip for one unit of volume in the account currency (RSK-001): one pip is
    /// <paramref name="pipSize"/> in the quote currency per unit.
    /// </summary>
    /// <param name="pipSize">Size of one pip in price units.</param>
    /// <param name="baseAsset">The symbol's base asset.</param>
    /// <param name="quoteAsset">The symbol's quote asset.</param>
    /// <param name="accountAsset">The account's deposit asset.</param>
    /// <param name="price">The symbol's price, used only when the account currency is the base currency.</param>
    /// <param name="convert">The platform's converter, used only for a cross pair.</param>
    /// <returns>The pip value per unit, or NaN.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static double PipValuePerUnit(double pipSize, string baseAsset, string quoteAsset, string accountAsset, double price, Func<double, string, string, double> convert)
    {
        return Convert(pipSize, quoteAsset, accountAsset, baseAsset, quoteAsset, price, convert);
    }

    /// <summary>
    /// Commission for one side (opening or closing) of one unit of volume in the account currency,
    /// for the commission type "USD per million USD of volume" (EXE-004): one unit is one unit of
    /// the base currency, whose value in USD is 1 when the base is USD and the price when the quote
    /// is USD.
    /// </summary>
    /// <param name="usdPerMillion">The commission in USD per million USD of volume.</param>
    /// <param name="baseAsset">The symbol's base asset.</param>
    /// <param name="quoteAsset">The symbol's quote asset.</param>
    /// <param name="accountAsset">The account's deposit asset.</param>
    /// <param name="price">The symbol's price.</param>
    /// <param name="convert">The platform's converter, used only where the price does not give the result.</param>
    /// <returns>The commission per unit and side, or NaN.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static double UsdPerMillionPerUnit(double usdPerMillion, string baseAsset, string quoteAsset, string accountAsset, double price, Func<double, string, string, double> convert)
    {
        double unitInUsd = Convert(1.0, baseAsset, Usd, baseAsset, quoteAsset, price, convert);
        return Convert(usdPerMillion / Million * unitInUsd, Usd, accountAsset, baseAsset, quoteAsset, price, convert);
    }

    private static bool IsPositive(double value)
    {
        return double.IsFinite(value) && value > 0.0;
    }
}
