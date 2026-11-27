using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-032: <see cref="BotParameters.SymbolProfile"/> must agree with the execution, Choch and risk
/// parameters, and the start line of a currency pair with one value set by hand.
/// </summary>
public class BotParametersProfileTests
{
    private static BotParameters Gold()
    {
        return GoldProfileData.Parameters(0.01);
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void Validate_RecordAgreeing_Accepted()
    {
        Assert.Null(Record.Exception(() => Gold().Validate(isBacktest: true)));
    }

    public static IEnumerable<object[]> Disagreeing()
    {
        BotParameters gold = GoldProfileData.Parameters(0.01);
        yield return new object[] { "MaxSpreadPips", gold with { Execution = gold.Execution with { MaxSpreadPips = 1.5 } } };
        yield return new object[] { "MinStopPips", gold with { Execution = gold.Execution with { MinStopPips = 12.0 } } };
        yield return new object[] { "ChochMinStopPips", gold with { Choch = gold.Choch! with { ChochMinStopPips = 5.0 } } };
        yield return new object[] { "MaxVolumeUnits", gold with { Risk = gold.Risk with { MaxVolumeUnits = 3000000.0 } } };
    }

    [Theory]
    [Trait("Req", "EXE-032")]
    [MemberData(nameof(Disagreeing))]
    public void Validate_RecordDisagreeing_RefusedNamingSymbolProfile(string field, BotParameters parameters)
    {
        var exception = Assert.Throws<ArgumentException>(() => parameters.Validate(isBacktest: true));

        Assert.Equal("SymbolProfile", exception.ParamName);
        Assert.Contains(field, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void Validate_ChochMinStopOfTheRecordWithoutChoch_Accepted()
    {
        Assert.Null(Record.Exception(() => (Gold() with { Choch = null }).Validate(isBacktest: true)));
    }

    private static SymbolProfileResult UsdJpy(bool minStopByHand, bool allByHand = false)
    {
        SymbolProfileResult result = SymbolProfile.Resolve(
            "USDJPY", "USD", "JPY", 0.01, 0.0,
            allByHand ? 1.5 : 0.0,
            minStopByHand || allByHand ? 8.0 : 0.0,
            allByHand ? 5.0 : 0.0,
            allByHand ? 3000000.0 : 0.0);
        return result;
    }

    private static BotParameters UsdJpyParameters(SymbolProfileResult profile)
    {
        BotParameters reference = RecoveryData.Parameters();
        return reference with
        {
            Execution = reference.Execution with { MaxSpreadPips = profile.MaxSpreadPips, MinStopPips = profile.MinStopPips },
            Risk = reference.Risk with { MaxVolumeUnits = profile.MaxVolumeUnits },
            SymbolProfile = profile,
        };
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void StartLine_ForexWithMinStopSetByHand_EndsWithTheProfile()
    {
        var h = new BotHarness();
        h.Start(RecoveryData.Activation, UsdJpyParameters(UsdJpy(minStopByHand: true)));

        Assert.EndsWith(
            " profile=Fx pipSize=0.01 maxSpreadPips=1.5 minStopPips=8 chochMinStopPips=5 maxVolumeUnits=3000000 auto=MaxSpreadPips,ChochMinStopPips,MaxVolumeUnits brake=off",
            Assert.Single(h.Journal.Lines("start")),
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void StartLine_ForexAllSetByHand_AutoNone()
    {
        var h = new BotHarness();
        h.Start(RecoveryData.Activation, UsdJpyParameters(UsdJpy(minStopByHand: false, allByHand: true)));

        Assert.EndsWith(" maxVolumeUnits=3000000 auto=none brake=off", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void StartLine_RecoveryPlan_SizingBeforeProfile()
    {
        var h = new BotHarness();
        BotParameters parameters = UsdJpyParameters(UsdJpy(minStopByHand: true));
        h.Start(RecoveryData.Activation, parameters with { Risk = parameters.Risk with { SizingPlan = ZoneFvg.Core.Risk.SizingPlan.Recovery } });

        Assert.Contains(" sizing=Recovery profile=Fx pipSize=0.01 ", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void StartLine_WithoutRecord_HasNoProfile()
    {
        var h = new BotHarness();
        h.Start(RecoveryData.Activation);

        Assert.DoesNotContain("profile=", Assert.Single(h.Journal.Lines("start")), StringComparison.Ordinal);
    }
}
