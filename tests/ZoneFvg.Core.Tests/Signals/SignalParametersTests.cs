using System.Globalization;
using System.Reflection;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

public class SignalParametersTests
{
    [Fact]
    public void Defaults_MatchSpecSection7()
    {
        var parameters = new SignalParameters();

        Assert.Equal(TimeFrame.D1, parameters.BiasTimeFrame);
        Assert.Equal(TimeFrame.H4, parameters.ZoneTimeFrame);
        Assert.Equal(50, parameters.BiasEmaPeriod);
        Assert.Equal(14, parameters.AtrPeriod);
        Assert.Equal(3, parameters.SwingStrength);
        Assert.Equal(2.0, parameters.ImpulseAtrMult);
        Assert.Equal(3, parameters.MaxImpulseBars);
        Assert.Equal(3, parameters.MaxBaseBars);
        Assert.True(parameters.RequireBos);
        Assert.Equal(0.2, parameters.MinZoneAtr);
        Assert.Equal(1.5, parameters.MaxZoneAtr);
        Assert.Equal(120, parameters.MaxZoneAgeBars);
        Assert.Equal(16, parameters.ConfirmWindowBars);
        Assert.Equal(0.25, parameters.MinFvgAtr);
        Assert.Equal(50.0, parameters.EntryLevelPct);
        Assert.Equal(0.1, parameters.StopBufferAtr);
        Assert.Equal(2.0, parameters.RewardRisk);
    }

    [Fact]
    public void Properties_AreTheSignalLayerParametersOfSpecSection7()
    {
        string[] expected =
        {
            "BiasTimeFrame", "ZoneTimeFrame", "BiasEmaPeriod", "AtrPeriod", "SwingStrength", "ImpulseAtrMult",
            "MaxImpulseBars", "MaxBaseBars", "RequireBos", "MinZoneAtr", "MaxZoneAtr", "MaxZoneAgeBars",
            "ConfirmWindowBars", "MinFvgAtr", "EntryLevelPct", "StopBufferAtr", "RewardRisk",
        };

        string[] actual = typeof(SignalParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal).ToArray(), actual);
    }

    [Fact]
    public void Validate_Defaults_AreAccepted()
    {
        Exception? exception = Record.Exception(() => new SignalParameters().Validate());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("BiasEmaPeriod", 0.0)]
    [InlineData("BiasEmaPeriod", -1.0)]
    [InlineData("BiasEmaPeriod", 10001.0)]
    [InlineData("AtrPeriod", 0.0)]
    [InlineData("AtrPeriod", -1.0)]
    [InlineData("AtrPeriod", 10001.0)]
    [InlineData("SwingStrength", 0.0)]
    [InlineData("SwingStrength", -1.0)]
    [InlineData("SwingStrength", 10001.0)]
    [InlineData("MaxImpulseBars", 0.0)]
    [InlineData("MaxImpulseBars", -1.0)]
    [InlineData("MaxImpulseBars", 10001.0)]
    [InlineData("MaxBaseBars", 0.0)]
    [InlineData("MaxBaseBars", -1.0)]
    [InlineData("MaxBaseBars", 10001.0)]
    [InlineData("MaxZoneAgeBars", 0.0)]
    [InlineData("MaxZoneAgeBars", -1.0)]
    [InlineData("MaxZoneAgeBars", 10001.0)]
    [InlineData("ConfirmWindowBars", 0.0)]
    [InlineData("ConfirmWindowBars", -1.0)]
    [InlineData("ConfirmWindowBars", 10001.0)]
    [InlineData("ImpulseAtrMult", 0.0)]
    [InlineData("ImpulseAtrMult", -0.5)]
    [InlineData("ImpulseAtrMult", double.NaN)]
    [InlineData("ImpulseAtrMult", double.PositiveInfinity)]
    [InlineData("MinZoneAtr", 0.0)]
    [InlineData("MinZoneAtr", -0.1)]
    [InlineData("MinZoneAtr", double.NaN)]
    [InlineData("MinZoneAtr", double.PositiveInfinity)]
    [InlineData("MaxZoneAtr", 0.0)]
    [InlineData("MaxZoneAtr", -1.5)]
    [InlineData("MaxZoneAtr", 0.19)] // below MinZoneAtr (0.2)
    [InlineData("MaxZoneAtr", double.NaN)]
    [InlineData("MaxZoneAtr", double.PositiveInfinity)]
    [InlineData("MinFvgAtr", 0.0)]
    [InlineData("MinFvgAtr", -0.25)]
    [InlineData("MinFvgAtr", double.NaN)]
    [InlineData("MinFvgAtr", double.PositiveInfinity)]
    [InlineData("EntryLevelPct", -0.01)]
    [InlineData("EntryLevelPct", 100.01)]
    [InlineData("EntryLevelPct", double.NaN)]
    [InlineData("EntryLevelPct", double.PositiveInfinity)]
    [InlineData("StopBufferAtr", -0.01)]
    [InlineData("StopBufferAtr", double.NaN)]
    [InlineData("StopBufferAtr", double.PositiveInfinity)]
    [InlineData("RewardRisk", 0.0)]
    [InlineData("RewardRisk", -2.0)]
    [InlineData("RewardRisk", double.NaN)]
    [InlineData("RewardRisk", double.PositiveInfinity)]
    public void Validate_ValueOutOfRange_ThrowsAndNamesTheParameter(string parameter, double value)
    {
        SignalParameters parameters = ParameterSets.With(parameter, value);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal(parameter, exception.ParamName);
        Assert.Contains(parameter, exception.Message);
    }

    [Theory]
    [InlineData("BiasEmaPeriod", 1.0)]
    [InlineData("BiasEmaPeriod", 10000.0)]
    [InlineData("AtrPeriod", 1.0)]
    [InlineData("AtrPeriod", 10000.0)]
    [InlineData("SwingStrength", 1.0)]
    [InlineData("SwingStrength", 10000.0)]
    [InlineData("MaxImpulseBars", 1.0)]
    [InlineData("MaxImpulseBars", 10000.0)]
    [InlineData("MaxBaseBars", 1.0)]
    [InlineData("MaxBaseBars", 10000.0)]
    [InlineData("MaxZoneAgeBars", 1.0)]
    [InlineData("MaxZoneAgeBars", 10000.0)]
    [InlineData("ConfirmWindowBars", 1.0)]
    [InlineData("ConfirmWindowBars", 10000.0)]
    [InlineData("ImpulseAtrMult", 0.001)]
    [InlineData("MinZoneAtr", 0.001)]
    [InlineData("MinZoneAtr", 1.5)] // equal to MaxZoneAtr
    [InlineData("MaxZoneAtr", 0.2)] // equal to MinZoneAtr
    [InlineData("MinFvgAtr", 0.001)]
    [InlineData("EntryLevelPct", 0.0)]
    [InlineData("EntryLevelPct", 100.0)]
    [InlineData("StopBufferAtr", 0.0)]
    [InlineData("RewardRisk", 0.001)]
    public void Validate_ValueAtTheEdgeOfItsRange_IsAccepted(string parameter, double value)
    {
        SignalParameters parameters = ParameterSets.With(parameter, value);

        Exception? exception = Record.Exception(() => parameters.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_MinZoneAtrAboveMaxZoneAtr_ThrowsAndNamesBoth()
    {
        var parameters = new SignalParameters { MinZoneAtr = 1.6 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("MaxZoneAtr", exception.ParamName);
        Assert.Contains("MinZoneAtr", exception.Message);
        Assert.Contains("MaxZoneAtr", exception.Message);
    }

    [Theory]
    [InlineData(TimeFrame.M15)]
    [InlineData(TimeFrame.H4)]
    public void Validate_BiasTimeFrameOtherThanD1_Throws(TimeFrame timeFrame)
    {
        var parameters = new SignalParameters { BiasTimeFrame = timeFrame };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("BiasTimeFrame", exception.ParamName);
        Assert.Contains("BiasTimeFrame", exception.Message);
    }

    [Theory]
    [InlineData(TimeFrame.M15)]
    [InlineData(TimeFrame.D1)]
    public void Validate_ZoneTimeFrameOtherThanH4_Throws(TimeFrame timeFrame)
    {
        var parameters = new SignalParameters { ZoneTimeFrame = timeFrame };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("ZoneTimeFrame", exception.ParamName);
        Assert.Contains("ZoneTimeFrame", exception.Message);
    }

    [Fact]
    public void Validate_SeveralInvalidValues_ReportsTheFirstInDeclarationOrder()
    {
        var parameters = new SignalParameters { RewardRisk = -1.0, AtrPeriod = 0, MinFvgAtr = 0.0 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("AtrPeriod", exception.ParamName);
    }

    [Fact]
    public void Validate_Message_StatesTheAllowedRangeAndTheRejectedValue()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => ParameterSets.With("BiasEmaPeriod", 10001.0).Validate());

        Assert.Contains("between 1 and 10000", exception.Message);
        Assert.Contains("10001", exception.Message);
    }

    [Fact]
    public void Validate_MessageUnderACultureWithDecimalComma_StillUsesInvariantFormatting()
    {
        // A culture that writes 0.19 as "0,19" must not change the text (AGENTS.md rule 7).
        CultureInfo original = CultureInfo.CurrentCulture;
        var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaCulture.NumberFormat.NumberDecimalSeparator = ",";
        try
        {
            CultureInfo.CurrentCulture = commaCulture;

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => ParameterSets.With("MaxZoneAtr", 0.19).Validate());

            Assert.Contains("0.19", exception.Message);
            Assert.DoesNotContain("0,19", exception.Message);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void MaxBarCount_IsTenThousand()
    {
        int maxBarCount = SignalParameters.MaxBarCount;

        Assert.Equal(10000, maxBarCount);
    }

    [Fact]
    public void With_ChangedValue_LeavesTheOriginalUntouched()
    {
        var original = new SignalParameters();

        SignalParameters changed = original with { BiasEmaPeriod = 200, RequireBos = false };

        Assert.Equal(50, original.BiasEmaPeriod);
        Assert.True(original.RequireBos);
        Assert.Equal(200, changed.BiasEmaPeriod);
        Assert.False(changed.RequireBos);
        Assert.NotEqual(original, changed);
    }

    [Fact]
    public void Equals_SameValues_IsTrue()
    {
        var first = new SignalParameters { BiasEmaPeriod = 100 };
        var second = new SignalParameters { BiasEmaPeriod = 100 };

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Contains("BiasEmaPeriod = 100", first.ToString());
    }
}
