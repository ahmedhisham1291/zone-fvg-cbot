using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-034: <see cref="BotParameters.SessionRange"/> selects SessionRange mode; its values are
/// checked at start by <see cref="BotParameters.Validate"/> (and so by the <see cref="BotCore"/>
/// constructor), and it is refused together with breakout parameters: one mode per instance.
/// </summary>
public class BotParametersSessionRangeTests
{
    [Fact]
    [Trait("Req", "SIG-034")]
    public void Mode_SessionRangeSet_IsSessionRange_OtherwiseUnchanged()
    {
        Assert.Null(new BotParameters().SessionRange);
        Assert.Equal(EntryMode.SessionRange, SessionRangeBotData.Parameters().Mode);
        Assert.Equal(EntryMode.ZoneFvg, (SessionRangeBotData.Parameters() with { SessionRange = null }).Mode);
        Assert.Equal(EntryMode.Breakout, BreakoutRecoveryData.Parameters().Mode);
        Assert.Equal(EntryMode.ZoneFvg, RecoveryData.Parameters().Mode);
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Validate_DefaultSessionRange_AcceptedLiveAndInABacktest_AndByTheConstructor()
    {
        SessionRangeBotData.Parameters().Validate(isBacktest: true);
        SessionRangeBotData.Parameters().Validate(isBacktest: false);
        var h = new BotHarness();

        Assert.NotNull(h.NewCore(SessionRangeBotData.Parameters()));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Validate_BreakoutAndSessionRangeBothSet_RefusedWithTheNameSessionRange()
    {
        BotParameters both = SessionRangeBotData.Parameters() with { Breakout = new BreakoutParameters() };

        ArgumentException error = Assert.Throws<ArgumentException>(() => both.Validate(isBacktest: true));

        Assert.Equal(nameof(BotParameters.SessionRange), error.ParamName);
        Assert.Contains("one entry mode per instance", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new BotHarness().NewCore(both));
    }

    [Theory]
    [Trait("Req", "SIG-034")]
    [InlineData("RangeStartNy")]
    [InlineData("RangeEndNy")]
    [InlineData("LastEntryNy")]
    [InlineData("ExitNy")]
    [InlineData("MinWidthAtr")]
    [InlineData("StopAtrMult")]
    public void Validate_SessionRangeValueOutOfRange_RefusedWithItsName(string name)
    {
        SessionRangeParameters bad = name switch
        {
            "RangeStartNy" => new SessionRangeParameters { RangeStartNy = new TimeSpan(19, 5, 0) },
            "RangeEndNy" => new SessionRangeParameters { RangeEndNy = new TimeSpan(20, 0, 0) },
            "LastEntryNy" => new SessionRangeParameters { LastEntryNy = new TimeSpan(2, 0, 0) },
            "ExitNy" => new SessionRangeParameters { ExitNy = new TimeSpan(11, 0, 0) },
            "MinWidthAtr" => new SessionRangeParameters { MinWidthAtr = -0.5 },
            _ => new SessionRangeParameters { StopAtrMult = 0.0 },
        };
        BotParameters parameters = SessionRangeBotData.Parameters() with { SessionRange = bad };

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate(isBacktest: true));

        Assert.Equal(name, error.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BotHarness().NewCore(parameters));
    }
}
