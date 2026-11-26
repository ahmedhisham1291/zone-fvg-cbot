using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-031 (spec 7.1): a value set by hand is used as given from one tenth to ten times the
/// profile's value, compared in price for gold and in pips for a currency pair, both ends accepted;
/// outside it the start is refused with a message written by hand here. Also the spread note.
/// </summary>
public class SymbolProfileHandSetTests
{
    private static SymbolProfileResult Gold(double pip, double maxSpread = 0.0, double minStop = 0.0, double chochMinStop = 0.0, double maxVolume = 0.0, double spread = 0.0)
    {
        return SymbolProfile.Resolve("XAUUSD", "XAU", "USD", pip, spread, maxSpread, minStop, chochMinStop, maxVolume);
    }

    private static SymbolProfileResult EurUsd(double maxSpread = 0.0, double minStop = 0.0, double chochMinStop = 0.0, double maxVolume = 0.0, double spread = 0.0)
    {
        return SymbolProfile.Resolve("EURUSD", "EUR", "USD", 0.0001, spread, maxSpread, minStop, chochMinStop, maxVolume);
    }

    private static void Refused(Func<SymbolProfileResult> resolve, string parameter, string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => resolve());
        Assert.Equal(parameter, exception.ParamName);
        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ gold at pip 0.01

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_GoldPip001_TheOldGoldSpread04_Refused()
    {
        Refused(
            () => Gold(0.01, maxSpread: 0.4),
            "MaxSpreadPips",
            "MaxSpreadPips 0.4 is 0.004 USD of price for XAUUSD at its pip size 0.01; accepted for gold: 0.04 to 4 USD; set 0 for the automatic value 40 (0.4 USD).");
    }

    [Theory]
    [Trait("Req", "EXE-031")]
    [InlineData(4.0)]
    [InlineData(40.0)]
    [InlineData(400.0)]
    public void Resolve_GoldPip001_SpreadInsideTheRange_UsedAsGiven(double value)
    {
        SymbolProfileResult result = Gold(0.01, maxSpread: value);

        Assert.Equal(value, result.MaxSpreadPips);
        Assert.False(result.MaxSpreadPipsAuto);
        Assert.True(result.MinStopPipsAuto);
    }

    [Theory]
    [Trait("Req", "EXE-031")]
    [InlineData(3.99)]
    [InlineData(400.01)]
    [InlineData(1.5)]
    public void Resolve_GoldPip001_SpreadOutsideTheRange_Refused(double value)
    {
        Refused(() => Gold(0.01, maxSpread: value), "MaxSpreadPips", "MaxSpreadPips " + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " is ");
    }

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_GoldPip001_TheOldChochStop3_Refused_30Accepted()
    {
        Refused(
            () => Gold(0.01, chochMinStop: 3.0),
            "ChochMinStopPips",
            "ChochMinStopPips 3 is 0.03 USD of price for XAUUSD at its pip size 0.01; accepted for gold: 0.3 to 30 USD; set 0 for the automatic value 300 (3 USD).");
        Refused(() => Gold(0.01, chochMinStop: 29.99), "ChochMinStopPips", "ChochMinStopPips 29.99 is ");
        Assert.Equal(30.0, Gold(0.01, chochMinStop: 30.0).ChochMinStopPips);
    }

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_GoldPip001_MinStop8_Refused_80Accepted()
    {
        Refused(
            () => Gold(0.01, minStop: 8.0),
            "MinStopPips",
            "MinStopPips 8 is 0.08 USD of price for XAUUSD at its pip size 0.01; accepted for gold: 0.8 to 80 USD; set 0 for the automatic value 800 (8 USD).");
        Assert.Equal(80.0, Gold(0.01, minStop: 80.0).MinStopPips);
    }

    // ------------------------------------------------------------------ gold at pip 1

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_GoldPip1_TheOldGoldFileValues_Accepted()
    {
        SymbolProfileResult result = Gold(1.0, maxSpread: 0.4, minStop: 8.0, chochMinStop: 3.0, maxVolume: 1000.0);

        Assert.Equal(0.4, result.MaxSpreadPips);
        Assert.Equal(8.0, result.MinStopPips);
        Assert.Equal(3.0, result.ChochMinStopPips);
        Assert.Equal(1000.0, result.MaxVolumeUnits);
        Assert.False(result.MaxSpreadPipsAuto || result.MinStopPipsAuto || result.ChochMinStopPipsAuto || result.MaxVolumeUnitsAuto);
    }

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_GoldPip1_StopBoundaries()
    {
        Refused(() => Gold(1.0, minStop: 0.79), "MinStopPips", "MinStopPips 0.79 is 0.79 USD of price for XAUUSD at its pip size 1; accepted for gold: 0.8 to 80 USD;");
        Assert.Equal(0.8, Gold(1.0, minStop: 0.8).MinStopPips);
        Refused(() => Gold(1.0, chochMinStop: 0.29), "ChochMinStopPips", "ChochMinStopPips 0.29 is ");
        Assert.Equal(0.3, Gold(1.0, chochMinStop: 0.3).ChochMinStopPips);
        Assert.Equal(30.0, Gold(1.0, chochMinStop: 30.0).ChochMinStopPips);
        Refused(() => Gold(1.0, chochMinStop: 30.01), "ChochMinStopPips", "ChochMinStopPips 30.01 is ");
    }

    [Theory]
    [Trait("Req", "EXE-031")]
    [InlineData(200.0)]
    [InlineData(100.0)]
    [InlineData(10000.0)]
    public void Resolve_GoldVolumeInsideTheRange_UsedAsGiven(double value)
    {
        Assert.Equal(value, Gold(0.01, maxVolume: value).MaxVolumeUnits);
    }

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_GoldVolumeOutsideTheRange_Refused()
    {
        Refused(
            () => Gold(0.01, maxVolume: 3000000.0),
            "MaxVolumeUnits",
            "MaxVolumeUnits 3000000 is outside the range for gold (XAUUSD): 100 to 10000 units; set 0 for the automatic value 1000.");
        Refused(() => Gold(1.0, maxVolume: 99.0), "MaxVolumeUnits", "MaxVolumeUnits 99 is outside the range for gold (XAUUSD): 100 to 10000 units;");
        Refused(() => Gold(1.0, maxVolume: 10001.0), "MaxVolumeUnits", "MaxVolumeUnits 10001 is outside the range for gold (XAUUSD): 100 to 10000 units;");
    }

    // ------------------------------------------------------------------ currency pair

    [Theory]
    [Trait("Req", "EXE-031")]
    [InlineData(1.5)]
    [InlineData(0.15)]
    [InlineData(15.0)]
    public void Resolve_ForexSpreadInsideTheRange_UsedAsGiven(double value)
    {
        Assert.Equal(value, EurUsd(maxSpread: value).MaxSpreadPips);
    }

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_ForexOutsideTheRange_RefusedWithTheForexMessage()
    {
        Refused(
            () => EurUsd(maxSpread: 0.14),
            "MaxSpreadPips",
            "MaxSpreadPips 0.14 is outside the range for the currency pair EURUSD: 0.15 to 15 pips; set 0 for the automatic value 1.5.");
        Refused(() => EurUsd(maxSpread: 15.01), "MaxSpreadPips", "MaxSpreadPips 15.01 is outside the range for the currency pair EURUSD: 0.15 to 15 pips;");
        Refused(
            () => EurUsd(minStop: 1.19),
            "MinStopPips",
            "MinStopPips 1.19 is outside the range for the currency pair EURUSD: 1.2 to 120 pips; set 0 for the automatic value 12.");
        Refused(() => EurUsd(chochMinStop: 0.49), "ChochMinStopPips", "ChochMinStopPips 0.49 is outside the range for the currency pair EURUSD: 0.5 to 50 pips; set 0 for the automatic value 5.");
        Refused(
            () => EurUsd(maxVolume: 299999.0),
            "MaxVolumeUnits",
            "MaxVolumeUnits 299999 is outside the range for the currency pair EURUSD: 300000 to 30000000 units; set 0 for the automatic value 3000000.");
    }

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_ForexValuesInsideTheRange_UsedAsGiven()
    {
        SymbolProfileResult result = EurUsd(minStop: 8.0, chochMinStop: 0.5, maxVolume: 500000.0);

        Assert.Equal(8.0, result.MinStopPips);
        Assert.Equal(0.5, result.ChochMinStopPips);
        Assert.Equal(500000.0, result.MaxVolumeUnits);
        Assert.Equal(30000000.0, EurUsd(maxVolume: 30000000.0).MaxVolumeUnits);
    }

    // ------------------------------------------------------------------ not a number, negative, order

    public static IEnumerable<object[]> BadValues()
    {
        foreach (string name in new[] { "MaxSpreadPips", "MinStopPips", "ChochMinStopPips", "MaxVolumeUnits" })
        {
            yield return new object[] { name, -1.0, "-1" };
            yield return new object[] { name, double.NaN, "NaN" };
            yield return new object[] { name, double.PositiveInfinity, "Infinity" };
        }
    }

    [Theory]
    [Trait("Req", "EXE-031")]
    [MemberData(nameof(BadValues))]
    public void Resolve_NegativeOrNotFinite_Refused(string name, double value, string text)
    {
        double[] values = { 0.0, 0.0, 0.0, 0.0 };
        values[Array.IndexOf(new[] { "MaxSpreadPips", "MinStopPips", "ChochMinStopPips", "MaxVolumeUnits" }, name)] = value;

        Refused(
            () => SymbolProfile.Resolve("XAUUSD", "XAU", "USD", 0.01, 0.0, values[0], values[1], values[2], values[3]),
            name,
            name + " must be 0 (automatic) or a finite number greater than 0; got " + text + ".");
    }

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_TwoValuesOutOfRange_TheMessageNamesMaxSpreadPips()
    {
        Refused(() => EurUsd(maxSpread: 0.1, minStop: 0.1), "MaxSpreadPips", "MaxSpreadPips 0.1 is outside");
    }

    // ------------------------------------------------------------------ spread note

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_SpreadAboveTheLimit_TheNote()
    {
        Assert.Equal(
            "spread at start 50 pips (0.5 in price) is above MaxSpreadPips 40; entries are skipped while it lasts.",
            Gold(0.01, spread: 0.5).SpreadNote);
        Assert.NotNull(EurUsd(spread: 0.0002).SpreadNote);
    }

    [Theory]
    [Trait("Req", "EXE-031")]
    [InlineData(0.40)]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(-0.1)]
    public void Resolve_SpreadNotAboveTheLimitOrNotUsable_NoNote(double spread)
    {
        Assert.Null(Gold(0.01, spread: spread).SpreadNote);
    }

    [Fact]
    [Trait("Req", "EXE-031")]
    public void Resolve_ForexSpreadAtTheLimit_NoNote()
    {
        Assert.Null(EurUsd(spread: 0.00015).SpreadNote);
    }
}
