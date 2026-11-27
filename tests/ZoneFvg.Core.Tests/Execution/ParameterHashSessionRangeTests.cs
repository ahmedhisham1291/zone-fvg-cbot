using System.Globalization;
using System.Reflection;
using Xunit;
using Xunit.Sdk;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-034 (prompt WP34, item 5): in SessionRange mode the parameter hash appends the six session
/// range values, under the names of spec section 7, after the existing fields; in the other modes
/// nothing is appended. The hash 3f0f30a3 of the default text and 70a33698 of the text with
/// RangeMinWidthAtr 0 were computed outside the code under test, with Python's hashlib.
/// </summary>
public class ParameterHashSessionRangeTests
{
    private const string CanonicalDefaults =
        "BiasTimeFrame=D1;ZoneTimeFrame=H4;BiasEmaPeriod=50;AtrPeriod=14;SwingStrength=3;ImpulseAtrMult=2;"
        + "MaxImpulseBars=3;MaxBaseBars=3;RequireBos=true;MinZoneAtr=0.2;MaxZoneAtr=1.5;MaxZoneAgeBars=120;"
        + "ConfirmWindowBars=16;MinFvgAtr=0.25;EntryLevelPct=50;StopBufferAtr=0.1;RewardRisk=2;"
        + "SessionStartUtc=07:00:00;SessionEndUtc=17:00:00;FridayNoEntryUtc=12:00:00;FridayCloseUtc=20:00:00;"
        + "MaxSpreadPips=1.5;MinStopPips=12;MaxStopAtr=2;CostToRiskMax=0.1;MinFvgSpreadMult=3;MinRoomR=1.5;EntryExpiryBars=8;";

    private const string RangePart =
        "RangeStartNy=19:00:00;RangeEndNy=03:00:00;RangeLastEntryNy=12:00:00;RangeExitNy=16:30:00;RangeMinWidthAtr=0.5;RangeStopAtr=1;";

    /// <summary>The name each session range parameter has in the canonical text, written by hand from spec section 7.</summary>
    private static readonly IReadOnlyDictionary<string, string> CanonicalNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ExitNy"] = "RangeExitNy",
        ["LastEntryNy"] = "RangeLastEntryNy",
        ["MinWidthAtr"] = "RangeMinWidthAtr",
        ["RangeEndNy"] = "RangeEndNy",
        ["RangeStartNy"] = "RangeStartNy",
        ["RangeTrendFastEma"] = "RangeTrendFastEma",
        ["StopAtrMult"] = "RangeStopAtr",
    };

    public static IEnumerable<object[]> RangeProperties()
    {
        return Properties().Select(property => new object[] { property.Name });
    }

    private static IEnumerable<PropertyInfo> Properties()
    {
        return typeof(SessionRangeParameters).GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal);
    }

    private static object Changed(PropertyInfo property, object value)
    {
        return value switch
        {
            int i => i + 1,
            double d => d + 0.125,
            TimeSpan t => t + TimeSpan.FromMinutes(15.0),
            _ => throw new XunitException("No change rule for " + property.Name + " of type " + property.PropertyType.Name + ": add one to this test."),
        };
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Canonical_WithDefaultSessionRangeParameters_AppendsTheRangePartAndHashes3f0f30a3()
    {
        Assert.Equal(CanonicalDefaults + RangePart, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, new SessionRangeParameters()));
        Assert.Equal("3f0f30a3", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, new SessionRangeParameters()));
        Assert.Equal("3f0f30a3", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), sessionRange: new SessionRangeParameters()));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Canonical_WithoutSessionRangeParameters_TheOtherModesKeepTheirTextAndHash()
    {
        Assert.Equal(CanonicalDefaults, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, null));
        Assert.Equal("f681dff2", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, null));
        Assert.Equal("119a3864", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), new BreakoutParameters(), null));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Canonical_OtherRangeValues_WrittenInConstantAndRoundTripFormats()
    {
        var range = new SessionRangeParameters
        {
            RangeStartNy = new TimeSpan(18, 45, 0),
            RangeEndNy = new TimeSpan(2, 30, 0),
            LastEntryNy = new TimeSpan(11, 15, 0),
            ExitNy = new TimeSpan(16, 0, 0),
            MinWidthAtr = 0.0,
            StopAtrMult = 1.25,
        };

        string text = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, range);

        Assert.Equal(CanonicalDefaults + "RangeStartNy=18:45:00;RangeEndNy=02:30:00;RangeLastEntryNy=11:15:00;RangeExitNy=16:00:00;RangeMinWidthAtr=0;RangeStopAtr=1.25;", text);
        Assert.Equal("70a33698", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, new SessionRangeParameters { MinWidthAtr = 0.0 }));
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Canonical_UnderACultureWithDecimalComma_StillUsesInvariantFormatting()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaCulture.NumberFormat.NumberDecimalSeparator = ",";
        commaCulture.DateTimeFormat.TimeSeparator = ".";
        try
        {
            CultureInfo.CurrentCulture = commaCulture;

            Assert.Equal(CanonicalDefaults + RangePart, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, new SessionRangeParameters()));
            Assert.Equal("3f0f30a3", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, new SessionRangeParameters()));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Req", "SIG-034")]
    public void Canonical_EverySessionRangeProperty_HasItsNamedEntry()
    {
        string[] names = Properties().Select(property => property.Name).ToArray();

        Assert.Equal(CanonicalNames.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray(), names);
        string text = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, new SessionRangeParameters());
        foreach (string name in CanonicalNames.Values.Where(name => name != "RangeTrendFastEma"))
        {
            Assert.Contains(";" + name + "=", text, StringComparison.Ordinal);
        }

        // SIG-042 (row AY): RangeTrendFastEma is written only when it is not 0, so it is checked in the text with 20.
        string withFast = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, new SessionRangeParameters { RangeTrendFastEma = 20 });
        Assert.Contains(";RangeTrendFastEma=", withFast, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIG-034")]
    [MemberData(nameof(RangeProperties))]
    public void Canonical_EverySessionRangeValueChanged_ChangesItsEntryAndTheHash(string name)
    {
        var range = new SessionRangeParameters();
        PropertyInfo property = typeof(SessionRangeParameters).GetProperty(name)!;
        string before = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, range);
        string hashBefore = ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, range);

        property.SetValue(range, Changed(property, property.GetValue(range)!));

        string after = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, range);
        Assert.NotEqual(before, after);
        Assert.StartsWith(CanonicalDefaults, after, StringComparison.Ordinal);
        Assert.NotEqual(hashBefore, ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, range));
    }
}
