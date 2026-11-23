using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-035: <see cref="BotParameters.Choch"/> selects the Choch entry model of ZoneFvg mode. It is
/// refused together with Breakout or SessionRange (fail closed: the cBot's default EntryMode is
/// Breakout) and its values are checked; a valid value in ZoneFvg mode is accepted (WP37b). Null
/// keeps every mode as it was.
/// </summary>
public class BotParametersChochTests
{
    [Fact]
    [Trait("Req", "SIG-035")]
    public void Choch_DefaultIsNull_AndTheModesAreUnchanged()
    {
        Assert.Null(new BotParameters().Choch);
        Assert.Equal(EntryMode.ZoneFvg, RecoveryData.Parameters().Mode);
        Assert.Equal(EntryMode.Breakout, BreakoutRecoveryData.Parameters().Mode);
        Assert.Equal(EntryMode.SessionRange, SessionRangeBotData.Parameters().Mode);
        RecoveryData.Parameters().Validate(isBacktest: true);
        BreakoutRecoveryData.Parameters().Validate(isBacktest: true);
        SessionRangeBotData.Parameters().Validate(isBacktest: true);
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    public void Validate_ChochWithBreakout_RefusedNamingBothParameters()
    {
        BotParameters both = BreakoutRecoveryData.Parameters() with { Choch = new ChochParameters() };

        ArgumentException error = Assert.Throws<ArgumentException>(() => both.Validate(isBacktest: true));

        Assert.Equal(nameof(BotParameters.Choch), error.ParamName);
        Assert.Equal("EntryModel Choch needs EntryMode ZoneFvg, but EntryMode is Breakout (SIG-035). (Parameter 'Choch')", error.Message);
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    public void Validate_ChochWithSessionRange_RefusedNamingBothParameters()
    {
        BotParameters both = SessionRangeBotData.Parameters() with { Choch = new ChochParameters() };

        ArgumentException error = Assert.Throws<ArgumentException>(() => both.Validate(isBacktest: false));

        Assert.Equal(nameof(BotParameters.Choch), error.ParamName);
        Assert.Equal("EntryModel Choch needs EntryMode ZoneFvg, but EntryMode is SessionRange (SIG-035). (Parameter 'Choch')", error.Message);
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    public void Validate_ChochValueOutOfRange_RefusedWithTheValuesMessage()
    {
        BotParameters bad = RecoveryData.Parameters() with { Choch = new ChochParameters { ChochWindowBars = 97 } };

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => bad.Validate(isBacktest: true));

        Assert.Equal("ChochWindowBars", error.ParamName);
        Assert.Equal("ChochWindowBars must be a whole number from 4 to 96 but was 97. (Parameter 'ChochWindowBars')", error.Message);
    }

    [Fact]
    [Trait("Req", "SIG-035")]
    [Trait("Req", "REC-020")]
    public void Validate_ValidChochInZoneFvgMode_AcceptedLiveAndInABacktest_AndByTheConstructor()
    {
        // WP37b wires the execution layer, so the refusal of WP37a (prompt item 7) is removed.
        BotParameters choch = RecoveryData.Parameters() with { Choch = new ChochParameters() };

        choch.Validate(isBacktest: true);
        choch.Validate(isBacktest: false);

        Assert.Equal(EntryMode.ZoneFvg, choch.Mode);
        Assert.NotNull(new BotHarness().NewCore(choch));
    }
}
