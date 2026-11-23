using System.Globalization;
using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The parameter hash with the breakout filters (SIG-028, SIG-029; architecture section 10, row
/// of 2026-11-23). TrendSlowEmaPeriod and BreakoutLastConfirmUtc are appended after the breakout
/// part, in this order, only when they differ from their off values (0 and the window end), so a
/// configuration without the filters keeps its hash. The expected hashes were computed outside the
/// code under test, with Python's hashlib (SHA-256 of the UTF-8 text, first 8 hex characters):
/// 119a3864 for the breakout defaults and 8aa3f28a with TrendSlowEmaPeriod 200 and 08:00.
/// </summary>
public class ParameterHashBreakoutFilterTests
{
    private const string BreakoutDefaults =
        "BiasTimeFrame=D1;ZoneTimeFrame=H4;BiasEmaPeriod=50;AtrPeriod=14;SwingStrength=3;ImpulseAtrMult=2;"
        + "MaxImpulseBars=3;MaxBaseBars=3;RequireBos=true;MinZoneAtr=0.2;MaxZoneAtr=1.5;MaxZoneAgeBars=120;"
        + "ConfirmWindowBars=16;MinFvgAtr=0.25;EntryLevelPct=50;StopBufferAtr=0.1;RewardRisk=2;"
        + "SessionStartUtc=07:00:00;SessionEndUtc=17:00:00;FridayNoEntryUtc=12:00:00;FridayCloseUtc=20:00:00;"
        + "MaxSpreadPips=1.5;MinStopPips=12;MaxStopAtr=2;CostToRiskMax=0.1;MinFvgSpreadMult=3;MinRoomR=1.5;EntryExpiryBars=8;"
        + "EntryMode=Breakout;BreakoutEntryBufferAtr=0.1;BreakoutStopAtr=1;BreakoutWindowStartUtc=07:00:00;BreakoutWindowEndUtc=17:00:00;";

    private static readonly BreakoutParameters CBotDefaults = new() { TrendSlowEmaPeriod = 200, LastConfirmUtc = TimeSpan.FromHours(8.0) };

    private static string Canonical(BreakoutParameters breakout)
    {
        return ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), breakout);
    }

    private static string Hash(BreakoutParameters breakout)
    {
        return ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), breakout);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    [Trait("Req", "SIG-029")]
    public void Canonical_OffValuesSetExplicitly_KeepTheOldTextAndHash119a3864()
    {
        var off = new BreakoutParameters { TrendSlowEmaPeriod = 0, LastConfirmUtc = TimeSpan.FromHours(17.0) };

        Assert.Equal(BreakoutDefaults, Canonical(off));
        Assert.Equal(BreakoutDefaults, Canonical(new BreakoutParameters()));
        Assert.Equal("119a3864", Hash(off));
        Assert.Equal("f681dff2", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null));
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void Canonical_LastConfirmEqualToAMovedWindowEnd_IsNotWritten()
    {
        var breakout = new BreakoutParameters { WindowEndUtc = new TimeSpan(16, 30, 0), LastConfirmUtc = new TimeSpan(16, 30, 0) };

        string text = Canonical(breakout);

        Assert.EndsWith("BreakoutWindowEndUtc=16:30:00;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("BreakoutLastConfirmUtc", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    public void Canonical_TrendSlowEmaPeriod200_IsAppendedAfterTheBreakoutPart()
    {
        Assert.Equal(BreakoutDefaults + "TrendSlowEmaPeriod=200;", Canonical(new BreakoutParameters { TrendSlowEmaPeriod = 200 }));
        Assert.Equal(BreakoutDefaults + "TrendSlowEmaPeriod=100;", Canonical(new BreakoutParameters { TrendSlowEmaPeriod = 100 }));
        Assert.Equal("6146550b", Hash(new BreakoutParameters { TrendSlowEmaPeriod = 200 }));
    }

    [Fact]
    [Trait("Req", "SIG-029")]
    public void Canonical_LastConfirm0800_IsAppendedInTheConstantFormat()
    {
        Assert.Equal(BreakoutDefaults + "BreakoutLastConfirmUtc=08:00:00;", Canonical(new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(8.0) }));
        Assert.Equal(BreakoutDefaults + "BreakoutLastConfirmUtc=07:30:15;", Canonical(new BreakoutParameters { LastConfirmUtc = new TimeSpan(7, 30, 15) }));
        Assert.Equal("93025eb7", Hash(new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(8.0) }));
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    [Trait("Req", "SIG-029")]
    public void Canonical_CBotDefaults_AppendBothInOrderAndHash8aa3f28a()
    {
        Assert.Equal(BreakoutDefaults + "TrendSlowEmaPeriod=200;BreakoutLastConfirmUtc=08:00:00;", Canonical(CBotDefaults));
        Assert.Equal("8aa3f28a", Hash(CBotDefaults));
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    [Trait("Req", "SIG-029")]
    public void Canonical_UnderACultureWithOtherSeparators_StillUsesInvariantFormatting()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        var other = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        other.NumberFormat.NumberDecimalSeparator = ",";
        other.NumberFormat.NumberGroupSeparator = ".";
        other.DateTimeFormat.TimeSeparator = ".";
        try
        {
            CultureInfo.CurrentCulture = other;

            Assert.Equal(BreakoutDefaults + "TrendSlowEmaPeriod=200;BreakoutLastConfirmUtc=08:00:00;", Canonical(CBotDefaults));
            Assert.Equal("8aa3f28a", Hash(CBotDefaults));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Req", "SIG-028")]
    [Trait("Req", "SIG-029")]
    public void Canonical_UnderThaiCulture_StillUsesInvariantFormatting()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");

            Assert.Equal("8aa3f28a", Hash(CBotDefaults));
            Assert.Equal("119a3864", Hash(new BreakoutParameters()));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
