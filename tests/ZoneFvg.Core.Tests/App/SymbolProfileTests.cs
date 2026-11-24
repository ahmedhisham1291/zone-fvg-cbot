using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-030 and EXE-031 (spec 7.1): the automatic symbol profile and the check of values set by
/// hand. Every expected value and message is written by hand from spec 7.1 and the WP39 prompt.
/// </summary>
public class SymbolProfileTests
{
    private const double Tolerance = 1e-9;

    private static SymbolProfileResult Gold(double pip, double spread = 0.0, double maxSpread = 0.0, double minStop = 0.0, double chochMinStop = 0.0, double maxVolume = 0.0)
    {
        return SymbolProfile.Resolve("XAUUSD", "XAU", "USD", pip, spread, maxSpread, minStop, chochMinStop, maxVolume);
    }

    private static SymbolProfileResult EurUsd(double pip = 0.0001, double spread = 0.0, double maxSpread = 0.0, double minStop = 0.0, double chochMinStop = 0.0, double maxVolume = 0.0)
    {
        return SymbolProfile.Resolve("EURUSD", "EUR", "USD", pip, spread, maxSpread, minStop, chochMinStop, maxVolume);
    }

    private static void Refused(Func<SymbolProfileResult> resolve, string parameter, string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => resolve());
        Assert.Equal(parameter, exception.ParamName);
        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
    }

    private static void Accepted(Func<SymbolProfileResult> resolve)
    {
        Assert.Null(Record.Exception(() => resolve()));
    }

    // ------------------------------------------------------------------ EXE-030: profile and automatic values

    [Theory]
    [Trait("Req", "EXE-030")]
    [InlineData(1.0, 0.4, 8.0, 3.0)]
    [InlineData(0.01, 40.0, 800.0, 300.0)]
    [InlineData(0.1, 4.0, 80.0, 30.0)]
    public void Resolve_GoldAllAutomatic_UsesThePriceValuesConvertedWithThePipSize(double pip, double spread, double stop, double chochStop)
    {
        SymbolProfileResult result = Gold(pip);

        Assert.Equal(SymbolProfileKind.Gold, result.Profile);
        Assert.Equal(pip, result.PipSize);
        Assert.Equal(spread, result.MaxSpreadPips);
        Assert.Equal(stop, result.MinStopPips);
        Assert.Equal(chochStop, result.ChochMinStopPips);
        Assert.Equal(1000.0, result.MaxVolumeUnits);
        Assert.True(result.MaxSpreadPipsAuto && result.MinStopPipsAuto && result.ChochMinStopPipsAuto && result.MaxVolumeUnitsAuto);
        Assert.Null(result.SpreadNote);
    }

    [Theory]
    [Trait("Req", "EXE-030")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void Resolve_GoldAtPip1AndPip001_TheSameLimitsInPrice(double pip)
    {
        SymbolProfileResult result = Gold(pip);

        Assert.Equal(0.40, result.MaxSpreadPips * pip, Tolerance);
        Assert.Equal(8.00, result.MinStopPips * pip, Tolerance);
        Assert.Equal(3.00, result.ChochMinStopPips * pip, Tolerance);
    }

    [Fact]
    [Trait("Req", "EXE-030")]
    public void Resolve_GoldInLowerCase_IsGold()
    {
        Assert.Equal(SymbolProfileKind.Gold, SymbolProfile.Resolve("xauusd", "xau", "usd", 0.01, 0.0, 0.0, 0.0, 0.0, 0.0).Profile);
    }

    [Theory]
    [Trait("Req", "EXE-030")]
    [InlineData("XAUEUR", "XAU", "EUR")]
    [InlineData("XAGUSD", "XAG", "USD")]
    [InlineData("BTCUSD", "BTC", "USD")]
    [InlineData("US30", "US30", "USD")]
    public void Resolve_NoProfile_AllValuesSet_UsedAsGivenWithoutRangeCheck(string symbol, string baseAsset, string quote)
    {
        SymbolProfileResult result = SymbolProfile.Resolve(symbol, baseAsset, quote, 0.01, 0.0, 2.0, 10.0, 4.0, 500.0);

        Assert.Equal(SymbolProfileKind.None, result.Profile);
        Assert.Equal(2.0, result.MaxSpreadPips);
        Assert.Equal(10.0, result.MinStopPips);
        Assert.Equal(4.0, result.ChochMinStopPips);
        Assert.Equal(500.0, result.MaxVolumeUnits);
        Assert.False(result.MaxSpreadPipsAuto || result.MinStopPipsAuto || result.ChochMinStopPipsAuto || result.MaxVolumeUnitsAuto);
    }

    [Fact]
    [Trait("Req", "EXE-030")]
    public void Resolve_NoProfileWithAnAutomaticValue_RefusedNamingIt()
    {
        Refused(
            () => SymbolProfile.Resolve("XAGUSD", "XAG", "USD", 0.001, 0.0, 0.0, 10.0, 4.0, 500.0),
            "MaxSpreadPips",
            "MaxSpreadPips is 0 (automatic), but XAGUSD (base XAG, quote USD) has no automatic profile; set MaxSpreadPips by hand.");
        Refused(
            () => SymbolProfile.Resolve("US30", "US30", "USD", 1.0, 0.0, 2.0, 10.0, 4.0, 0.0),
            "MaxVolumeUnits",
            "MaxVolumeUnits is 0 (automatic), but US30 (base US30, quote USD) has no automatic profile; set MaxVolumeUnits by hand.");
    }

    [Theory]
    [Trait("Req", "EXE-030")]
    [InlineData("EURUSD", "EUR", "USD", 0.0001)]
    [InlineData("USDJPY", "USD", "JPY", 0.01)]
    [InlineData("gbpusd", "gbp", "usd", 0.0001)]
    public void Resolve_CurrencyPairAllAutomatic_TheForexValues(string symbol, string baseAsset, string quote, double pip)
    {
        SymbolProfileResult result = SymbolProfile.Resolve(symbol, baseAsset, quote, pip, 0.0, 0.0, 0.0, 0.0, 0.0);

        Assert.Equal(SymbolProfileKind.Fx, result.Profile);
        Assert.Equal(1.5, result.MaxSpreadPips);
        Assert.Equal(12.0, result.MinStopPips);
        Assert.Equal(5.0, result.ChochMinStopPips);
        Assert.Equal(3000000.0, result.MaxVolumeUnits);
    }

    [Fact]
    [Trait("Req", "EXE-030")]
    public void Resolve_CurrencyPairWithANonStandardPipSize_Refused()
    {
        Refused(
            () => SymbolProfile.Resolve("USDJPY", "USD", "JPY", 0.0001, 0.0, 0.0, 0.0, 0.0, 0.0),
            "PipSize",
            "USDJPY reports a pip size of 0.0001; the currency-pair values are in pips of 0.01; the bot does not start.");
        Refused(
            () => EurUsd(pip: 0.00001),
            "PipSize",
            "EURUSD reports a pip size of 1E-05; the currency-pair values are in pips of 0.0001; the bot does not start.");
        Refused(() => EurUsd(pip: 0.0001 * (1.0 + 1e-8)), "PipSize", "EURUSD reports a pip size of ");
    }

    [Fact]
    [Trait("Req", "EXE-030")]
    public void Resolve_CurrencyPairPipWithinTheTolerance_Accepted()
    {
        Assert.Equal(SymbolProfileKind.Fx, EurUsd(pip: 0.0001 * (1.0 + 1e-10)).Profile);
    }

    [Theory]
    [Trait("Req", "EXE-030")]
    [InlineData(0.0)]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Resolve_PipSizeNotUsable_RefusedForEveryProfile(double pip)
    {
        string text = pip.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        Refused(() => Gold(pip), "PipSize", "XAUUSD reports a pip size of " + text + ", which is not a finite number greater than 0; the bot does not start.");
        Refused(() => EurUsd(pip: pip), "PipSize", "EURUSD reports a pip size of " + text + ", which is not a finite number greater than 0; the bot does not start.");
        Refused(
            () => SymbolProfile.Resolve("XAGUSD", "XAG", "USD", pip, 0.0, 2.0, 10.0, 4.0, 500.0),
            "PipSize",
            "XAGUSD reports a pip size of " + text + ", which is not a finite number greater than 0; the bot does not start.");
    }
}
