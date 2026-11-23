using System.Globalization;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-020: in the Choch model the parameter hash appends, after every existing field,
/// EntryModel=Choch and the seven Choch parameters in the formats the hash already uses; without
/// Choch parameters the text and hash of every other model and mode stay what they were. Every
/// expected hash was computed outside the code under test with Python's hashlib (SHA-256 of the
/// UTF-8 canonical text, first eight hex characters).
/// </summary>
public class ParameterHashChochTests
{
    private const string CanonicalDefaults =
        "BiasTimeFrame=D1;ZoneTimeFrame=H4;BiasEmaPeriod=50;AtrPeriod=14;SwingStrength=3;ImpulseAtrMult=2;"
        + "MaxImpulseBars=3;MaxBaseBars=3;RequireBos=true;MinZoneAtr=0.2;MaxZoneAtr=1.5;MaxZoneAgeBars=120;"
        + "ConfirmWindowBars=16;MinFvgAtr=0.25;EntryLevelPct=50;StopBufferAtr=0.1;RewardRisk=2;"
        + "SessionStartUtc=07:00:00;SessionEndUtc=17:00:00;FridayNoEntryUtc=12:00:00;FridayCloseUtc=20:00:00;"
        + "MaxSpreadPips=1.5;MinStopPips=12;MaxStopAtr=2;CostToRiskMax=0.1;MinFvgSpreadMult=3;MinRoomR=1.5;EntryExpiryBars=8;";

    private const string ChochPart =
        "EntryModel=Choch;RequireSweep=false;ChochWindowBars=32;ChochUseBias=false;ChochStopAtDistal=false;"
        + "ChochTargetBufferAtr=0.1;ChochMinStopPips=5;MinRewardRisk=2;";

    [Fact]
    [Trait("Req", "REC-020")]
    public void Canonical_DefaultChoch_AppendsTheChochPartAndHashes88051907()
    {
        Assert.Equal(CanonicalDefaults + ChochPart, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, null, new ChochParameters()));
        Assert.Equal("88051907", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null, new ChochParameters()));
    }

    [Fact]
    [Trait("Req", "REC-020")]
    public void Canonical_WithoutChoch_TheFvgModelKeepsItsTextAndHash()
    {
        Assert.Equal(CanonicalDefaults, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, null, null));
        Assert.Equal("f681dff2", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters()));
        Assert.Equal(BreakoutRecoveryData.Hash, ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), new BreakoutParameters(), null, null));
    }

    [Fact]
    [Trait("Req", "REC-020")]
    public void Canonical_AllDaySessionWithChoch_HashesTo0df0b3e7()
    {
        var allDay = new ExecutionParameters { SessionStartUtc = TimeSpan.Zero, SessionEndUtc = TimeSpan.FromHours(24.0) };

        string text = ParameterHash.Canonical(new SignalParameters(), allDay, null, null, new ChochParameters());

        Assert.EndsWith("EntryExpiryBars=8;" + ChochPart, text, StringComparison.Ordinal);
        Assert.Equal("0df0b3e7", ParameterHash.Compute(new SignalParameters(), allDay, null, null, new ChochParameters()));
    }

    [Fact]
    [Trait("Req", "REC-020")]
    public void Canonical_GoldValues_HashToAc941dbe()
    {
        var gold = new ExecutionParameters { MinStopPips = 8.0, MaxSpreadPips = 0.4 };

        Assert.Equal("ac941dbe", ParameterHash.Compute(new SignalParameters(), gold, null, null, new ChochParameters { ChochMinStopPips = 3.0 }));
    }

    [Fact]
    [Trait("Req", "REC-020")]
    public void Canonical_OtherValues_WrittenInTheHashFormats()
    {
        var choch = new ChochParameters { RequireSweep = true, ChochWindowBars = 48, ChochUseBias = true, ChochStopAtDistal = true, ChochTargetBufferAtr = 0.25, ChochMinStopPips = 3.5, MinRewardRisk = 1.5 };

        string text = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, null, choch);

        Assert.Equal(CanonicalDefaults + "EntryModel=Choch;RequireSweep=true;ChochWindowBars=48;ChochUseBias=true;ChochStopAtDistal=true;ChochTargetBufferAtr=0.25;ChochMinStopPips=3.5;MinRewardRisk=1.5;", text);
    }

    public static IEnumerable<object[]> Changes()
    {
        yield return new object[] { "RequireSweep", new ChochParameters { RequireSweep = true } };
        yield return new object[] { "ChochWindowBars", new ChochParameters { ChochWindowBars = 33 } };
        yield return new object[] { "ChochUseBias", new ChochParameters { ChochUseBias = true } };
        yield return new object[] { "ChochStopAtDistal", new ChochParameters { ChochStopAtDistal = true } };
        yield return new object[] { "ChochTargetBufferAtr", new ChochParameters { ChochTargetBufferAtr = 0.2 } };
        yield return new object[] { "ChochMinStopPips", new ChochParameters { ChochMinStopPips = 6.0 } };
        yield return new object[] { "MinRewardRisk", new ChochParameters { MinRewardRisk = 2.5 } };
    }

    [Theory]
    [Trait("Req", "REC-020")]
    [MemberData(nameof(Changes))]
    public void Compute_EveryChochValueChanged_ChangesTheHash(string name, ChochParameters changed)
    {
        string before = ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null, new ChochParameters());

        Assert.NotEqual(before, ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null, changed));
        Assert.Contains(name + "=", ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, null, changed), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "REC-020")]
    public void Canonical_UnderACultureWithDecimalComma_StillInvariant()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(CanonicalDefaults + ChochPart, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, null, new ChochParameters()));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    [Trait("Req", "REC-020")]
    public void BotCore_StartLine_EndsWithModeZoneFvgModelChoch_FvgLineUnchanged()
    {
        var choch = new BotHarness(ChochBotData.Bars(), ChochBotData.Broker(ChochBotData.Start));
        choch.Start(ChochBotData.Start, ChochBotData.Parameters());
        var fvg = new BotHarness();
        fvg.Start(RecoveryData.Activation);

        string start = Assert.Single(choch.Journal.Lines("start"));
        Assert.Contains(" hash=" + ChochBotData.Hash + " ", start, StringComparison.Ordinal);
        Assert.EndsWith(" mode=ZoneFvg model=Choch", start, StringComparison.Ordinal);
        Assert.DoesNotContain(" mode=", Assert.Single(fvg.Journal.Lines("start")), StringComparison.Ordinal);
        Assert.DoesNotContain(" model=", Assert.Single(fvg.Journal.Lines("start")), StringComparison.Ordinal);
    }
}
