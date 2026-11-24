using Xunit;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

public class RiskParametersTests
{
    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Defaults_MatchSpecSections4And7()
    {
        var parameters = new RiskParameters();

        Assert.Equal(0.5, parameters.RiskPercent);
        Assert.Equal(1.5, parameters.MaxOpenRiskPercent);
        Assert.Equal(2, parameters.MaxPositionsPerCurrency);
        Assert.Equal(2.0, parameters.DailyLossLimitPercent);
        Assert.Equal(8.0, parameters.MaxDrawdownPercent);
        Assert.Equal(200000.0, parameters.InitialCapital);
        Assert.Equal(5.0, parameters.FtmoDailyLossPercent);
        Assert.Equal(10.0, parameters.FtmoMaxLossPercent);
        Assert.Equal(1.0, parameters.FtmoGuardBufferPercent);
        Assert.Equal(2, parameters.MaxEntriesPerSymbolPerDay);
        Assert.Equal(6, parameters.MaxEntriesPerDay);
        Assert.Equal(20, parameters.MaxRequestsPerHour);
        Assert.Equal(100, parameters.MaxRequestsPerDay);
        Assert.Equal(5, parameters.MaxErrorsPerHour);
        Assert.Equal(3000000.0, parameters.MaxVolumeUnits);
        Assert.False(parameters.SanityCapEnabled);
        Assert.Equal(0.55, parameters.SanityCapPercent);
        Assert.False(parameters.OpenRiskCapEnabled);
        Assert.False(parameters.CurrencyCapEnabled);
        Assert.Equal(SizingPlan.Ladder, parameters.SizingPlan);
        Assert.Equal(1.0, parameters.RecoveryRunLossPercent);
        Assert.Equal(1.28, parameters.RecoveryMaxRiskPercent);
        Assert.Equal(50.0, RiskParameters.MinFreeMarginPercentOfEquity);
    }

    [Fact]
    public void Validate_DefaultsWithEpochSet_AreAccepted()
    {
        Exception? exception = Record.Exception(() => new RiskParameters { RiskEpochDate = Epoch }.Validate());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void Validate_EpochNotSetAsUtc_Throws(DateTimeKind kind)
    {
        var parameters = new RiskParameters { RiskEpochDate = new DateTime(2026, 1, 1, 0, 0, 0, kind) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("RiskEpochDate", exception.ParamName);
    }

    [Fact]
    public void Validate_EpochWithATimeOfDay_Throws()
    {
        // RiskEpochDate is a date (it means 00:00 Prague time on that date), so a time of day is refused.
        var parameters = new RiskParameters { RiskEpochDate = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("RiskEpochDate", exception.ParamName);
    }

    [Fact]
    public void Validate_EpochLeftAtItsDefault_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new RiskParameters().Validate());

        Assert.Equal("RiskEpochDate", exception.ParamName);
    }

    [Theory]
    [InlineData("RiskPercent", 0.0)]
    [InlineData("RiskPercent", -0.5)]
    [InlineData("RiskPercent", double.NaN)]
    [InlineData("RiskPercent", 100.5)]
    [InlineData("MaxOpenRiskPercent", 0.0)]
    [InlineData("MaxPositionsPerCurrency", 0.0)]
    [InlineData("DailyLossLimitPercent", double.PositiveInfinity)]
    [InlineData("MaxDrawdownPercent", 0.0)]
    [InlineData("InitialCapital", 0.0)]
    [InlineData("InitialCapital", double.NaN)]
    [InlineData("FtmoDailyLossPercent", 0.0)]
    [InlineData("FtmoMaxLossPercent", 101.0)]
    [InlineData("FtmoGuardBufferPercent", -0.1)]
    [InlineData("FtmoGuardBufferPercent", double.NaN)]
    [InlineData("FtmoGuardBufferPercent", 5.0)]
    [InlineData("MaxEntriesPerSymbolPerDay", 0.0)]
    [InlineData("MaxEntriesPerDay", -1.0)]
    [InlineData("MaxRequestsPerHour", 0.0)]
    [InlineData("MaxRequestsPerDay", 0.0)]
    [InlineData("MaxErrorsPerHour", 0.0)]
    [InlineData("MaxVolumeUnits", 0.0)]
    [InlineData("MaxVolumeUnits", double.PositiveInfinity)]
    public void Validate_ValueOutOfRange_ThrowsNamingTheParameter(string name, double value)
    {
        RiskParameters parameters = With(name, value);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal(name, exception.ParamName);
    }

    [Fact]
    public void Validate_BufferNotBelowTheSmallerFtmoLimit_Throws()
    {
        // The overall limit (10) is now the smaller one; a buffer of 10 equals it.
        var parameters = new RiskParameters { RiskEpochDate = Epoch, FtmoDailyLossPercent = 20.0, FtmoGuardBufferPercent = 10.0 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("FtmoGuardBufferPercent", exception.ParamName);
    }

    [Fact]
    public void Validate_BoundaryValues_AreAccepted()
    {
        var valid = new RiskParameters { RiskEpochDate = Epoch };

        // 100% is the largest allowed percentage; a buffer of 0 and just below the smaller FTMO limit are allowed.
        Assert.Null(Record.Exception(() => (valid with { RiskPercent = 100.0 }).Validate()));
        Assert.Null(Record.Exception(() => (valid with { FtmoGuardBufferPercent = 0.0 }).Validate()));
        Assert.Null(Record.Exception(() => (valid with { FtmoGuardBufferPercent = 4.99 }).Validate()));
        Assert.Null(Record.Exception(() => (valid with { MaxPositionsPerCurrency = 1 }).Validate()));
    }

    [Fact]
    public void Validate_Message_UsesInvariantFormatting()
    {
        var parameters = new RiskParameters { RiskEpochDate = Epoch, RiskPercent = 100.25 };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Contains("100.25", exception.Message, StringComparison.Ordinal);
    }

    private static RiskParameters With(string name, double value)
    {
        var valid = new RiskParameters { RiskEpochDate = Epoch };
        return name switch
        {
            "RiskPercent" => valid with { RiskPercent = value },
            "MaxOpenRiskPercent" => valid with { MaxOpenRiskPercent = value },
            "MaxPositionsPerCurrency" => valid with { MaxPositionsPerCurrency = (int)value },
            "DailyLossLimitPercent" => valid with { DailyLossLimitPercent = value },
            "MaxDrawdownPercent" => valid with { MaxDrawdownPercent = value },
            "InitialCapital" => valid with { InitialCapital = value },
            "FtmoDailyLossPercent" => valid with { FtmoDailyLossPercent = value },
            "FtmoMaxLossPercent" => valid with { FtmoMaxLossPercent = value },
            "FtmoGuardBufferPercent" => valid with { FtmoGuardBufferPercent = value },
            "MaxEntriesPerSymbolPerDay" => valid with { MaxEntriesPerSymbolPerDay = (int)value },
            "MaxEntriesPerDay" => valid with { MaxEntriesPerDay = (int)value },
            "MaxRequestsPerHour" => valid with { MaxRequestsPerHour = (int)value },
            "MaxRequestsPerDay" => valid with { MaxRequestsPerDay = (int)value },
            "MaxErrorsPerHour" => valid with { MaxErrorsPerHour = (int)value },
            "MaxVolumeUnits" => valid with { MaxVolumeUnits = value },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown parameter name."),
        };
    }
}
