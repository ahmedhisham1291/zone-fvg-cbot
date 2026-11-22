using System.Globalization;
using System.Reflection;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Breakout parameters and the tracker's constructor (SIG-024). Validate checks EntryBufferAtr,
/// StopAtrMult, WindowStartUtc, WindowEndUtc and then the order of the window, and names the first
/// parameter that is out of range.
/// </summary>
public class BreakoutParametersTests
{
    [Fact]
    [Trait("Req", "SIG-024")]
    public void Defaults_MatchSpecSection37()
    {
        var parameters = new BreakoutParameters();

        Assert.Equal(0.1, parameters.EntryBufferAtr);
        Assert.Equal(1.0, parameters.StopAtrMult);
        Assert.Equal(TimeSpan.FromHours(7.0), parameters.WindowStartUtc);
        Assert.Equal(TimeSpan.FromHours(17.0), parameters.WindowEndUtc);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void Properties_AreTheFourBreakoutParameters()
    {
        string[] actual = typeof(BreakoutParameters)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "EntryBufferAtr", "StopAtrMult", "WindowEndUtc", "WindowStartUtc" }, actual);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void Validate_Defaults_AreAccepted()
    {
        Exception? exception = Record.Exception(() => new BreakoutParameters().Validate());

        Assert.Null(exception);
    }

    [Theory]
    [Trait("Req", "SIG-024")]
    [InlineData("EntryBufferAtr", -0.01)]
    [InlineData("EntryBufferAtr", double.NaN)]
    [InlineData("EntryBufferAtr", double.PositiveInfinity)]
    [InlineData("StopAtrMult", 0.0)]
    [InlineData("StopAtrMult", -1.0)]
    [InlineData("StopAtrMult", double.NaN)]
    [InlineData("StopAtrMult", double.PositiveInfinity)]
    public void Validate_NumberOutOfRange_ThrowsAndNamesTheParameter(string parameter, double value)
    {
        BreakoutParameters parameters = WithNumber(parameter, value);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal(parameter, exception.ParamName);
        Assert.Contains(parameter, exception.Message, StringComparison.Ordinal);
        Assert.Contains(Invariant.Text(value), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-024")]
    [InlineData("EntryBufferAtr", 0.0)]
    [InlineData("StopAtrMult", 0.001)]
    public void Validate_NumberAtTheEdgeOfItsRange_IsAccepted(string parameter, double value)
    {
        Exception? exception = Record.Exception(() => WithNumber(parameter, value).Validate());

        Assert.Null(exception);
    }

    [Theory]
    [Trait("Req", "SIG-024")]
    [InlineData("WindowStartUtc", -1)] // one minute before 00:00
    [InlineData("WindowStartUtc", 1440)] // 24:00
    [InlineData("WindowEndUtc", -1)]
    [InlineData("WindowEndUtc", 1440)]
    public void Validate_WindowTimeNotATimeOfDay_ThrowsAndNamesTheParameter(string parameter, int minutes)
    {
        TimeSpan value = TimeSpan.FromMinutes(minutes);
        BreakoutParameters parameters = parameter == "WindowStartUtc"
            ? new BreakoutParameters { WindowStartUtc = value }
            : new BreakoutParameters { WindowEndUtc = value };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal(parameter, exception.ParamName);
        Assert.Contains(parameter, exception.Message, StringComparison.Ordinal);
        Assert.Contains(Invariant.Text(value), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void Validate_WindowStartOneTickBelow24Hours_IsRejectedOnlyBecauseTheEndIsNotAfterIt()
    {
        // 23:59:59.9999999 is a time of day; the end (17:00) is then not after the start.
        var parameters = new BreakoutParameters { WindowStartUtc = TimeSpan.FromHours(24.0) - TimeSpan.FromTicks(1) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("WindowEndUtc", exception.ParamName);
    }

    [Theory]
    [Trait("Req", "SIG-024")]
    [InlineData(420, 420)] // end equal to the start
    [InlineData(420, 419)] // end one minute before the start
    public void Validate_WindowEndNotAfterTheStart_ThrowsAndNamesTheEnd(int startMinutes, int endMinutes)
    {
        var parameters = new BreakoutParameters { WindowStartUtc = TimeSpan.FromMinutes(startMinutes), WindowEndUtc = TimeSpan.FromMinutes(endMinutes) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("WindowEndUtc", exception.ParamName);
        Assert.Contains("WindowStartUtc", exception.Message, StringComparison.Ordinal);
        Assert.Contains(Invariant.Text(TimeSpan.FromMinutes(endMinutes)), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-024")]
    [InlineData(0, 1)] // from 00:00, one minute long
    [InlineData(420, 421)] // end one minute after the start
    [InlineData(0, 1439)] // 00:00 to 23:59
    public void Validate_WindowAtTheEdgeOfItsRange_IsAccepted(int startMinutes, int endMinutes)
    {
        var parameters = new BreakoutParameters { WindowStartUtc = TimeSpan.FromMinutes(startMinutes), WindowEndUtc = TimeSpan.FromMinutes(endMinutes) };

        Exception? exception = Record.Exception(() => parameters.Validate());

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void Validate_SeveralInvalidValues_ReportsTheFirstInOrder()
    {
        var all = new BreakoutParameters { EntryBufferAtr = -1.0, StopAtrMult = 0.0, WindowStartUtc = TimeSpan.FromHours(-1.0), WindowEndUtc = TimeSpan.FromHours(25.0) };
        var fromStop = all with { EntryBufferAtr = 0.1 };
        var fromStart = fromStop with { StopAtrMult = 1.0 };
        var fromEnd = fromStart with { WindowStartUtc = TimeSpan.FromHours(7.0) };

        Assert.Equal("EntryBufferAtr", Assert.Throws<ArgumentOutOfRangeException>(() => all.Validate()).ParamName);
        Assert.Equal("StopAtrMult", Assert.Throws<ArgumentOutOfRangeException>(() => fromStop.Validate()).ParamName);
        Assert.Equal("WindowStartUtc", Assert.Throws<ArgumentOutOfRangeException>(() => fromStart.Validate()).ParamName);
        Assert.Equal("WindowEndUtc", Assert.Throws<ArgumentOutOfRangeException>(() => fromEnd.Validate()).ParamName);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void Validate_MessageUnderACultureWithDecimalComma_StillUsesInvariantFormatting()
    {
        // A culture that writes -0.25 as "-0,25" must not change the text (AGENTS.md rule 7).
        CultureInfo original = CultureInfo.CurrentCulture;
        var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaCulture.NumberFormat.NumberDecimalSeparator = ",";
        try
        {
            CultureInfo.CurrentCulture = commaCulture;

            var parameters = new BreakoutParameters { EntryBufferAtr = -0.25 };

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

            Assert.Contains("-0.25", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("-0,25", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void Constructor_MessageUnderACultureWithDecimalComma_StillUsesInvariantFormatting()
    {
        // A culture that writes -0.5 as "-0,5" must not change the text (AGENTS.md rule 7).
        CultureInfo original = CultureInfo.CurrentCulture;
        var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaCulture.NumberFormat.NumberDecimalSeparator = ",";
        try
        {
            CultureInfo.CurrentCulture = commaCulture;

            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new BreakoutTracker(new BreakoutParameters(), -0.5));

            Assert.Contains("-0.5", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("-0,5", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [Trait("Req", "SIG-024")]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_RewardRiskOutOfRange_Throws(double rewardRisk)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new BreakoutTracker(new BreakoutParameters(), rewardRisk));

        Assert.Equal("rewardRisk", exception.ParamName);
        Assert.Contains(Invariant.Text(rewardRisk), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void Constructor_RewardRiskJustAboveZero_IsAccepted()
    {
        var tracker = new BreakoutTracker(new BreakoutParameters(), 0.001);

        Assert.Null(tracker.Reference);
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void Constructor_NullParameters_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BreakoutTracker(null!, 2.0));
    }

    [Fact]
    [Trait("Req", "SIG-024")]
    public void Constructor_InvalidParameters_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new BreakoutTracker(new BreakoutParameters { StopAtrMult = 0.0 }, 2.0));

        Assert.Equal("StopAtrMult", exception.ParamName);
    }

    private static BreakoutParameters WithNumber(string parameter, double value)
    {
        return parameter switch
        {
            "EntryBufferAtr" => new BreakoutParameters { EntryBufferAtr = value },
            "StopAtrMult" => new BreakoutParameters { StopAtrMult = value },
            _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, "Unknown parameter."),
        };
    }
}
