using System.Reflection;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-035: the Choch parameters, their defaults and the ranges <see cref="ChochParameters.Validate"/>
/// checks, each at its boundary, with the message that names the parameter as cTrader shows it,
/// its rule and the rejected value.
/// </summary>
public class ChochParametersTests
{
    private static void AssertRefused(ChochParameters parameters, string name, string text)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal(name, exception.ParamName);
        Assert.Equal(text + " (Parameter '" + name + "')", exception.Message);
    }

    private static void AssertAccepted(ChochParameters parameters)
    {
        Assert.Null(Record.Exception(() => parameters.Validate()));
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    public void Defaults_MatchSpecSection39()
    {
        var parameters = new ChochParameters();

        Assert.False(parameters.RequireSweep);
        Assert.Equal(32, parameters.ChochWindowBars);
        Assert.False(parameters.ChochUseBias);
        Assert.False(parameters.ChochStopAtDistal);
        Assert.Equal(0.1, parameters.ChochTargetBufferAtr);
        Assert.Equal(5.0, parameters.ChochMinStopPips);
        Assert.Equal(2.0, parameters.MinRewardRisk);
        AssertAccepted(parameters);
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    public void Properties_AreTheSevenChochParametersWithTheirCbotNames()
    {
        string[] actual = typeof(ChochParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "ChochMinStopPips", "ChochStopAtDistal", "ChochTargetBufferAtr", "ChochUseBias", "ChochWindowBars", "MinRewardRisk", "RequireSweep" }, actual);
    }

    [Theory]
    [Trait("Req", "SIG-035")]
    [InlineData(3)]
    [InlineData(97)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WindowOutsideFourToNinetySix_Refused(int bars)
    {
        AssertRefused(new ChochParameters { ChochWindowBars = bars }, "ChochWindowBars", "ChochWindowBars must be a whole number from 4 to 96 but was " + Invariant.Text(bars) + ".");
    }

    [Theory]
    [Trait("Req", "SIG-035")]
    [InlineData(4)]
    [InlineData(96)]
    public void Validate_WindowAtItsLimits_Accepted(int bars)
    {
        AssertAccepted(new ChochParameters { ChochWindowBars = bars });
    }

    [Theory]
    [Trait("Req", "SIG-035")]
    [InlineData(-0.0001)]
    [InlineData(1.0001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validate_TargetBufferOutsideZeroToOne_Refused(double value)
    {
        AssertRefused(new ChochParameters { ChochTargetBufferAtr = value }, "ChochTargetBufferAtr", "ChochTargetBufferAtr must be a finite number from 0 to 1 but was " + Invariant.Text(value) + ".");
    }

    [Theory]
    [Trait("Req", "SIG-035")]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void Validate_TargetBufferAtItsLimits_Accepted(double value)
    {
        AssertAccepted(new ChochParameters { ChochTargetBufferAtr = value });
    }

    [Theory]
    [Trait("Req", "SIG-035")]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validate_MinStopPipsNotPositive_Refused(double value)
    {
        AssertRefused(new ChochParameters { ChochMinStopPips = value }, "ChochMinStopPips", "ChochMinStopPips must be a finite number greater than zero but was " + Invariant.Text(value) + ".");
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    public void Validate_MinStopPipsTheSmallestPositiveValue_Accepted()
    {
        AssertAccepted(new ChochParameters { ChochMinStopPips = double.Epsilon });
        AssertAccepted(new ChochParameters { ChochMinStopPips = 3.0 });
    }

    [Theory]
    [Trait("Req", "SIG-035")]
    [InlineData(0.9999)]
    [InlineData(10.0001)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void Validate_MinRewardRiskOutsideOneToTen_Refused(double value)
    {
        AssertRefused(new ChochParameters { MinRewardRisk = value }, "MinRewardRisk", "MinRewardRisk must be a finite number from 1 to 10 but was " + Invariant.Text(value) + ".");
    }

    [Theory]
    [Trait("Req", "SIG-035")]
    [InlineData(1.0)]
    [InlineData(10.0)]
    public void Validate_MinRewardRiskAtItsLimits_Accepted(double value)
    {
        AssertAccepted(new ChochParameters { MinRewardRisk = value });
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    public void Validate_TheBooleans_AcceptBothValues()
    {
        AssertAccepted(new ChochParameters { RequireSweep = true, ChochUseBias = true, ChochStopAtDistal = true });
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    public void Validate_SeveralValuesOutOfRange_TheFirstInDeclarationOrderIsNamed()
    {
        var parameters = new ChochParameters { ChochWindowBars = 3, ChochTargetBufferAtr = 2.0, ChochMinStopPips = 0.0, MinRewardRisk = 0.5 };

        Assert.Equal("ChochWindowBars", Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate()).ParamName);
        Assert.Equal("ChochTargetBufferAtr", Assert.Throws<ArgumentOutOfRangeException>(() => (parameters with { ChochWindowBars = 4 }).Validate()).ParamName);
        Assert.Equal("ChochMinStopPips", Assert.Throws<ArgumentOutOfRangeException>(() => (parameters with { ChochWindowBars = 4, ChochTargetBufferAtr = 0.1 }).Validate()).ParamName);
    }
}
