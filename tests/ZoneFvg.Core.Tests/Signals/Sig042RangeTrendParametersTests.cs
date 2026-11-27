using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-042 (spec 3.8, row AY of v0.17): the parameter RangeTrendFastEma of
/// <see cref="SessionRangeParameters"/>, its own check (0 or at least 2), the check against
/// BiasEmaPeriod in <see cref="BotParameters.Validate"/>, the cBot's text path
/// <see cref="SessionRangeText"/>, the direction rule of the engine on plain numbers, and the proof
/// that the other modes cannot carry the value.
/// </summary>
public class Sig042RangeTrendParametersTests
{
    private static BotParameters WithFast(int fast, int biasEmaPeriod = 50)
    {
        BotParameters parameters = SessionRangeBotData.Parameters();
        return parameters with
        {
            Signal = parameters.Signal with { BiasEmaPeriod = biasEmaPeriod },
            SessionRange = new SessionRangeParameters { RangeTrendFastEma = fast },
        };
    }

    // ------------------------------------------------------------------ the record

    [Fact]
    [Trait("Req", "SIG-042")]
    public void RangeTrendFastEma_CoreDefault_IsZero()
    {
        Assert.Equal(0, new SessionRangeParameters().RangeTrendFastEma);
        Assert.Equal(0, SessionRangeText.Build("19:00", "03:00", "12:00", "16:30", 0.5, 1.0).RangeTrendFastEma);
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(int.MinValue)]
    public void Validate_NegativeOrOne_RefusedNamingRangeTrendFastEma(int fast)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SessionRangeParameters { RangeTrendFastEma = fast }.Validate());

        Assert.Equal("RangeTrendFastEma", exception.ParamName);
        Assert.StartsWith("RangeTrendFastEma must be 0 (off) or at least 2 but was " + Invariant.Text(fast) + ".", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(20)]
    [InlineData(10000)]
    public void Validate_ZeroOrTwoAndAbove_Accepted(int fast)
    {
        Assert.Null(Record.Exception(() => new SessionRangeParameters { RangeTrendFastEma = fast }.Validate()));
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Validate_AnotherValueOutOfRangeToo_TheEarlierParameterIsNamedFirst()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SessionRangeParameters { StopAtrMult = 0.0, RangeTrendFastEma = 1 }.Validate());

        Assert.Equal("StopAtrMult", exception.ParamName);
    }

    // ------------------------------------------------------------------ against BiasEmaPeriod

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(20)]
    [InlineData(49)]
    public void BotParametersValidate_BiasEmaPeriod50_ZeroOrBelow50Accepted(int fast)
    {
        Assert.Null(Record.Exception(() => WithFast(fast).Validate(isBacktest: true)));
        Assert.Null(Record.Exception(() => WithFast(fast).Validate(isBacktest: false)));
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(50)]
    [InlineData(51)]
    [InlineData(200)]
    public void BotParametersValidate_BiasEmaPeriod50_50AndAboveRefusedNamingRangeTrendFastEma(int fast)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WithFast(fast).Validate(isBacktest: true));

        Assert.Equal("RangeTrendFastEma", exception.ParamName);
        Assert.StartsWith("RangeTrendFastEma must be 0 (off) or below BiasEmaPeriod (50) but was " + Invariant.Text(fast) + " (SIG-042).", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BotHarness().NewCore(WithFast(fast)));
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(-1)]
    [InlineData(1)]
    public void BotParametersValidate_NegativeOrOne_RefusedNamingRangeTrendFastEma(int fast)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WithFast(fast).Validate(isBacktest: true));

        Assert.Equal("RangeTrendFastEma", exception.ParamName);
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(20, 21, true)]
    [InlineData(20, 20, false)]
    [InlineData(2, 3, true)]
    [InlineData(2, 2, false)]
    public void BotParametersValidate_OtherBiasEmaPeriods_TheFastPeriodMustBeBelowIt(int fast, int biasEmaPeriod, bool accepted)
    {
        Exception? error = Record.Exception(() => WithFast(fast, biasEmaPeriod).Validate(isBacktest: true));

        Assert.Equal(accepted, error is null);
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void BotParametersValidate_ZeroWithAnyBiasEmaPeriod_Accepted()
    {
        Assert.Null(Record.Exception(() => WithFast(0, 2).Validate(isBacktest: true)));
        Assert.Null(Record.Exception(() => WithFast(0, 1).Validate(isBacktest: true)));
    }

    // ------------------------------------------------------------------ the cBot's text path

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(-1)]
    [InlineData(1)]
    public void SessionRangeTextBuild_NegativeOrOne_RefusedNamingRangeTrendFastEma(int fast)
    {
        ArgumentException exception = Assert.ThrowsAny<ArgumentException>(() => SessionRangeText.Build("19:00", "03:00", "12:00", "16:30", 0.5, 1.0, fast));

        Assert.Equal("RangeTrendFastEma", exception.ParamName);
        Assert.Equal("RangeTrendFastEma must be 0 (off) or at least 2 but was " + Invariant.Text(fast) + ". (Parameter 'RangeTrendFastEma')", exception.Message);
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(20)]
    public void SessionRangeTextBuild_ZeroOrTwoAndAbove_CarriesTheValueAndTheSixOthers(int fast)
    {
        SessionRangeParameters built = SessionRangeText.Build("19:00", "03:00", "12:00", "16:30", 0.5, 1.0, fast);

        Assert.Equal(new SessionRangeParameters { RangeTrendFastEma = fast }, built);
        Assert.Equal("RangeTrendFastEma", SessionRangeText.CbotName("RangeTrendFastEma"));
    }

    // ------------------------------------------------------------------ the other modes cannot carry it

    [Fact]
    [Trait("Req", "SIG-042")]
    public void OtherModes_TheValueLivesOnlyInTheSessionRangeParameters_WhichTheyRefuse()
    {
        // Breakout and the Choch model of ZoneFvg are refused together with session range parameters,
        // so RangeTrendFastEma 20 cannot reach an instance of another mode.
        BotParameters breakout = WithFast(20) with { Breakout = new BreakoutParameters() };
        BotParameters choch = WithFast(20) with { Choch = new ChochParameters() };

        Assert.Equal(nameof(BotParameters.SessionRange), Assert.Throws<ArgumentException>(() => breakout.Validate(isBacktest: true)).ParamName);
        Assert.Equal(nameof(BotParameters.Choch), Assert.Throws<ArgumentException>(() => choch.Validate(isBacktest: true)).ParamName);
        Assert.Equal(EntryMode.ZoneFvg, (WithFast(20) with { SessionRange = null }).Mode);
        Assert.Throws<ArgumentException>(() => WarmupCalculator.For(new SignalParameters(), new BreakoutParameters(), new SessionRangeParameters { RangeTrendFastEma = 20 }));
    }

    // ------------------------------------------------------------------ the direction rule on plain numbers

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(0.0, 0.0, Bias.Neutral)]
    [InlineData(1e-9, 0.0, Bias.Neutral)] // exactly one tolerance above: equal
    [InlineData(-1e-9, 0.0, Bias.Neutral)] // exactly one tolerance below: equal
    [InlineData(0.5e-9, 0.0, Bias.Neutral)]
    [InlineData(-0.5e-9, 0.0, Bias.Neutral)]
    [InlineData(1.5e-9, 0.0, Bias.Long)] // more than one tolerance above
    [InlineData(-1.5e-9, 0.0, Bias.Short)] // more than one tolerance below
    [InlineData(2000.01, 2000.0, Bias.Long)]
    [InlineData(1999.99, 2000.0, Bias.Short)]
    [InlineData(2000.0, 2000.0, Bias.Neutral)]
    public void Direction_FastAgainstSlow_LongAboveShortBelowNeutralWithinTheTolerance(double fast, double slow, Bias expected)
    {
        Assert.Equal(expected, SignalEngine.Direction(fast, slow));
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Direction_EitherEmaMissing_Neutral()
    {
        Assert.Equal(Bias.Neutral, SignalEngine.Direction(null, 2000.0));
        Assert.Equal(Bias.Neutral, SignalEngine.Direction(2000.0, null));
        Assert.Equal(Bias.Neutral, SignalEngine.Direction(null, null));
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Warmup_RangeTrendFastEmaBelowBiasEmaPeriod_TheD1RequirementIsUnchanged()
    {
        // D1 = 4 x BiasEmaPeriod + 1 = 201 also covers the 80 bars of EMA(20).
        IReadOnlyDictionary<TimeFrame, int> off = WarmupCalculator.For(new SignalParameters(), null, new SessionRangeParameters());
        IReadOnlyDictionary<TimeFrame, int> on = WarmupCalculator.For(new SignalParameters(), null, new SessionRangeParameters { RangeTrendFastEma = 49 });

        Assert.Equal(off.ToArray(), on.ToArray());
        Assert.Equal(201, on[TimeFrame.D1]);
        Assert.True(on[TimeFrame.D1] >= (4 * 49) + 1);
    }
}
