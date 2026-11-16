using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Label, comment, parameter hash and price grid (EXE-008). Expected texts are written by hand;
/// the default hash f681dff2 was computed outside the code under test, with Python's hashlib, from
/// the canonical text given in <see cref="CanonicalDefaults"/>.
/// </summary>
public class OrderFormatTests
{
    private const string CanonicalDefaults =
        "BiasTimeFrame=D1;ZoneTimeFrame=H4;BiasEmaPeriod=50;AtrPeriod=14;SwingStrength=3;ImpulseAtrMult=2;"
        + "MaxImpulseBars=3;MaxBaseBars=3;RequireBos=true;MinZoneAtr=0.2;MaxZoneAtr=1.5;MaxZoneAgeBars=120;"
        + "ConfirmWindowBars=16;MinFvgAtr=0.25;EntryLevelPct=50;StopBufferAtr=0.1;RewardRisk=2;"
        + "SessionStartUtc=07:00:00;SessionEndUtc=17:00:00;FridayNoEntryUtc=12:00:00;FridayCloseUtc=20:00:00;"
        + "MaxSpreadPips=1.5;MinStopPips=12;MaxStopAtr=2;CostToRiskMax=0.1;MinFvgSpreadMult=3;MinRoomR=1.5;EntryExpiryBars=8;";

    // ------------------------------------------------------------------ label

    [Fact]
    [Trait("Req", "EXE-008")]
    public void Label_Build_IsBotVersionSymbolAndZone()
    {
        Assert.Equal("ZFVG|v1|EURUSD|D-20260114-0400", OrderLabel.Build("EURUSD", "D-20260114-0400"));
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void Label_TryParse_RoundTrips()
    {
        Assert.True(OrderLabel.TryParse("ZFVG|v1|USDJPY|S-20260301-0800", out string symbol, out string zoneId));
        Assert.Equal("USDJPY", symbol);
        Assert.Equal("S-20260301-0800", zoneId);
    }

    [Theory]
    [Trait("Req", "EXE-008")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ZFVG|v2|EURUSD|D-1")]
    [InlineData("XFVG|v1|EURUSD|D-1")]
    [InlineData("ZFVG|v1|EURUSD")]
    [InlineData("ZFVG|v1|EURUSD|D-1|x")]
    [InlineData("ZFVG|v1||D-1")]
    [InlineData("ZFVG|v1|EURUSD|")]
    public void Label_TryParse_RejectsAnythingElse(string? label)
    {
        Assert.False(OrderLabel.TryParse(label, out string symbol, out string zoneId));
        Assert.Equal(string.Empty, symbol);
        Assert.Equal(string.Empty, zoneId);
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void Label_LengthGuard_AtMost100Characters()
    {
        // "ZFVG|v1|EURUSD|" is 15 characters: a zone id of 85 gives exactly 100, of 86 gives 101.
        string fits = OrderLabel.Build("EURUSD", new string('Z', 85));
        Assert.Equal(100, fits.Length);
        Assert.True(OrderLabel.TryParse(fits, out _, out _));
        Assert.Throws<ArgumentException>(() => OrderLabel.Build("EURUSD", new string('Z', 86)));
        Assert.False(OrderLabel.TryParse(fits + "Z", out _, out _));
    }

    [Theory]
    [Trait("Req", "EXE-008")]
    [InlineData("", "D-1")]
    [InlineData("EURUSD", "")]
    [InlineData("EUR|USD", "D-1")]
    [InlineData("EURUSD", "D|1")]
    public void Label_Build_RejectsEmptyFieldsAndSeparators(string symbol, string zoneId)
    {
        Assert.Throws<ArgumentException>(() => OrderLabel.Build(symbol, zoneId));
    }

    // ------------------------------------------------------------------ comment

    [Fact]
    [Trait("Req", "EXE-008")]
    public void Comment_Build_FiveDigitPricesTwoDecimalRiskAndTheHash()
    {
        string comment = OrderComment.Build(new CommentPlan(1.1, 1.0021, 1.104, 499.8, "f681dff2"), 5);

        Assert.Equal("e=1.10000;s=1.00210;t=1.10400;r=499.80;h=f681dff2", comment);
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void Comment_Build_ThreeDigitPricesForAYenPair()
    {
        string comment = OrderComment.Build(new CommentPlan(155.125, 154.9, 155.575, 500.0, "0123abcd"), 3);

        Assert.Equal("e=155.125;s=154.900;t=155.575;r=500.00;h=0123abcd", comment);
    }

    [Theory]
    [Trait("Req", "EXE-008")]
    [InlineData(1.10213, 1.09787, 1.11065, 5)]
    [InlineData(0.65432, 0.65012, 0.66272, 5)]
    [InlineData(155.125, 154.9, 155.575, 3)]
    public void Comment_ParseOfBuild_IsExactAtTheSymbolsDigits(double entry, double stop, double target, int digits)
    {
        var plan = new CommentPlan(entry, stop, target, 123.45, "deadbeef");
        string format = "F" + digits;

        CommentPlan parsed = OrderComment.TryParse(OrderComment.Build(plan, digits))!;

        Assert.Equal(entry.ToString(format, System.Globalization.CultureInfo.InvariantCulture), parsed.Entry.ToString(format, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(stop.ToString(format, System.Globalization.CultureInfo.InvariantCulture), parsed.Stop.ToString(format, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(target.ToString(format, System.Globalization.CultureInfo.InvariantCulture), parsed.Target.ToString(format, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(123.45, parsed.RiskMoney, 9);
        Assert.Equal("deadbeef", parsed.ParameterHash);
        Assert.Equal(entry, parsed.Entry, 9);
    }

    [Theory]
    [Trait("Req", "EXE-008")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("e=1.1;s=1.098;t=1.104;r=500.00")]
    [InlineData("e=1.1;s=1.098;t=1.104;r=500.00;h=f681dff2;x=1")]
    [InlineData("s=1.098;e=1.1;t=1.104;r=500.00;h=f681dff2")]
    [InlineData("e=;s=1.098;t=1.104;r=500.00;h=f681dff2")]
    [InlineData("e=-1.1;s=1.098;t=1.104;r=500.00;h=f681dff2")]
    [InlineData("e=0;s=1.098;t=1.104;r=500.00;h=f681dff2")]
    [InlineData("e=1e0;s=1.098;t=1.104;r=500.00;h=f681dff2")]
    [InlineData("e=1,1;s=1.098;t=1.104;r=500.00;h=f681dff2")]
    [InlineData("e=1.1;s=1.098;t=1.104;r=500.00;h=F681DFF2")]
    [InlineData("e=1.1;s=1.098;t=1.104;r=500.00;h=f681dff")]
    [InlineData("e=1.1;s=1.098;t=1.104;r=500.00;h=g681dff2")]
    [InlineData("e=1.1;s=1.098;t=1.104;r=x;h=f681dff2")]
    [InlineData("e=;s=;t=")]
    public void Comment_TryParse_RejectsAnythingElse(string? comment)
    {
        Assert.Null(OrderComment.TryParse(comment));
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void Comment_TryParse_ReadsAHandWrittenComment()
    {
        CommentPlan plan = OrderComment.TryParse("e=1.10000;s=1.09800;t=1.10400;r=500.00;h=f681dff2")!;

        Assert.Equal(new CommentPlan(1.1, 1.098, 1.104, 500.0, "f681dff2"), plan);
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void Comment_LengthGuard_AtMost100Characters()
    {
        // With 5 digits the fixed part is "e=1.10000" (9) + ";s=1.09800" (10) + ";t=1.10400" (10)
        // + ";r=" (3) + ";h=f681dff2" (11) = 43 characters. A risk written with 57 characters (54
        // integer digits and ".00") gives exactly 100; ten times that risk has 55 digits: 101.
        double fits = double.Parse(new string('1', 54), System.Globalization.CultureInfo.InvariantCulture);
        string comment = OrderComment.Build(new CommentPlan(1.1, 1.098, 1.104, fits, "f681dff2"), 5);
        Assert.Equal(100, comment.Length);
        Assert.NotNull(OrderComment.TryParse(comment));

        double tooLong = fits * 10.0;
        Assert.Throws<ArgumentException>(() => OrderComment.Build(new CommentPlan(1.1, 1.098, 1.104, tooLong, "f681dff2"), 5));
        Assert.Null(OrderComment.TryParse(comment + "0"));
    }

    [Theory]
    [Trait("Req", "EXE-008")]
    [InlineData(double.NaN, 1.098, 1.104, 500.0, "f681dff2", 5)]
    [InlineData(1.1, 0.0, 1.104, 500.0, "f681dff2", 5)]
    [InlineData(1.1, 1.098, double.PositiveInfinity, 500.0, "f681dff2", 5)]
    [InlineData(1.1, 1.098, 1.104, -0.01, "f681dff2", 5)]
    [InlineData(1.1, 1.098, 1.104, 500.0, "f681dff", 5)]
    [InlineData(1.1, 1.098, 1.104, 500.0, "F681DFF2", 5)]
    [InlineData(1.1, 1.098, 1.104, 500.0, "f681dff2", -1)]
    [InlineData(1.1, 1.098, 1.104, 500.0, "f681dff2", 11)]
    public void Comment_Build_RejectsValuesThatCannotBeWritten(double entry, double stop, double target, double risk, string hash, int digits)
    {
        Assert.Throws<ArgumentException>(() => OrderComment.Build(new CommentPlan(entry, stop, target, risk, hash), digits));
    }

    // ------------------------------------------------------------------ parameter hash

    [Fact]
    [Trait("Req", "EXE-008")]
    public void ParameterHash_Defaults_CanonicalTextAndHashAsComputedOutsideTheCode()
    {
        Assert.Equal(CanonicalDefaults, ParameterHash.Canonical(new SignalParameters(), new ExecutionParameters()));
        Assert.Equal("f681dff2", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters()));
    }

    public static IEnumerable<object[]> ChangedParameters()
    {
        var s = new SignalParameters();
        var e = new ExecutionParameters();
        yield return new object[] { "BiasEmaPeriod", s with { BiasEmaPeriod = 100 }, e };
        yield return new object[] { "AtrPeriod", s with { AtrPeriod = 15 }, e };
        yield return new object[] { "SwingStrength", s with { SwingStrength = 4 }, e };
        yield return new object[] { "ImpulseAtrMult", s with { ImpulseAtrMult = 2.5 }, e };
        yield return new object[] { "MaxImpulseBars", s with { MaxImpulseBars = 2 }, e };
        yield return new object[] { "MaxBaseBars", s with { MaxBaseBars = 2 }, e };
        yield return new object[] { "RequireBos", s with { RequireBos = false }, e };
        yield return new object[] { "MinZoneAtr", s with { MinZoneAtr = 0.3 }, e };
        yield return new object[] { "MaxZoneAtr", s with { MaxZoneAtr = 1.4 }, e };
        yield return new object[] { "MaxZoneAgeBars", s with { MaxZoneAgeBars = 121 }, e };
        yield return new object[] { "ConfirmWindowBars", s with { ConfirmWindowBars = 15 }, e };
        yield return new object[] { "MinFvgAtr", s with { MinFvgAtr = 0.35 }, e };
        yield return new object[] { "EntryLevelPct", s with { EntryLevelPct = 0.0 }, e };
        yield return new object[] { "StopBufferAtr", s with { StopBufferAtr = 0.2 }, e };
        yield return new object[] { "RewardRisk", s with { RewardRisk = 2.5 }, e };
        yield return new object[] { "SessionStartUtc", s, e with { SessionStartUtc = TimeSpan.FromHours(8.0) } };
        yield return new object[] { "SessionEndUtc", s, e with { SessionEndUtc = TimeSpan.FromHours(16.0) } };
        yield return new object[] { "FridayNoEntryUtc", s, e with { FridayNoEntryUtc = TimeSpan.FromHours(11.0) } };
        yield return new object[] { "FridayCloseUtc", s, e with { FridayCloseUtc = TimeSpan.FromHours(19.0) } };
        yield return new object[] { "MaxSpreadPips", s, e with { MaxSpreadPips = 2.0 } };
        yield return new object[] { "MinStopPips", s, e with { MinStopPips = 10.0 } };
        yield return new object[] { "MaxStopAtr", s, e with { MaxStopAtr = 2.5 } };
        yield return new object[] { "CostToRiskMax", s, e with { CostToRiskMax = 0.2 } };
        yield return new object[] { "MinFvgSpreadMult", s, e with { MinFvgSpreadMult = 2.0 } };
        yield return new object[] { "MinRoomR", s, e with { MinRoomR = 2.0 } };
        yield return new object[] { "EntryExpiryBars", s, e with { EntryExpiryBars = 9 } };
    }

    [Theory]
    [Trait("Req", "EXE-008")]
    [MemberData(nameof(ChangedParameters))]
    public void ParameterHash_AnyParameterChanged_ChangesTheCanonicalTextAndTheHash(string name, SignalParameters signal, ExecutionParameters execution)
    {
        string canonical = ParameterHash.Canonical(signal, execution);

        Assert.NotEqual(CanonicalDefaults, canonical);
        Assert.Contains(name + "=", canonical, StringComparison.Ordinal);
        Assert.NotEqual("f681dff2", ParameterHash.Compute(signal, execution));
        Assert.Matches("^[0-9a-f]{8}$", ParameterHash.Compute(signal, execution));
    }

    // ------------------------------------------------------------------ price grid

    [Theory]
    [Trait("Req", "EXE-008")]
    [InlineData(0.0001, 5, 0.00001)]
    [InlineData(0.01, 3, 0.001)]
    [InlineData(1.0, 1, 0.1)]
    public void PriceGrid_DigitsAndTick_FromThePipSize(double pipSize, int digits, double tick)
    {
        Assert.True(PriceGrid.IsUsable(pipSize));
        Assert.Equal(digits, PriceGrid.Digits(pipSize));
        Assert.Equal(tick, PriceGrid.Tick(pipSize), 15);
    }

    [Theory]
    [Trait("Req", "EXE-008")]
    [InlineData(1.0980049, 0.0001, 1.098)]
    [InlineData(1.10213999999, 0.0001, 1.10214)]
    [InlineData(155.12549, 0.01, 155.125)]
    [InlineData(2.25, 1.0, 2.3)] // an exact tie (2.25 is exact in binary): half away from zero
    public void PriceGrid_Round_ToTheNearestStep(double price, double pipSize, double expected)
    {
        Assert.Equal(expected, PriceGrid.Round(price, pipSize), 12);
    }

    [Theory]
    [Trait("Req", "EXE-008")]
    [InlineData(0.0)]
    [InlineData(-0.0001)]
    [InlineData(double.NaN)]
    [InlineData(1e-12)]
    public void PriceGrid_UnusablePipSize_IsRejected(double pipSize)
    {
        Assert.False(PriceGrid.IsUsable(pipSize));
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceGrid.Digits(pipSize));
    }

    [Fact]
    [Trait("Req", "EXE-008")]
    public void PriceGrid_PipSizeAboveOne_IsNotUsableAndANonFinitePriceThrows()
    {
        Assert.False(PriceGrid.IsUsable(10.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PriceGrid.Round(double.NaN, 0.0001));
    }
}
