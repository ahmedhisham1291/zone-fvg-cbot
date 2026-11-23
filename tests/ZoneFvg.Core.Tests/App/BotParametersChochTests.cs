using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-035: <see cref="BotParameters.Choch"/> selects the Choch entry model of ZoneFvg mode. It is
/// refused together with Breakout or SessionRange (fail closed: the cBot's default EntryMode is
/// Breakout), its values are checked, and until WP37b wires the execution layer every Choch value
/// is refused as not wired yet. Null keeps every mode as it was.
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
    public void Validate_ValidChochInZoneFvgMode_RefusedUntilTheExecutionLayerIsWired()
    {
        // WP37a: the execution layer cannot place a Choch signal yet, so the model must not start (fail closed).
        BotParameters choch = RecoveryData.Parameters() with { Choch = new ChochParameters() };

        ArgumentException error = Assert.Throws<ArgumentException>(() => choch.Validate(isBacktest: true));

        Assert.Equal(nameof(BotParameters.Choch), error.ParamName);
        Assert.Contains("not wired", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new BotHarness().NewCore(choch));
    }
}
