using System.Globalization;
using System.Reflection;
using Xunit;
using Xunit.Sdk;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The parameter hash in the two entry modes (EXE-018 with EXE-008; architecture section 10, row
/// of 2026-11-22). Without breakout parameters the text and the hash are those of v0.9; with them
/// the breakout part is appended. The hash 119a3864 of the default breakout text was computed
/// outside the code under test, with Python's hashlib, from <see cref="BreakoutDefaults"/>.
/// </summary>
public class ParameterHashBreakoutTests
{
    private const string CanonicalDefaults =
        "BiasTimeFrame=D1;ZoneTimeFrame=H4;BiasEmaPeriod=50;AtrPeriod=14;SwingStrength=3;ImpulseAtrMult=2;"
        + "MaxImpulseBars=3;MaxBaseBars=3;RequireBos=true;MinZoneAtr=0.2;MaxZoneAtr=1.5;MaxZoneAgeBars=120;"
        + "ConfirmWindowBars=16;MinFvgAtr=0.25;EntryLevelPct=50;StopBufferAtr=0.1;RewardRisk=2;"
        + "SessionStartUtc=07:00:00;SessionEndUtc=17:00:00;FridayNoEntryUtc=12:00:00;FridayCloseUtc=20:00:00;"
        + "MaxSpreadPips=1.5;MinStopPips=12;MaxStopAtr=2;CostToRiskMax=0.1;MinFvgSpreadMult=3;MinRoomR=1.5;EntryExpiryBars=8;";

    private const string BreakoutPart =
        "EntryMode=Breakout;BreakoutEntryBufferAtr=0.1;BreakoutStopAtr=1;BreakoutWindowStartUtc=07:00:00;BreakoutWindowEndUtc=17:00:00;";

    private const string BreakoutDefaults = CanonicalDefaults + BreakoutPart;

    /// <summary>The name each breakout parameter has in the canonical text, written by hand.</summary>
    private static readonly IReadOnlyDictionary<string, string> CanonicalNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["EntryBufferAtr"] = "BreakoutEntryBufferAtr",
        ["StopAtrMult"] = "BreakoutStopAtr",
        ["WindowStartUtc"] = "BreakoutWindowStartUtc",
        ["WindowEndUtc"] = "BreakoutWindowEndUtc",
    };

    public static IEnumerable<object[]> BreakoutProperties()
    {
        return Properties().Select(property => new object[] { property.Name });
    }

    private static IEnumerable<PropertyInfo> Properties()
    {
        return typeof(BreakoutParameters).GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name, StringComparer.Ordinal);
    }

    private static object Changed(PropertyInfo property, object value)
    {
        return value switch
        {
            double d => d + 0.125,
            TimeSpan t => t + TimeSpan.FromMinutes(1.0),
            _ => throw new XunitException("No change rule for " + property.Name + " of type " + property.PropertyType.Name + ": add one to this test."),
        };
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void Canonical_WithoutBreakoutParameters_IsTheV09TextAndHash()
    {
        Assert.Equal(CanonicalDefaults, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null));
        Assert.Equal("f681dff2", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null));
        Assert.Equal("f681dff2", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters()));
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void Canonical_WithDefaultBreakoutParameters_AppendsTheBreakoutPartAndHashes119a3864()
    {
        Assert.Equal(BreakoutDefaults, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), new BreakoutParameters()));
        Assert.Equal("119a3864", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), new BreakoutParameters()));
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void Canonical_OtherBreakoutValues_WrittenInRoundTripAndConstantFormats()
    {
        var breakout = new BreakoutParameters { EntryBufferAtr = 0.25, StopAtrMult = 1.5, WindowStartUtc = new TimeSpan(7, 30, 0), WindowEndUtc = new TimeSpan(16, 45, 0) };

        string text = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), breakout);

        Assert.Equal(CanonicalDefaults + "EntryMode=Breakout;BreakoutEntryBufferAtr=0.25;BreakoutStopAtr=1.5;BreakoutWindowStartUtc=07:30:00;BreakoutWindowEndUtc=16:45:00;", text);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void Canonical_UnderACultureWithDecimalComma_StillUsesInvariantFormatting()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaCulture.NumberFormat.NumberDecimalSeparator = ",";
        commaCulture.DateTimeFormat.TimeSeparator = ".";
        try
        {
            CultureInfo.CurrentCulture = commaCulture;

            Assert.Equal(BreakoutDefaults, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), new BreakoutParameters()));
            Assert.Equal("119a3864", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), new BreakoutParameters()));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [Trait("Req", "EXE-018")]
    [MemberData(nameof(BreakoutProperties))]
    public void Canonical_EveryBreakoutParameterChanged_ChangesTheTextAndTheHash(string name)
    {
        var breakout = new BreakoutParameters();
        PropertyInfo property = typeof(BreakoutParameters).GetProperty(name)!;
        string before = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), breakout);
        string hashBefore = ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), breakout);

        property.SetValue(breakout, Changed(property, property.GetValue(breakout)!));

        Assert.True(CanonicalNames.ContainsKey(name), "no canonical name for " + name + ": add one to this test");
        Assert.Contains(CanonicalNames[name] + "=", before, StringComparison.Ordinal);
        Assert.NotEqual(before, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), breakout));
        Assert.NotEqual(hashBefore, ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), breakout));
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void Canonical_AppendedPartHasOneEntryPerBreakoutParameterPlusEntryMode()
    {
        string without = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null);
        string with = ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), new BreakoutParameters());

        Assert.StartsWith(without, with, StringComparison.Ordinal);
        string[] entries = with.Substring(without.Length)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Substring(0, entry.IndexOf('=')))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] expected = Properties().Select(p => CanonicalNames.TryGetValue(p.Name, out string? canonical) ? canonical : p.Name)
            .Append("EntryMode")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, entries);
    }

    [Fact]
    [Trait("Req", "EXE-018")]
    public void Canonical_NullSignalOrExecutionParameters_StillThrows()
    {
        Assert.Throws<ArgumentNullException>(() => ParameterHash.Canonical(null!, new ExecutionParameters(), new BreakoutParameters()));
        Assert.Throws<ArgumentNullException>(() => ParameterHash.Canonical(new SignalParameters(), null!, new BreakoutParameters()));
    }
}
