using System.Globalization;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The two filter parameters of the Breakout mode (SIG-028, SIG-029): TrendSlowEmaPeriod is 0 (off)
/// or from 2 to 10,000 (the bar-count range of SignalParameters, architecture section 10, row of
/// 2026-11-13); LastConfirmUtc lies after WindowStartUtc and not after WindowEndUtc, and when it
/// is not set it equals WindowEndUtc (off). Both are checked after the four values of SIG-024.
/// </summary>
public class BreakoutFilterParametersTests
{
    [Fact]
    [Trait("Req", "SIG-028")]
    [Trait("Req", "SIG-029")]
    public void Defaults_AreTheOffValues()
    {
        var parameters = new BreakoutParameters();

        Assert.Equal(0, parameters.TrendSlowEmaPeriod);
        Assert.Equal(TimeSpan.FromHours(17.0), parameters.LastConfirmUtc);
        Assert.Equal(parameters.WindowEndUtc, parameters.LastConfirmUtc);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void LastConfirmUtc_NotSet_FollowsTheWindowEndAlsoInACopy()
    {
        var parameters = new BreakoutParameters { WindowEndUtc = new TimeSpan(16, 30, 0) };
        BreakoutParameters copy = parameters with { WindowEndUtc = TimeSpan.FromHours(12.0) };

        Assert.Equal(new TimeSpan(16, 30, 0), parameters.LastConfirmUtc);
        Assert.Equal(TimeSpan.FromHours(12.0), copy.LastConfirmUtc);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void LastConfirmUtc_Set_KeepsItsValueWhenTheWindowEndChanges()
    {
        var parameters = new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(8.0) };
        BreakoutParameters copy = parameters with { WindowEndUtc = TimeSpan.FromHours(12.0) };

        Assert.Equal(TimeSpan.FromHours(8.0), copy.LastConfirmUtc);
        Assert.NotEqual(parameters, new BreakoutParameters());
        Assert.Equal(parameters, new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(8.0) });
    }

    [Theory]
    [Trait("Req", "SIG-028")]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(200)]
    [InlineData(10000)]
    public void Validate_TrendSlowEmaPeriodInRange_IsAccepted(int period)
    {
        Exception? exception = Record.Exception(() => new BreakoutParameters { TrendSlowEmaPeriod = period }.Validate());

        Assert.Null(exception);
    }

    [Theory]
    [Trait("Req", "SIG-028")]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(10001)]
    [InlineData(int.MinValue)]
    public void Validate_TrendSlowEmaPeriodOutOfRange_ThrowsAndNamesTheParameter(int period)
    {
        var parameters = new BreakoutParameters { TrendSlowEmaPeriod = period };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("TrendSlowEmaPeriod", exception.ParamName);
        Assert.Contains("TrendSlowEmaPeriod", exception.Message, StringComparison.Ordinal);
        Assert.Contains("but was " + Invariant.Text(period) + ".", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-029")]
    [InlineData(421)] // 07:01
    [InlineData(480)] // 08:00
    [InlineData(1020)] // 17:00, equal to the window end
    public void Validate_LastConfirmUtcInRange_IsAccepted(int minutes)
    {
        Exception? exception = Record.Exception(() => new BreakoutParameters { LastConfirmUtc = TimeSpan.FromMinutes(minutes) }.Validate());

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void Validate_LastConfirmUtcOneTickAfterTheStart_IsAccepted()
    {
        var parameters = new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(7.0) + TimeSpan.FromTicks(1) };

        Assert.Null(Record.Exception(() => parameters.Validate()));
    }

    [Theory]
    [Trait("Req", "SIG-029")]
    [InlineData(420, "07:00:00")] // equal to the window start
    [InlineData(419, "06:59:00")] // before the window start
    [InlineData(0, "00:00:00")]
    [InlineData(1021, "17:01:00")] // after the window end
    [InlineData(-60, "-01:00:00")]
    public void Validate_LastConfirmUtcOutOfRange_ThrowsAndNamesTheParameter(int minutes, string text)
    {
        var parameters = new BreakoutParameters { LastConfirmUtc = TimeSpan.FromMinutes(minutes) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal("LastConfirmUtc", exception.ParamName);
        Assert.Contains("WindowStartUtc (07:00:00)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("WindowEndUtc (17:00:00)", exception.Message, StringComparison.Ordinal);
        Assert.Contains("but was " + text + ".", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void Validate_LastConfirmUtcOneTickAfterTheWindowEnd_Throws()
    {
        var parameters = new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(17.0) + TimeSpan.FromTicks(1) };

        Assert.Equal("LastConfirmUtc", Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate()).ParamName);
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void Validate_LastConfirmUtcAgainstAMovedWindow_UsesTheWindowOfTheParameters()
    {
        var accepted = new BreakoutParameters { WindowStartUtc = TimeSpan.FromHours(9.0), WindowEndUtc = TimeSpan.FromHours(12.0), LastConfirmUtc = TimeSpan.FromHours(12.0) };
        var beforeStart = accepted with { LastConfirmUtc = TimeSpan.FromHours(8.0) };
        var afterEnd = accepted with { LastConfirmUtc = new TimeSpan(12, 15, 0) };

        Assert.Null(Record.Exception(() => accepted.Validate()));
        Assert.Equal("LastConfirmUtc", Assert.Throws<ArgumentOutOfRangeException>(() => beforeStart.Validate()).ParamName);
        Assert.Equal("LastConfirmUtc", Assert.Throws<ArgumentOutOfRangeException>(() => afterEnd.Validate()).ParamName);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    [Trait("Req", "SIG-029")]
    public void Validate_SeveralInvalidValues_ReportsTheWindowThenTheSlowPeriodThenTheLastConfirmation()
    {
        var all = new BreakoutParameters { WindowEndUtc = TimeSpan.FromHours(6.0), TrendSlowEmaPeriod = 1, LastConfirmUtc = TimeSpan.FromHours(20.0) };
        var fromSlow = all with { WindowEndUtc = TimeSpan.FromHours(17.0) };
        var fromLast = fromSlow with { TrendSlowEmaPeriod = 200 };

        Assert.Equal("WindowEndUtc", Assert.Throws<ArgumentOutOfRangeException>(() => all.Validate()).ParamName);
        Assert.Equal("TrendSlowEmaPeriod", Assert.Throws<ArgumentOutOfRangeException>(() => fromSlow.Validate()).ParamName);
        Assert.Equal("LastConfirmUtc", Assert.Throws<ArgumentOutOfRangeException>(() => fromLast.Validate()).ParamName);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    [Trait("Req", "SIG-029")]
    public void Validate_MessagesUnderAnotherCulture_StillUseInvariantFormatting()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        var other = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        other.NumberFormat.NegativeSign = "~";
        other.NumberFormat.NumberDecimalSeparator = ",";
        other.DateTimeFormat.TimeSeparator = ".";
        try
        {
            CultureInfo.CurrentCulture = other;

            var slow = Assert.Throws<ArgumentOutOfRangeException>(() => new BreakoutParameters { TrendSlowEmaPeriod = -5 }.Validate());
            var last = Assert.Throws<ArgumentOutOfRangeException>(() => new BreakoutParameters { LastConfirmUtc = new TimeSpan(6, 30, 0) }.Validate());

            Assert.Contains("but was -5.", slow.Message, StringComparison.Ordinal);
            Assert.Contains("10000", slow.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("~", slow.Message, StringComparison.Ordinal);
            Assert.Contains("but was 06:30:00.", last.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("06.30", last.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    [Trait("Req", "SIG-029")]
    public void TrackerConstructor_InvalidFilterValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakoutTracker(new BreakoutParameters { TrendSlowEmaPeriod = 1 }, 2.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BreakoutTracker(new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(7.0) }, 2.0));
    }
}
