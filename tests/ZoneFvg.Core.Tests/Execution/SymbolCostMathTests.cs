using Xunit;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Pip value and commission in the account currency (RSK-001, EXE-004). The owner's backtest of
/// 2026-11-17 got 0 from the platform's asset converter even for USD to USD, so every signal was
/// rejected. Where the result follows from the symbol's own price, the converter is not used.
/// </summary>
public class SymbolCostMathTests
{
    private const double Tolerance = 1e-15;

    /// <summary>A converter that must not be called.</summary>
    private static double NoConverter(double value, string from, string to)
    {
        throw new InvalidOperationException("converter called for " + from + " to " + to);
    }

    /// <summary>The backtest's converter: 0 for everything.</summary>
    private static double ZeroConverter(double value, string from, string to)
    {
        return 0.0;
    }

    private static SymbolInfo Symbol(string baseAsset, string quoteAsset, double pipSize, double pipValue, double commission)
    {
        return new SymbolInfo(baseAsset + quoteAsset, baseAsset, quoteAsset, pipSize, pipValue, 1000.0, 1000.0, 100000000.0, 0.00001, true, commission);
    }

    // ------------------------------------------------------------------ pip value

    [Fact]
    [Trait("Req", "RSK-001")]
    public void PipValuePerUnit_QuoteIsAccount_IsThePipSize()
    {
        Assert.Equal(0.0001, SymbolCostMath.PipValuePerUnit(0.0001, "EUR", "USD", "USD", 1.16, NoConverter));
        Assert.Equal(0.0001, SymbolCostMath.PipValuePerUnit(0.0001, "GBP", "USD", "USD", 1.30, NoConverter));
        Assert.Equal(0.0001, SymbolCostMath.PipValuePerUnit(0.0001, "AUD", "USD", "USD", 0.65, NoConverter));
        Assert.Equal(AssetConversionPath.Same, SymbolCostMath.PathOf("USD", "USD", "EUR", "USD"));
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void PipValuePerUnit_QuoteIsAccountWithNoPrice_StillThePipSize()
    {
        // The price is not needed when the quote currency is the account currency.
        Assert.Equal(0.0001, SymbolCostMath.PipValuePerUnit(0.0001, "EUR", "USD", "USD", double.NaN, NoConverter));
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void PipValuePerUnit_BaseIsAccount_IsPipSizeDividedByPrice()
    {
        double value = SymbolCostMath.PipValuePerUnit(0.01, "USD", "JPY", "USD", 150.00, NoConverter);

        Assert.Equal(0.01 / 150.00, value, Tolerance);
        Assert.Equal(AssetConversionPath.DivideByPrice, SymbolCostMath.PathOf("JPY", "USD", "USD", "JPY"));
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void PipValuePerUnit_CrossPair_UsesTheConverter()
    {
        var calls = new List<string>();
        double Convert(double value, string from, string to)
        {
            calls.Add(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + from + " " + to);
            return value * 1.27;
        }

        double result = SymbolCostMath.PipValuePerUnit(0.0001, "EUR", "GBP", "USD", 0.86, Convert);

        Assert.Equal(0.0001 * 1.27, result, Tolerance);
        Assert.Equal(new[] { "1 GBP USD" }, calls);
        Assert.Equal(AssetConversionPath.Converter, SymbolCostMath.PathOf("GBP", "USD", "EUR", "GBP"));
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void PipValuePerUnit_CrossPairConverterReturnsZero_IsRejectedByTheCore()
    {
        double value = SymbolCostMath.PipValuePerUnit(0.0001, "EUR", "GBP", "USD", 0.86, ZeroConverter);

        Assert.True(double.IsNaN(value));
        Assert.NotNull(RiskMath.InvalidSymbolData(Symbol("EUR", "GBP", 0.0001, value, 0.0)));
    }

    [Theory]
    [Trait("Req", "RSK-001")]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1.27)]
    public void PipValuePerUnit_CrossPairConverterInvalid_IsNaN(double rate)
    {
        double value = SymbolCostMath.PipValuePerUnit(0.0001, "EUR", "GBP", "USD", 0.86, (v, f, t) => v * rate);

        Assert.True(double.IsNaN(value));
    }

    [Theory]
    [Trait("Req", "RSK-001")]
    [InlineData(0.0)]
    [InlineData(-150.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void PipValuePerUnit_BaseIsAccountWithInvalidPrice_IsRejectedByTheCore(double price)
    {
        double value = SymbolCostMath.PipValuePerUnit(0.01, "USD", "JPY", "USD", price, NoConverter);

        Assert.True(double.IsNaN(value));
        Assert.NotNull(RiskMath.InvalidSymbolData(Symbol("USD", "JPY", 0.01, value, 0.0)));
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void PipValuePerUnit_EmptyAssetName_IsNaN()
    {
        Assert.True(double.IsNaN(SymbolCostMath.PipValuePerUnit(0.0001, "EUR", "USD", string.Empty, 1.16, NoConverter)));
        Assert.True(double.IsNaN(SymbolCostMath.PipValuePerUnit(0.0001, string.Empty, "USD", "USD", 1.16, NoConverter)));
        Assert.True(double.IsNaN(SymbolCostMath.PipValuePerUnit(0.0001, "EUR", string.Empty, "USD", 1.16, NoConverter)));
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void PipValuePerUnit_NullArgument_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SymbolCostMath.PipValuePerUnit(0.0001, "EUR", "USD", "USD", 1.16, null!));
        Assert.Throws<ArgumentNullException>(() => SymbolCostMath.PipValuePerUnit(0.0001, null!, "USD", "USD", 1.16, NoConverter));
        Assert.Throws<ArgumentNullException>(() => SymbolCostMath.PipValuePerUnit(0.0001, "EUR", null!, "USD", 1.16, NoConverter));
        Assert.Throws<ArgumentNullException>(() => SymbolCostMath.PipValuePerUnit(0.0001, "EUR", "USD", null!, 1.16, NoConverter));
    }

    // ------------------------------------------------------------------ conversions

    [Fact]
    [Trait("Req", "EXE-004")]
    public void Convert_BaseToQuote_MultipliesByThePrice()
    {
        // One EUR is 1.16 USD at EURUSD 1.16; the account is USD.
        Assert.Equal(1.16, SymbolCostMath.Convert(1.0, "EUR", "USD", "EUR", "USD", 1.16, NoConverter), Tolerance);
        Assert.Equal(AssetConversionPath.MultiplyByPrice, SymbolCostMath.PathOf("EUR", "USD", "EUR", "USD"));
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void Convert_ZeroAmountThroughTheConverter_StaysZeroButAnInvalidRateIsNaN()
    {
        Assert.Equal(0.0, SymbolCostMath.Convert(0.0, "GBP", "USD", "EUR", "GBP", 0.86, (v, f, t) => v * 1.27));
        Assert.True(double.IsNaN(SymbolCostMath.Convert(0.0, "GBP", "USD", "EUR", "GBP", 0.86, ZeroConverter)));
    }

    // ------------------------------------------------------------------ commission

    [Fact]
    [Trait("Req", "EXE-004")]
    public void UsdPerMillionPerUnit_EurUsdAtOnePointOneSixUsdAccount_IsExact()
    {
        // 2.5 USD per million USD of volume; one unit of EUR is 1.16 USD.
        double perUnit = SymbolCostMath.UsdPerMillionPerUnit(2.5, "EUR", "USD", "USD", 1.16, NoConverter);

        Assert.Equal(2.5 / 1000000.0 * 1.16, perUnit, Tolerance);
        Assert.Equal(0.29, perUnit * 100000.0, 1e-12);
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void UsdPerMillionPerUnit_UsdJpyUsdAccount_IsExactAndNeedsNoPrice()
    {
        // One unit of USDJPY is one USD.
        Assert.Equal(2.5 / 1000000.0, SymbolCostMath.UsdPerMillionPerUnit(2.5, "USD", "JPY", "USD", 150.00, NoConverter), Tolerance);
        Assert.Equal(2.5 / 1000000.0, SymbolCostMath.UsdPerMillionPerUnit(2.5, "USD", "JPY", "USD", double.NaN, NoConverter), Tolerance);
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void UsdPerMillionPerUnit_EurUsdEurAccount_ConvertsUsdToEurWithThePrice()
    {
        // One EUR is 1.16 USD, the commission in USD is divided by 1.16 for a EUR account: 2.5 per million EUR.
        Assert.Equal(2.5 / 1000000.0, SymbolCostMath.UsdPerMillionPerUnit(2.5, "EUR", "USD", "EUR", 1.16, NoConverter), Tolerance);
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void UsdPerMillionPerUnit_CrossPairConverterReturnsZero_IsRejectedByTheCore()
    {
        double perUnit = SymbolCostMath.UsdPerMillionPerUnit(2.5, "EUR", "GBP", "USD", 0.86, ZeroConverter);

        Assert.True(double.IsNaN(perUnit));
        Assert.NotNull(RiskMath.InvalidSymbolData(Symbol("EUR", "GBP", 0.0001, 0.000127, 2.0 * perUnit)));
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void UsdPerMillionPerUnit_CrossPair_UsesTheConverterForBothSteps()
    {
        var calls = new List<string>();
        double Convert(double value, string from, string to)
        {
            calls.Add(from + " " + to);
            return from == "EUR" ? value * 1.16 : value * 0.92;
        }

        double perUnit = SymbolCostMath.UsdPerMillionPerUnit(2.5, "EUR", "GBP", "CHF", 0.86, Convert);

        Assert.Equal(2.5 / 1000000.0 * 1.16 * 0.92, perUnit, Tolerance);
        Assert.Equal(new[] { "EUR USD", "USD CHF" }, calls);
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void UsdPerMillionPerUnit_EurUsdWithInvalidPrice_IsRejectedByTheCore()
    {
        double perUnit = SymbolCostMath.UsdPerMillionPerUnit(2.5, "EUR", "USD", "USD", 0.0, NoConverter);

        Assert.True(double.IsNaN(perUnit));
        Assert.NotNull(RiskMath.InvalidSymbolData(Symbol("EUR", "USD", 0.0001, 0.0001, 2.0 * perUnit)));
    }

    [Fact]
    [Trait("Req", "EXE-004")]
    public void UsdPerMillionPerUnit_ZeroCommission_IsZeroAndAccepted()
    {
        double perUnit = SymbolCostMath.UsdPerMillionPerUnit(0.0, "EUR", "USD", "USD", 1.16, NoConverter);

        Assert.Equal(0.0, perUnit);
        Assert.Null(RiskMath.InvalidSymbolData(Symbol("EUR", "USD", 0.0001, 0.0001, 2.0 * perUnit)));
    }

    [Fact]
    [Trait("Req", "RSK-001")]
    public void PipValueAndCommission_EurUsdUsdAccount_AreAcceptedByTheCore()
    {
        double pip = SymbolCostMath.PipValuePerUnit(0.0001, "EUR", "USD", "USD", 1.16, NoConverter);
        double commission = 2.0 * SymbolCostMath.UsdPerMillionPerUnit(2.5, "EUR", "USD", "USD", 1.16, NoConverter);

        Assert.Null(RiskMath.InvalidSymbolData(Symbol("EUR", "USD", 0.0001, pip, commission)));
    }
}
