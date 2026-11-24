using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// EXE-032: an automatic MaxSpreadPips, MinStopPips or ChochMinStopPips is written into the hash
/// text as the profile's value in its own unit, so gold gives the same hash at a pip of 1 and of
/// 0.01; values set by hand are written as before. Every expected hash was computed outside the
/// code under test with Python's hashlib (SHA-256 of the UTF-8 canonical text, first eight hex
/// characters), from the texts written here by hand.
/// </summary>
public class ParameterHashProfileTests
{
    private const string Head =
        "BiasTimeFrame=D1;ZoneTimeFrame=H4;BiasEmaPeriod=50;AtrPeriod=14;SwingStrength=3;ImpulseAtrMult=2;"
        + "MaxImpulseBars=3;MaxBaseBars=3;RequireBos=true;MinZoneAtr=0.2;MaxZoneAtr=1.5;MaxZoneAgeBars=120;"
        + "ConfirmWindowBars=16;MinFvgAtr=0.25;EntryLevelPct=50;StopBufferAtr=0.1;RewardRisk=2;"
        + "SessionStartUtc=07:00:00;SessionEndUtc=17:00:00;FridayNoEntryUtc=12:00:00;FridayCloseUtc=20:00:00;";

    private const string Tail = "MaxStopAtr=2;CostToRiskMax=0.1;MinFvgSpreadMult=3;MinRoomR=1.5;EntryExpiryBars=8;";

    private static string Choch(string minStop)
    {
        return "EntryModel=Choch;RequireSweep=false;ChochWindowBars=32;ChochUseBias=false;ChochStopAtDistal=false;ChochTargetBufferAtr=0.1;ChochMinStopPips=" + minStop + ";MinRewardRisk=2;";
    }

    internal static SymbolProfileResult GoldAuto(double pip)
    {
        return new SymbolProfileResult
        {
            Profile = SymbolProfileKind.Gold,
            PipSize = pip,
            MaxSpreadPips = 0.4 / pip,
            MinStopPips = 8.0 / pip,
            ChochMinStopPips = 3.0 / pip,
            MaxVolumeUnits = 1000.0,
            MaxSpreadPipsAuto = true,
            MinStopPipsAuto = true,
            ChochMinStopPipsAuto = true,
            MaxVolumeUnitsAuto = true,
        };
    }

    private static SymbolProfileResult FxAuto()
    {
        return new SymbolProfileResult
        {
            Profile = SymbolProfileKind.Fx,
            PipSize = 0.0001,
            MaxSpreadPips = 1.5,
            MinStopPips = 12.0,
            ChochMinStopPips = 5.0,
            MaxVolumeUnits = 3000000.0,
            MaxSpreadPipsAuto = true,
            MinStopPipsAuto = true,
            ChochMinStopPipsAuto = true,
            MaxVolumeUnitsAuto = true,
        };
    }

    private static ExecutionParameters Execution(SymbolProfileResult profile)
    {
        return new ExecutionParameters { MaxSpreadPips = profile.MaxSpreadPips, MinStopPips = profile.MinStopPips };
    }

    private static ChochParameters ChochOf(SymbolProfileResult profile)
    {
        return new ChochParameters { ChochMinStopPips = profile.ChochMinStopPips };
    }

    [Theory]
    [Trait("Req", "EXE-032")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void Canonical_GoldAutomaticChoch_TheSameTextAndHashAtEveryPipSize(double pip)
    {
        SymbolProfileResult profile = GoldAuto(pip);
        string expected = Head + "MaxSpreadPips=auto:0.4USD;MinStopPips=auto:8USD;" + Tail + Choch("auto:3USD");

        Assert.Equal(expected, ParameterHash.Canonical(new SignalParameters(), Execution(profile), null, null, ChochOf(profile), profile));
        Assert.Equal("a2eedc3a", ParameterHash.Compute(new SignalParameters(), Execution(profile), null, null, ChochOf(profile), profile));
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void Canonical_ForexAutomaticChoch_WritesPips()
    {
        SymbolProfileResult profile = FxAuto();
        string expected = Head + "MaxSpreadPips=auto:1.5pips;MinStopPips=auto:12pips;" + Tail + Choch("auto:5pips");

        Assert.Equal(expected, ParameterHash.Canonical(new SignalParameters(), Execution(profile), null, null, ChochOf(profile), profile));
        Assert.Equal("7516cd20", ParameterHash.Compute(new SignalParameters(), Execution(profile), null, null, ChochOf(profile), profile));
    }

    [Theory]
    [Trait("Req", "EXE-032")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void Canonical_GoldAutomaticFvgModel_NoChochMinStop(double pip)
    {
        SymbolProfileResult profile = GoldAuto(pip);

        string text = ParameterHash.Canonical(new SignalParameters(), Execution(profile), null, null, null, profile);

        Assert.Equal(Head + "MaxSpreadPips=auto:0.4USD;MinStopPips=auto:8USD;" + Tail, text);
        Assert.DoesNotContain("ChochMinStopPips", text, StringComparison.Ordinal);
        Assert.Equal("8655594c", ParameterHash.Compute(new SignalParameters(), Execution(profile), null, null, null, profile));
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void Canonical_GoldAutomaticBreakout_TheTwoPipFieldsAsAuto()
    {
        SymbolProfileResult profile = GoldAuto(0.01);

        string text = ParameterHash.Canonical(new SignalParameters(), Execution(profile), new BreakoutParameters(), null, null, profile);

        Assert.Contains(";MaxSpreadPips=auto:0.4USD;MinStopPips=auto:8USD;", text, StringComparison.Ordinal);
        Assert.Contains("EntryMode=Breakout;", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void Compute_AllValuesSetByHand_TheExistingPinnedHash()
    {
        SymbolProfileResult byHand = FxAuto() with { MaxSpreadPipsAuto = false, MinStopPipsAuto = false, ChochMinStopPipsAuto = false, MaxVolumeUnitsAuto = false };

        Assert.Equal("88051907", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null, new ChochParameters(), byHand));
        Assert.Equal("f681dff2", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null, null, byHand));
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void Compute_OnlyMaxVolumeUnitsAutomatic_TheExistingPinnedHash()
    {
        SymbolProfileResult volumeOnly = FxAuto() with { MaxSpreadPipsAuto = false, MinStopPipsAuto = false, ChochMinStopPipsAuto = false };

        Assert.Equal("88051907", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null, new ChochParameters(), volumeOnly));
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void Compute_ProfileNone_WritesTheNumbers()
    {
        SymbolProfileResult none = FxAuto() with { Profile = SymbolProfileKind.None };

        Assert.Equal("88051907", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null, new ChochParameters(), none));
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void Compute_WithoutARecord_Unchanged()
    {
        Assert.Equal("88051907", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null, new ChochParameters(), null));
    }

    [Fact]
    [Trait("Req", "EXE-032")]
    public void HashText_OtherName_Refused()
    {
        Assert.Throws<ArgumentException>(() => FxAuto().HashText("MaxVolumeUnits"));
        Assert.Throws<ArgumentException>(() => SymbolProfileValues.Of(SymbolProfileKind.Gold, "RiskPercent"));
        Assert.True(double.IsNaN(SymbolProfileValues.Of(SymbolProfileKind.None, "MaxSpreadPips")));
    }
}
