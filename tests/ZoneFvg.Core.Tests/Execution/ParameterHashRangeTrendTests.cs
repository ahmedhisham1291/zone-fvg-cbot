using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-042 (row AY): in SessionRange mode the parameter hash appends <c>RangeTrendFastEma=&lt;n&gt;;</c>
/// after the six range fields, only when n is not 0; with 0 every text and hash is what it was.
/// Every expected hash was computed outside the code under test with Python's hashlib (SHA-256 of
/// the UTF-8 canonical text, first eight hex characters), with a script that first reproduced the
/// existing pins 3f0f30a3, 70a33698, f681dff2, 469b1f35, f949f461, 57c90d56 and f99c0872.
/// </summary>
public class ParameterHashRangeTrendTests
{
    private const string CanonicalDefaults =
        "BiasTimeFrame=D1;ZoneTimeFrame=H4;BiasEmaPeriod=50;AtrPeriod=14;SwingStrength=3;ImpulseAtrMult=2;"
        + "MaxImpulseBars=3;MaxBaseBars=3;RequireBos=true;MinZoneAtr=0.2;MaxZoneAtr=1.5;MaxZoneAgeBars=120;"
        + "ConfirmWindowBars=16;MinFvgAtr=0.25;EntryLevelPct=50;StopBufferAtr=0.1;RewardRisk=2;"
        + "SessionStartUtc=07:00:00;SessionEndUtc=17:00:00;FridayNoEntryUtc=12:00:00;FridayCloseUtc=20:00:00;"
        + "MaxSpreadPips=1.5;MinStopPips=12;MaxStopAtr=2;CostToRiskMax=0.1;MinFvgSpreadMult=3;MinRoomR=1.5;EntryExpiryBars=8;";

    private const string RangePart =
        "RangeStartNy=19:00:00;RangeEndNy=03:00:00;RangeLastEntryNy=12:00:00;RangeExitNy=16:30:00;RangeMinWidthAtr=0.5;RangeStopAtr=1;";

    private static string Canonical(SessionRangeParameters? range)
    {
        return ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, range);
    }

    private static string Hash(SessionRangeParameters? range)
    {
        return ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, range);
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Canonical_Zero_TheTextAndHashOfBefore3f0f30a3()
    {
        var zero = new SessionRangeParameters { RangeTrendFastEma = 0 };

        Assert.Equal(CanonicalDefaults + RangePart, Canonical(zero));
        Assert.Equal("3f0f30a3", Hash(zero));
        Assert.DoesNotContain("RangeTrendFastEma", Canonical(zero), StringComparison.Ordinal);
        Assert.Equal("70a33698", Hash(new SessionRangeParameters { MinWidthAtr = 0.0, RangeTrendFastEma = 0 }));
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Canonical_Twenty_AppendsTheFieldAfterTheRangeFieldsAndHashesC3c1d46e()
    {
        var twenty = new SessionRangeParameters { RangeTrendFastEma = 20 };

        Assert.Equal(CanonicalDefaults + RangePart + "RangeTrendFastEma=20;", Canonical(twenty));
        Assert.Equal("c3c1d46e", Hash(twenty));
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(2)]
    [InlineData(49)]
    public void Canonical_OtherValues_WrittenAsInvariantIntegers_EachItsOwnHash(int fast)
    {
        var range = new SessionRangeParameters { RangeTrendFastEma = fast };

        Assert.EndsWith(RangePart + "RangeTrendFastEma=" + fast.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";", Canonical(range), StringComparison.Ordinal);
        Assert.NotEqual(Hash(new SessionRangeParameters { RangeTrendFastEma = 20 }), Hash(range));
        Assert.NotEqual("3f0f30a3", Hash(range));
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Canonical_OtherModes_NoSessionRangeParameters_NothingAppended()
    {
        Assert.Equal(CanonicalDefaults, Canonical(null));
        Assert.Equal("f681dff2", Hash(null));
        Assert.Equal("119a3864", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), new BreakoutParameters(), null));
        Assert.DoesNotContain("RangeTrendFastEma", ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), new BreakoutParameters { TrendSlowEmaPeriod = 200 }, null, null), StringComparison.Ordinal);
        Assert.DoesNotContain("RangeTrendFastEma", ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters(), null, null, new ChochParameters()), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the five parameter files

    private static SymbolProfileResult Gold(double pip)
    {
        return SymbolProfile.Resolve("XAUUSD", "XAU", "USD", pip, 0.0, 0.0, 0.0, 0.0, 0.0);
    }

    private static SymbolProfileResult Forex()
    {
        return SymbolProfile.Resolve("EURUSD", "EUR", "USD", 0.0001, 0.0, 0.0, 0.0, 0.0, 0.0);
    }

    private static string FileHash(SymbolProfileResult profile, int fast, bool allDay = true)
    {
        var execution = new ExecutionParameters
        {
            SessionStartUtc = allDay ? TimeSpan.Zero : TimeSpan.FromHours(7.0),
            SessionEndUtc = allDay ? TimeSpan.FromHours(24.0) : TimeSpan.FromHours(17.0),
            MaxSpreadPips = profile.MaxSpreadPips,
            MinStopPips = profile.MinStopPips,
        };
        SessionRangeParameters range = SessionRangeText.Build("19:00", "03:00", "12:00", "16:30", 0.5, 1.0, fast);
        return ParameterHash.Compute(new SignalParameters(), execution, null, range, null, profile);
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void FileHash_Gold_Twenty2dea1bc3_Zero469b1f35(double pip)
    {
        Assert.Equal("2dea1bc3", FileHash(Gold(pip), 20));
        Assert.Equal("469b1f35", FileHash(Gold(pip), 0));
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void FileHash_Forex_Twenty3280d399_ZeroF949f461()
    {
        Assert.Equal("3280d399", FileHash(Forex(), 20));
        Assert.Equal("f949f461", FileHash(Forex(), 0));
    }

    [Theory]
    [Trait("Req", "SIG-042")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void FileHash_SevenToSeventeen_Gold9078773fForexAfa64011_WithZeroTheOldOnes(double pip)
    {
        Assert.Equal("9078773f", FileHash(Gold(pip), 20, allDay: false));
        Assert.Equal("afa64011", FileHash(Forex(), 20, allDay: false));
        Assert.Equal("57c90d56", FileHash(Gold(pip), 0, allDay: false));
        Assert.Equal("f99c0872", FileHash(Forex(), 0, allDay: false));
    }
}
