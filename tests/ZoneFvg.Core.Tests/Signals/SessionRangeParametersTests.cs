using System.Reflection;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SessionRange parameters and the tracker's constructor (SIG-032). Validate checks the four
/// times, their order in the trading day, the window length and the two numbers, and names the
/// first parameter that is out of range.
/// </summary>
public class SessionRangeParametersTests
{
    [Fact]
    [Trait("Req", "SIG-032")]
    public void Defaults_MatchSpecSection38()
    {
        var parameters = new SessionRangeParameters();

        Assert.Equal(TimeSpan.FromHours(19.0), parameters.RangeStartNy);
        Assert.Equal(TimeSpan.FromHours(3.0), parameters.RangeEndNy);
        Assert.Equal(TimeSpan.FromHours(12.0), parameters.LastEntryNy);
        Assert.Equal(new TimeSpan(16, 30, 0), parameters.ExitNy);
        Assert.Equal(0.5, parameters.MinWidthAtr);
        Assert.Equal(1.0, parameters.StopAtrMult);
        Assert.Equal(16, SessionRangeParameters.MinRangeBars);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Properties_AreTheSixSessionRangeParameters()
    {
        string[] actual = typeof(SessionRangeParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "ExitNy", "LastEntryNy", "MinWidthAtr", "RangeEndNy", "RangeStartNy", "RangeTrendFastEma", "StopAtrMult" }, actual);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Validate_Defaults_AreAccepted()
    {
        Assert.Null(Record.Exception(() => new SessionRangeParameters().Validate()));
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData("RangeStartNy", 19, 5, 0)]
    [InlineData("RangeStartNy", 19, 0, 1)]
    [InlineData("RangeEndNy", 3, 10, 0)]
    [InlineData("LastEntryNy", 11, 59, 0)]
    [InlineData("ExitNy", 16, 31, 0)]
    public void Validate_TimeNotOnAQuarterHour_ThrowsAndNamesTheParameter(string parameter, int hour, int minute, int second)
    {
        var value = new TimeSpan(hour, minute, second);
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WithTime(parameter, value).Validate());

        Assert.Equal(parameter, exception.ParamName);
        Assert.StartsWith(parameter + " must be a time of day from 00:00 up to but not including 24:00 on a 15-minute boundary but was " + Invariant.Text(value) + ".", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData("RangeStartNy")]
    [InlineData("RangeEndNy")]
    [InlineData("LastEntryNy")]
    [InlineData("ExitNy")]
    public void Validate_TimeOutsideADay_Throws(string parameter)
    {
        var below = Assert.Throws<ArgumentOutOfRangeException>(() => WithTime(parameter, TimeSpan.FromMinutes(-15.0)).Validate());
        var above = Assert.Throws<ArgumentOutOfRangeException>(() => WithTime(parameter, TimeSpan.FromHours(24.0)).Validate());

        Assert.Equal(parameter, below.ParamName);
        Assert.Equal(parameter, above.ParamName);
        Assert.Contains("1.00:00:00", above.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData(19, 0)]
    [InlineData(18, 0)]
    [InlineData(17, 0)]
    public void Validate_RangeEndNotAfterRangeStartInTheTradingDay_Throws(int endHour, int endMinute)
    {
        var parameters = new SessionRangeParameters { RangeEndNy = new TimeSpan(endHour, endMinute, 0) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("RangeEndNy", exception.ParamName);
        Assert.StartsWith("RangeEndNy must be after RangeStartNy (19:00:00) in the order of the trading day but was " + Invariant.Text(parameters.RangeEndNy) + ".", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Validate_RangeStartAt1700_IsTheFirstMinuteOfTheTradingDayAndAccepted()
    {
        var parameters = new SessionRangeParameters { RangeStartNy = TimeSpan.FromHours(17.0) };

        Assert.Null(Record.Exception(() => parameters.Validate()));
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Validate_WindowOfExactly16Bars_IsAccepted()
    {
        var parameters = new SessionRangeParameters { RangeEndNy = TimeSpan.FromHours(23.0) };

        Assert.Null(Record.Exception(() => parameters.Validate()));
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Validate_WindowOf15Bars_Throws()
    {
        var parameters = new SessionRangeParameters { RangeEndNy = new TimeSpan(22, 45, 0) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("RangeEndNy", exception.ParamName);
        Assert.StartsWith("RangeEndNy must leave a window of at least 16 M15 bars after RangeStartNy (19:00:00) but was 22:45:00.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Validate_LastEntryEqualToRangeEnd_IsAccepted()
    {
        var parameters = new SessionRangeParameters { LastEntryNy = TimeSpan.FromHours(3.0) };

        Assert.Null(Record.Exception(() => parameters.Validate()));
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Validate_LastEntryBeforeRangeEnd_Throws()
    {
        var parameters = new SessionRangeParameters { LastEntryNy = new TimeSpan(2, 45, 0) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("LastEntryNy", exception.ParamName);
        Assert.StartsWith("LastEntryNy must not be before RangeEndNy (03:00:00) in the order of the trading day but was 02:45:00.", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData(12, 15)]
    [InlineData(16, 45)]
    public void Validate_ExitAfterLastEntry_IsAccepted(int hour, int minute)
    {
        var parameters = new SessionRangeParameters { ExitNy = new TimeSpan(hour, minute, 0) };

        Assert.Null(Record.Exception(() => parameters.Validate()));
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData(12, 0)]
    [InlineData(11, 45)]
    [InlineData(17, 0)]
    public void Validate_ExitNotAfterLastEntryInTheTradingDay_Throws(int hour, int minute)
    {
        var parameters = new SessionRangeParameters { ExitNy = new TimeSpan(hour, minute, 0) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("ExitNy", exception.ParamName);
        Assert.StartsWith("ExitNy must be after LastEntryNy (12:00:00) in the order of the trading day but was " + Invariant.Text(parameters.ExitNy) + ".", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData("MinWidthAtr", -0.01, "must be a finite number that is not negative")]
    [InlineData("MinWidthAtr", double.NaN, "must be a finite number that is not negative")]
    [InlineData("MinWidthAtr", double.PositiveInfinity, "must be a finite number that is not negative")]
    [InlineData("StopAtrMult", 0.0, "must be a finite number greater than zero")]
    [InlineData("StopAtrMult", -1.0, "must be a finite number greater than zero")]
    [InlineData("StopAtrMult", double.NaN, "must be a finite number greater than zero")]
    [InlineData("StopAtrMult", double.PositiveInfinity, "must be a finite number greater than zero")]
    public void Validate_NumberOutOfRange_ThrowsAndNamesTheParameter(string parameter, double value, string rule)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WithNumber(parameter, value).Validate());

        Assert.Equal(parameter, exception.ParamName);
        Assert.StartsWith(parameter + " " + rule + " but was " + Invariant.Text(value) + ".", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData("MinWidthAtr", 0.0)]
    [InlineData("StopAtrMult", 0.001)]
    public void Validate_NumberAtTheEdgeOfItsRange_IsAccepted(string parameter, double value)
    {
        Assert.Null(Record.Exception(() => WithNumber(parameter, value).Validate()));
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Validate_SeveralValuesOutOfRange_NamesTheFirstInTheDocumentedOrder()
    {
        var parameters = new SessionRangeParameters { ExitNy = TimeSpan.FromMinutes(1.0), StopAtrMult = 0.0, RangeEndNy = new TimeSpan(22, 45, 0) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("ExitNy", exception.ParamName);
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Constructor_NullParameters_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionRangeTracker(null!, 2.0, TimeSpan.FromHours(12.0)));
    }

    [Fact]
    [Trait("Req", "SIG-032")]
    public void Constructor_InvalidParameters_ThrowsFromValidate()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SessionRangeTracker(new SessionRangeParameters { StopAtrMult = 0.0 }, 2.0, TimeSpan.FromHours(12.0)));

        Assert.Equal("StopAtrMult", exception.ParamName);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_RewardRiskOutOfRange_Throws(double rewardRisk)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SessionRangeTracker(new SessionRangeParameters(), rewardRisk, TimeSpan.FromHours(12.0)));

        Assert.Equal("rewardRisk", exception.ParamName);
        Assert.StartsWith("rewardRisk must be a finite number greater than zero but was " + Invariant.Text(rewardRisk) + ".", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData(-1L)]
    [InlineData(864000000000L)]
    public void Constructor_FridayNoEntryOutsideADay_Throws(long ticks)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SessionRangeTracker(new SessionRangeParameters(), 2.0, TimeSpan.FromTicks(ticks)));

        Assert.Equal("fridayNoEntryUtc", exception.ParamName);
        Assert.StartsWith("fridayNoEntryUtc must be a time of day from 00:00 up to but not including 24:00 but was " + Invariant.Text(TimeSpan.FromTicks(ticks)) + ".", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-032")]
    [InlineData(0L)]
    [InlineData(863999999999L)]
    public void Constructor_FridayNoEntryAtTheEdgeOfADay_IsAccepted(long ticks)
    {
        Assert.Null(Record.Exception(() => new SessionRangeTracker(new SessionRangeParameters(), 0.001, TimeSpan.FromTicks(ticks))));
    }

    private static SessionRangeParameters WithTime(string parameter, TimeSpan value)
    {
        var parameters = new SessionRangeParameters();
        return parameter switch
        {
            "RangeStartNy" => parameters with { RangeStartNy = value },
            "RangeEndNy" => parameters with { RangeEndNy = value },
            "LastEntryNy" => parameters with { LastEntryNy = value },
            "ExitNy" => parameters with { ExitNy = value },
            _ => throw new ArgumentException("Unknown parameter " + parameter, nameof(parameter)),
        };
    }

    private static SessionRangeParameters WithNumber(string parameter, double value)
    {
        var parameters = new SessionRangeParameters();
        return parameter switch
        {
            "MinWidthAtr" => parameters with { MinWidthAtr = value },
            "StopAtrMult" => parameters with { StopAtrMult = value },
            _ => throw new ArgumentException("Unknown parameter " + parameter, nameof(parameter)),
        };
    }
}
