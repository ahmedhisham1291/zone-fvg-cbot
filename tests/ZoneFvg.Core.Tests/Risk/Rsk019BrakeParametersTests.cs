using Xunit;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-019: the two drawdown brake parameters, their core defaults (0 = off and 0.5) and their
/// validation, which names the parameter in the existing message style.
/// </summary>
public class Rsk019BrakeParametersTests
{
    private static RiskParameters Valid => new() { RiskEpochDate = RiskData.Epoch };

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Defaults_BrakeOffFactorHalf_RiskPercentStaysHalfAPercent()
    {
        var parameters = new RiskParameters();

        Assert.Equal(0.0, parameters.DrawdownBrakePercent);
        Assert.Equal(0.5, parameters.DrawdownBrakeFactor);
        Assert.Equal(0.5, parameters.RiskPercent);
        Assert.Equal(8.0, parameters.MaxDrawdownPercent);
    }

    [Theory]
    [Trait("Req", "RSK-019")]
    [InlineData(-0.01, "-0.01")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(8.0, "8")]
    [InlineData(9.0, "9")]
    public void Validate_BrakePercentOutOfRange_RefusedNamingIt(double value, string text)
    {
        RiskParameters parameters = Valid with { DrawdownBrakePercent = value };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("DrawdownBrakePercent", exception.ParamName);
        Assert.StartsWith("DrawdownBrakePercent must be at least 0 and below MaxDrawdownPercent but was " + text + ".", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "RSK-019")]
    [InlineData(0.0)]
    [InlineData(3.0)]
    [InlineData(7.99)]
    public void Validate_BrakePercentInRange_Accepted(double value)
    {
        Exception? exception = Record.Exception(() => (Valid with { DrawdownBrakePercent = value }).Validate());

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Validate_BrakePercentAgainstALowerMaxDrawdown_RefusedAtThatLimit()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => (Valid with { MaxDrawdownPercent = 3.0, DrawdownBrakePercent = 3.0 }).Validate());

        Assert.Equal("DrawdownBrakePercent", exception.ParamName);
        Assert.Null(Record.Exception(() => (Valid with { MaxDrawdownPercent = 3.0, DrawdownBrakePercent = 2.99 }).Validate()));
    }

    [Theory]
    [Trait("Req", "RSK-019")]
    [InlineData(0.0, "0")]
    [InlineData(-0.5, "-0.5")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(1.01, "1.01")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    public void Validate_BrakeFactorOutOfRange_RefusedNamingIt(double value, string text)
    {
        RiskParameters parameters = Valid with { DrawdownBrakePercent = 3.0, DrawdownBrakeFactor = value };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("DrawdownBrakeFactor", exception.ParamName);
        Assert.StartsWith("DrawdownBrakeFactor must be greater than 0 and at most 1 but was " + text + ".", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Validate_BrakeFactorOutOfRangeWithTheBrakeOff_StillRefused()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => (Valid with { DrawdownBrakeFactor = 0.0 }).Validate());

        Assert.Equal("DrawdownBrakeFactor", exception.ParamName);
    }

    [Theory]
    [Trait("Req", "RSK-019")]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Validate_BrakeFactorInRange_Accepted(double value)
    {
        Exception? exception = Record.Exception(() => (Valid with { DrawdownBrakePercent = 3.0, DrawdownBrakeFactor = value }).Validate());

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Validate_AnEarlierValueAndABadBrake_TheEarlierRefusalComesFirst()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => (Valid with { RiskPercent = 0.0, DrawdownBrakePercent = -1.0 }).Validate());

        Assert.Equal("RiskPercent", exception.ParamName);
    }

    [Theory]
    [Trait("Req", "RSK-019")]
    [InlineData(210000.0, 203700.0, 3.0, true)]
    [InlineData(210000.0, 203700.01, 3.0, false)]
    [InlineData(210000.0, 203699.99, 3.0, true)]
    [InlineData(210000.0, 205000.0, 3.0, false)]
    [InlineData(210000.0, 203700.0, 0.0, false)]
    [InlineData(0.0, -10.0, 3.0, false)]
    [InlineData(-5.0, -10.0, 3.0, false)]
    [InlineData(double.NaN, 100.0, 3.0, false)]
    public void IsBraked_PeakBalanceAndPercent_EqualCountsAsBraked(double peak, double balance, double percent, bool braked)
    {
        Assert.Equal(braked, DrawdownBrake.IsBraked(peak, balance, percent));
    }

    [Fact]
    [Trait("Req", "RSK-019")]
    public void Note_Texts_RoundedThenInvariantR()
    {
        var note = new DrawdownBrakeNote(210000.004, 203699.987, 0.3750000004);

        Assert.Equal("210000", note.PeakText);
        Assert.Equal("203699.99", note.BalanceText);
        Assert.Equal("0.375", note.RiskPercentText);
    }
}
