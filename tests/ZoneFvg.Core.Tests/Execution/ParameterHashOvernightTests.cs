using System.Reflection;
using Xunit;
using Xunit.Sdk;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-043: in Overnight mode the parameter hash appends the seven Night values after every other
/// field, with no field for the mode, as in SessionRange mode; in the other modes nothing is
/// appended. Every expected hash was computed outside the code under test with Python's hashlib
/// (SHA-256 of the UTF-8 text, first eight hex characters), with the same script that reproduces
/// f681dff2, 3f0f30a3, 2dea1bc3, 3280d399 and 8655594c.
/// </summary>
public class ParameterHashOvernightTests
{
    private const string CanonicalDefaults =
        "BiasTimeFrame=D1;ZoneTimeFrame=H4;BiasEmaPeriod=50;AtrPeriod=14;SwingStrength=3;ImpulseAtrMult=2;"
        + "MaxImpulseBars=3;MaxBaseBars=3;RequireBos=true;MinZoneAtr=0.2;MaxZoneAtr=1.5;MaxZoneAgeBars=120;"
        + "ConfirmWindowBars=16;MinFvgAtr=0.25;EntryLevelPct=50;StopBufferAtr=0.1;RewardRisk=2;"
        + "SessionStartUtc=07:00:00;SessionEndUtc=17:00:00;FridayNoEntryUtc=12:00:00;FridayCloseUtc=20:00:00;"
        + "MaxSpreadPips=1.5;MinStopPips=12;MaxStopAtr=2;CostToRiskMax=0.1;MinFvgSpreadMult=3;MinRoomR=1.5;EntryExpiryBars=8;";

    private const string NightPart =
        "NightEntryNy=18:15:00;NightLatestNy=19:00:00;NightExitNy=02:45:00;NightEntryBufferAtr=0.03;NightStopAtr=1;NightTargetR=5;NightOrderMinutes=60;";

    private static IEnumerable<PropertyInfo> Properties()
    {
        return typeof(OvernightParameters).GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal);
    }

    public static IEnumerable<object[]> NightProperties()
    {
        return Properties().Select(property => new object[] { property.Name });
    }

    private static object Changed(PropertyInfo property, object value)
    {
        return value switch
        {
            int i => i + 1,
            double d => d + 0.125,
            TimeSpan t => t + TimeSpan.FromMinutes(15.0),
            _ => throw new XunitException("No change rule for " + property.Name + ": add one to this test."),
        };
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Canonical_WithDefaultOvernightParameters_AppendsTheNightPartAndHashesC13379a6()
    {
        string text = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), overnight: new OvernightParameters());

        Assert.Equal(CanonicalDefaults + NightPart, text);
        Assert.Equal("c13379a6", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), overnight: new OvernightParameters()));
        Assert.Equal("c13379a6", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null, null, null, new OvernightParameters()));
        Assert.DoesNotContain("EntryMode", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Compute_OvernightMinutes61_HashesA8d31fbb()
    {
        Assert.Equal("a8d31fbb", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), overnight: new OvernightParameters { NightOrderMinutes = 61 }));
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Compute_TheScenarioParameters_Hash76489619()
    {
        Assert.Equal(OvernightBotData.Hash, ParameterHash.Compute(new SignalParameters(), OvernightBotData.Execution(), overnight: new OvernightParameters()));
        Assert.Equal("76489619", OvernightBotData.Hash);
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Compute_GoldFileWithTheProfile_Hashes826c617fAtPip1And001()
    {
        foreach (double pip in new[] { 1.0, 0.01 })
        {
            SymbolProfileResult profile = SymbolProfile.Resolve("XAUUSD", "XAU", "USD", pip, 0.0, 0.0, 0.0, 0.0, 0.0);
            var execution = new ExecutionParameters { SessionStartUtc = TimeSpan.Zero, SessionEndUtc = TimeSpan.FromHours(24.0), MaxSpreadPips = profile.MaxSpreadPips, MinStopPips = profile.MinStopPips };

            Assert.Equal("826c617f", ParameterHash.Compute(new SignalParameters(), execution, null, null, null, profile, new OvernightParameters()));
        }
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Compute_WithoutOvernight_EveryOtherModeKeepsItsPinnedHash()
    {
        Assert.Equal("f681dff2", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters()));
        Assert.Equal("f681dff2", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), overnight: null));
        Assert.Equal("119a3864", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), new BreakoutParameters()));
        Assert.Equal("3f0f30a3", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, new SessionRangeParameters()));
        Assert.Equal("88051907", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null, new ChochParameters()));
        Assert.Equal(CanonicalDefaults, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters()));
    }

    [Fact]
    [Trait("Req", "SIG-043")]
    public void Canonical_HasOneEntryPerNightParameter()
    {
        string[] names = NightPart.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(entry => entry.Substring(0, entry.IndexOf('='))).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(Properties().Select(p => p.Name).ToArray(), names);
    }

    [Theory]
    [Trait("Req", "SIG-043")]
    [MemberData(nameof(NightProperties))]
    public void Canonical_EveryNightParameterChanged_ChangesTheTextAndTheHash(string name)
    {
        var defaults = new OvernightParameters();
        PropertyInfo property = typeof(OvernightParameters).GetProperty(name)!;
        OvernightParameters changed = defaults with { };
        property.SetValue(changed, Changed(property, property.GetValue(defaults)!));

        Assert.NotEqual(ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), overnight: defaults), ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), overnight: changed));
        Assert.NotEqual("c13379a6", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), overnight: changed));
        Assert.Contains(";" + name + "=", ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), overnight: changed), StringComparison.Ordinal);
    }
}
