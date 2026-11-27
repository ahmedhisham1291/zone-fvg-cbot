using System.Reflection;
using Xunit;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-043: the Overnight parameters with the defaults of spec 7 and their validation. A refusal
/// names the parameter (the core names are the cBot's).
/// </summary>
public class OvernightParametersTests
{
    [Fact]
    [Trait("Req", "SIG-043")]
    public void Defaults_MatchSpecSection7()
    {
        var parameters = new OvernightParameters();

        Assert.Equal(new TimeSpan(18, 15, 0), parameters.NightEntryNy);
        Assert.Equal(new TimeSpan(19, 0, 0), parameters.NightLatestNy);
        Assert.Equal(new TimeSpan(2, 45, 0), parameters.NightExitNy);
        Assert.Equal(0.03, parameters.NightEntryBufferAtr);
        Assert.Equal(1.0, parameters.NightStopAtr);
        Assert.Equal(5.0, parameters.NightTargetR);
        Assert.Equal(60, parameters.NightOrderMinutes);
        Assert.Null(Record.Exception(() => parameters.Validate()));
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Properties_AreTheSevenNightParameters()
    {
        string[] actual = typeof(OvernightParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "NightEntryBufferAtr", "NightEntryNy", "NightExitNy", "NightLatestNy", "NightOrderMinutes", "NightStopAtr", "NightTargetR" }, actual);
    }

    public static IEnumerable<object[]> TimesOutOfRange()
    {
        foreach (string name in new[] { "NightEntryNy", "NightLatestNy", "NightExitNy" })
        {
            yield return new object[] { name, -1L };
            yield return new object[] { name, TimeSpan.FromHours(24.0).Ticks };
            yield return new object[] { name, TimeSpan.FromHours(25.0).Ticks };
        }
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [MemberData(nameof(TimesOutOfRange))]
    public void Validate_TimeOutside0000To2359_RefusedNamingIt(string name, long ticks)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WithTime(name, TimeSpan.FromTicks(ticks)).Validate());

        Assert.Equal(name, exception.ParamName);
        Assert.Contains(name + " must be a time of day from 00:00 up to but not including 24:00", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [InlineData("NightEntryNy", 18, 59)]
    [InlineData("NightEntryNy", 17, 0)]
    [InlineData("NightLatestNy", 18, 16)]
    [InlineData("NightExitNy", 16, 59)]
    [InlineData("NightExitNy", 19, 1)]
    public void Validate_TimeInsideTheDayAndInOrder_Accepted(string name, int hour, int minute)
    {
        Assert.Null(Record.Exception(() => WithTime(name, new TimeSpan(hour, minute, 0)).Validate()));
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [InlineData(19, 0)]
    [InlineData(19, 1)]
    [InlineData(2, 0)]
    public void Validate_EntryNotBeforeLatest_RefusesNightLatestNy(int hour, int minute)
    {
        var parameters = new OvernightParameters { NightEntryNy = new TimeSpan(hour, minute, 0) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("NightLatestNy", exception.ParamName);
        Assert.Contains("NightLatestNy must be after NightEntryNy", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [InlineData(19, 0)]
    [InlineData(18, 30)]
    [InlineData(17, 0)]
    public void Validate_LatestNotBeforeExitInTradingDayOrder_RefusesNightExitNy(int hour, int minute)
    {
        // 19:00 is two hours into the trading day; 17:00 and 18:30 come before it in that order.
        var parameters = new OvernightParameters { NightExitNy = new TimeSpan(hour, minute, 0) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("NightExitNy", exception.ParamName);
        Assert.Contains("NightExitNy must be after NightLatestNy", exception.Message, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> NumbersOutOfRange()
    {
        foreach (string name in new[] { "NightEntryBufferAtr", "NightStopAtr", "NightTargetR" })
        {
            yield return new object[] { name, 0.0 };
            yield return new object[] { name, -0.01 };
            yield return new object[] { name, double.NaN };
            yield return new object[] { name, double.PositiveInfinity };
        }
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [MemberData(nameof(NumbersOutOfRange))]
    public void Validate_MultipleNotPositiveFinite_RefusedNamingIt(string name, double value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WithNumber(name, value).Validate());

        Assert.Equal(name, exception.ParamName);
        Assert.Contains(name + " must be a finite number greater than zero", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [InlineData("NightEntryBufferAtr", 0.0001)]
    [InlineData("NightStopAtr", 0.0001)]
    [InlineData("NightTargetR", 100.0)]
    public void Validate_MultiplePositiveFinite_Accepted(string name, double value)
    {
        Assert.Null(Record.Exception(() => WithNumber(name, value).Validate()));
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [InlineData(0)]
    [InlineData(181)]
    [InlineData(-1)]
    public void Validate_MinutesOutside1To180_RefusesNightOrderMinutes(int minutes)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new OvernightParameters { NightOrderMinutes = minutes }.Validate());

        Assert.Equal("NightOrderMinutes", exception.ParamName);
        Assert.Contains("NightOrderMinutes must be from 1 to 180", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [InlineData(1)]
    [InlineData(180)]
    public void Validate_Minutes1And180_Accepted(int minutes)
    {
        Assert.Null(Record.Exception(() => new OvernightParameters { NightOrderMinutes = minutes }.Validate()));
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Tracker_InvalidParameters_RefusedAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new OvernightTracker(null!));
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new OvernightTracker(new OvernightParameters { NightOrderMinutes = 0 }));
        Assert.Equal("NightOrderMinutes", exception.ParamName);
    }

    private static OvernightParameters WithTime(string name, TimeSpan value)
    {
        var defaults = new OvernightParameters();
        return name switch
        {
            "NightEntryNy" => defaults with { NightEntryNy = value },
            "NightLatestNy" => defaults with { NightLatestNy = value },
            _ => defaults with { NightExitNy = value },
        };
    }

    private static OvernightParameters WithNumber(string name, double value)
    {
        var defaults = new OvernightParameters();
        return name switch
        {
            "NightEntryBufferAtr" => defaults with { NightEntryBufferAtr = value },
            "NightStopAtr" => defaults with { NightStopAtr = value },
            _ => defaults with { NightTargetR = value },
        };
    }
}
