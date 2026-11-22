using System.Globalization;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-017: <see cref="BotParameters.Breakout"/> selects the entry mode, and breakout parameters
/// whose window differs from the session (SessionStartUtc, SessionEndUtc) are refused at start,
/// by <see cref="BotParameters.Validate"/> and so by the <see cref="BotCore"/> constructor.
/// </summary>
public class BotParametersBreakoutTests
{
    private static readonly TimeSpan Seven = TimeSpan.FromHours(7.0);
    private static readonly TimeSpan Seventeen = TimeSpan.FromHours(17.0);

    private static BotParameters WithWindow(TimeSpan start, TimeSpan end)
    {
        return BreakoutRecoveryData.Parameters() with { Breakout = new BreakoutParameters { WindowStartUtc = start, WindowEndUtc = end } };
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Mode_BreakoutNull_IsZoneFvg_BreakoutSet_IsBreakout()
    {
        Assert.Null(new BotParameters().Breakout);
        Assert.Equal(EntryMode.ZoneFvg, new BotParameters().Mode);
        Assert.Equal(EntryMode.ZoneFvg, RecoveryData.Parameters().Mode);
        Assert.Equal(EntryMode.Breakout, BreakoutRecoveryData.Parameters().Mode);
        Assert.Equal(EntryMode.ZoneFvg, (BreakoutRecoveryData.Parameters() with { Breakout = null }).Mode);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void EntryMode_Values_ZoneFvgIsZeroAndBreakoutIsOne()
    {
        Assert.Equal(new[] { EntryMode.ZoneFvg, EntryMode.Breakout }, Enum.GetValues<EntryMode>());
        Assert.Equal(0, (int)EntryMode.ZoneFvg);
        Assert.Equal(1, (int)EntryMode.Breakout);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Validate_WindowEqualToTheSession_IsAccepted()
    {
        BreakoutRecoveryData.Parameters().Validate(isBacktest: true);
        WithWindow(Seven, Seventeen).Validate(isBacktest: false);

        // Other session times, with the same window: accepted, also by the constructor.
        var session = new ExecutionParameters { SessionStartUtc = TimeSpan.FromHours(8.0), SessionEndUtc = new TimeSpan(16, 30, 0) };
        BotParameters other = WithWindow(TimeSpan.FromHours(8.0), new TimeSpan(16, 30, 0)) with { Execution = session };
        other.Validate(isBacktest: true);
        var h = new BotHarness();
        Assert.NotNull(h.NewCore(other));
    }

    [Theory]
    [Trait("Req", "REC-017")]
    [InlineData(1, 0, "07:01:00", "17:00:00")]
    [InlineData(-1, 0, "06:59:00", "17:00:00")]
    [InlineData(0, 1, "07:00:00", "17:01:00")]
    [InlineData(0, -1, "07:00:00", "16:59:00")]
    public void Constructor_WindowOneMinuteOffTheSession_ThrowsNamingBreakoutAndBothPairsOfTimes(int startMinutes, int endMinutes, string windowStart, string windowEnd)
    {
        var h = new BotHarness();
        BotParameters parameters = WithWindow(Seven.Add(TimeSpan.FromMinutes(startMinutes)), Seventeen.Add(TimeSpan.FromMinutes(endMinutes)));

        ArgumentException error = Assert.Throws<ArgumentException>(() => h.NewCore(parameters));

        Assert.Equal(nameof(BotParameters.Breakout), error.ParamName);
        Assert.Contains("Breakout.WindowStartUtc " + windowStart, error.Message, StringComparison.Ordinal);
        Assert.Contains("Breakout.WindowEndUtc " + windowEnd, error.Message, StringComparison.Ordinal);
        Assert.Contains("Execution.SessionStartUtc 07:00:00", error.Message, StringComparison.Ordinal);
        Assert.Contains("Execution.SessionEndUtc 17:00:00", error.Message, StringComparison.Ordinal);
        Assert.Empty(h.Journal.Entries);
        Assert.Equal(0, h.Broker.WriteRequestCount);
        Assert.Throws<ArgumentException>(() => parameters.Validate(isBacktest: true));
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Validate_SessionMovedButTheWindowLeftAtItsDefault_Throws()
    {
        BotParameters parameters = BreakoutRecoveryData.Parameters() with { Execution = new ExecutionParameters { SessionStartUtc = TimeSpan.FromHours(8.0) } };

        ArgumentException error = Assert.Throws<ArgumentException>(() => parameters.Validate(isBacktest: true));

        Assert.Equal(nameof(BotParameters.Breakout), error.ParamName);
        Assert.Contains("Breakout.WindowStartUtc 07:00:00", error.Message, StringComparison.Ordinal);
        Assert.Contains("Execution.SessionStartUtc 08:00:00", error.Message, StringComparison.Ordinal);

        // Without breakout parameters (ZoneFvg mode) the same session is accepted.
        (parameters with { Breakout = null }).Validate(isBacktest: true);
    }

    [Theory]
    [Trait("Req", "REC-017")]
    [InlineData("StopAtrMult")]
    [InlineData("EntryBufferAtr")]
    [InlineData("WindowEndUtc")]
    public void Constructor_InvalidBreakoutParameters_ThrowTheirOwnException(string name)
    {
        BreakoutParameters breakout = name switch
        {
            "StopAtrMult" => new BreakoutParameters { StopAtrMult = 0.0 },
            "EntryBufferAtr" => new BreakoutParameters { EntryBufferAtr = -0.1 },
            _ => new BreakoutParameters { WindowStartUtc = Seventeen, WindowEndUtc = Seven },
        };
        var h = new BotHarness();

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => h.NewCore(BreakoutRecoveryData.Parameters() with { Breakout = breakout }));

        Assert.Equal(name, error.ParamName);
        Assert.Empty(h.Journal.Entries);
    }

    [Fact]
    [Trait("Req", "REC-017")]
    public void Validate_MessageUnderACultureWithOtherSeparators_StillUsesInvariantFormatting()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        var other = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        other.NumberFormat.NumberDecimalSeparator = ",";
        other.DateTimeFormat.TimeSeparator = ".";
        try
        {
            CultureInfo.CurrentCulture = other;

            ArgumentException error = Assert.Throws<ArgumentException>(() => WithWindow(new TimeSpan(7, 1, 0), Seventeen).Validate(isBacktest: true));

            Assert.Contains("Breakout.WindowStartUtc 07:01:00", error.Message, StringComparison.Ordinal);
            Assert.Contains("Execution.SessionEndUtc 17:00:00", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("07.01", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
