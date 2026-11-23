using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// PLT-015: the six cBot parameters of the SessionRange mode are turned into
/// <see cref="SessionRangeParameters"/> by <see cref="SessionRangeText.Build"/> and checked; a
/// refusal names each parameter as cTrader shows it; the hash of the mode with the cBot defaults is
/// the hand-computed one, and the hashes of the other modes do not change. Every expected hash was
/// computed outside the code under test, with Python's hashlib (SHA-256, UTF-8, first eight hex
/// characters) from the canonical text of <see cref="ParameterHash.Canonical"/>: forex defaults
/// (MinStopPips 12, MaxSpreadPips 1.5) and gold's example file (MinStopPips 8, MaxSpreadPips 0.4).
/// </summary>
public class SessionRangeTextTests
{
    private static readonly ExecutionParameters Gold = new() { MinStopPips = 8.0, MaxSpreadPips = 0.4 };

    private static SessionRangeParameters Defaults()
    {
        return SessionRangeText.Build("19:00", "03:00", "12:00", "16:30", 0.5, 1.0);
    }

    private static ArgumentException Refused(string start = "19:00", string end = "03:00", string lastEntry = "12:00", string exit = "16:30", double minWidth = 0.5, double stop = 1.0)
    {
        return Assert.ThrowsAny<ArgumentException>(() => SessionRangeText.Build(start, end, lastEntry, exit, minWidth, stop));
    }

    private static void AssertRefusal(ArgumentException exception, string name, string text)
    {
        Assert.Equal(name, exception.ParamName);
        Assert.Equal(text + " (Parameter '" + name + "')", exception.Message);
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Build_TheCbotDefaults_GiveTheCoreDefaults()
    {
        Assert.Equal(new SessionRangeParameters(), Defaults());
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Build_OtherValidTexts_AreTakenAsTheyAre()
    {
        SessionRangeParameters parameters = SessionRangeText.Build("18:15", "02:30", "11:45", "16:00", 0.0, 1.25);

        Assert.Equal(new TimeSpan(18, 15, 0), parameters.RangeStartNy);
        Assert.Equal(new TimeSpan(2, 30, 0), parameters.RangeEndNy);
        Assert.Equal(new TimeSpan(11, 45, 0), parameters.LastEntryNy);
        Assert.Equal(new TimeSpan(16, 0, 0), parameters.ExitNy);
        Assert.Equal(0.0, parameters.MinWidthAtr);
        Assert.Equal(1.25, parameters.StopAtrMult);
    }

    [Theory]
    [Trait("Req", "PLT-015")]
    [InlineData(0, "RangeStartNy")]
    [InlineData(1, "RangeEndNy")]
    [InlineData(2, "RangeLastEntryNy")]
    [InlineData(3, "RangeExitNy")]
    public void Build_ATimeThatIsNotHhMm_RefusedWithTheCbotName(int which, string name)
    {
        foreach (string? text in new string?[] { null, string.Empty, "abc", "19.00", "1900", "19:00:00" })
        {
            string?[] texts = { "19:00", "03:00", "12:00", "16:30" };
            texts[which] = text;

            ArgumentException exception = Assert.Throws<ArgumentException>(() => SessionRangeText.Build(texts[0], texts[1], texts[2], texts[3], 0.5, 1.0));

            AssertRefusal(exception, name, name + " must be a New York time of day written as HH:mm, for example 19:00.");
        }
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Build_TimeTexts_ParsedInTheOrderOfThePanel()
    {
        Assert.Equal("RangeStartNy", Assert.Throws<ArgumentException>(() => SessionRangeText.Build("x", "x", "x", "x", -1.0, 0.0)).ParamName);
        Assert.Equal("RangeEndNy", Assert.Throws<ArgumentException>(() => SessionRangeText.Build("19:00", "x", "x", "x", -1.0, 0.0)).ParamName);
        Assert.Equal("RangeLastEntryNy", Assert.Throws<ArgumentException>(() => SessionRangeText.Build("19:00", "03:00", "x", "x", -1.0, 0.0)).ParamName);
        Assert.Equal("RangeExitNy", Assert.Throws<ArgumentException>(() => SessionRangeText.Build("19:00", "03:00", "12:00", "x", -1.0, 0.0)).ParamName);
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Build_StartNotOnAQuarterHour_RefusedAsRangeStartNy()
    {
        AssertRefusal(
            Refused(start: "19:10"),
            "RangeStartNy",
            "RangeStartNy must be a time of day from 00:00 up to but not including 24:00 on a 15-minute boundary but was 19:10:00.");
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Build_LastEntryNotOnAQuarterHour_RefusedAsRangeLastEntryNy()
    {
        AssertRefusal(
            Refused(lastEntry: "11:59"),
            "RangeLastEntryNy",
            "RangeLastEntryNy must be a time of day from 00:00 up to but not including 24:00 on a 15-minute boundary but was 11:59:00.");
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Build_LastEntryBeforeTheRangeEnd_RefusedAsRangeLastEntryNy_AtTheEndAccepted()
    {
        AssertRefusal(
            Refused(lastEntry: "02:45"),
            "RangeLastEntryNy",
            "RangeLastEntryNy must not be before RangeEndNy (03:00:00) in the order of the trading day but was 02:45:00.");
        Assert.Equal(TimeSpan.FromHours(3.0), SessionRangeText.Build("19:00", "03:00", "03:00", "16:30", 0.5, 1.0).LastEntryNy);
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Build_ExitNotAfterTheLastEntry_RefusedAsRangeExitNy_OneBarLaterAccepted()
    {
        AssertRefusal(
            Refused(exit: "12:00"),
            "RangeExitNy",
            "RangeExitNy must be after RangeLastEntryNy (12:00:00) in the order of the trading day but was 12:00:00.");
        Assert.Equal(new TimeSpan(12, 15, 0), SessionRangeText.Build("19:00", "03:00", "12:00", "12:15", 0.5, 1.0).ExitNy);
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Build_EndNotAfterTheStart_RefusedAsRangeEndNy()
    {
        AssertRefusal(
            Refused(end: "18:45"),
            "RangeEndNy",
            "RangeEndNy must be after RangeStartNy (19:00:00) in the order of the trading day but was 18:45:00.");
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Build_WindowOfSixteenBars_Accepted_FifteenBarsRefusedAsRangeEndNy()
    {
        Assert.Equal(TimeSpan.FromHours(23.0), SessionRangeText.Build("19:00", "23:00", "23:00", "23:15", 0.5, 1.0).RangeEndNy);
        AssertRefusal(
            Refused(end: "22:45", lastEntry: "23:00", exit: "23:15"),
            "RangeEndNy",
            "RangeEndNy must leave a window of at least 16 M15 bars after RangeStartNy (19:00:00) but was 22:45:00.");
    }

    [Theory]
    [Trait("Req", "PLT-015")]
    [InlineData(-0.1, "-0.1")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    public void Build_MinWidthNegativeOrNotFinite_RefusedAsRangeMinWidthAtr(double value, string text)
    {
        AssertRefusal(
            Refused(minWidth: value),
            "RangeMinWidthAtr",
            "RangeMinWidthAtr must be a finite number that is not negative but was " + text + ".");
    }

    [Theory]
    [Trait("Req", "PLT-015")]
    [InlineData(0.0, "0")]
    [InlineData(-1.0, "-1")]
    [InlineData(double.NaN, "NaN")]
    public void Build_StopNotGreaterThanZero_RefusedAsRangeStopAtr(double value, string text)
    {
        AssertRefusal(
            Refused(stop: value),
            "RangeStopAtr",
            "RangeStopAtr must be a finite number greater than zero but was " + text + ".");
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Build_Refusal_KeepsTheCoreRefusalAsTheInnerException()
    {
        ArgumentException exception = Refused(stop: 0.0);

        Assert.Equal("StopAtrMult", Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException).ParamName);
    }

    [Theory]
    [Trait("Req", "PLT-015")]
    [InlineData("RangeStartNy", "RangeStartNy")]
    [InlineData("RangeEndNy", "RangeEndNy")]
    [InlineData("LastEntryNy", "RangeLastEntryNy")]
    [InlineData("ExitNy", "RangeExitNy")]
    [InlineData("MinWidthAtr", "RangeMinWidthAtr")]
    [InlineData("StopAtrMult", "RangeStopAtr")]
    [InlineData("Other", "Other")]
    public void CbotName_EachCoreName_IsTheNameCtraderShows(string coreName, string cbotName)
    {
        Assert.Equal(cbotName, SessionRangeText.CbotName(coreName));
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Hash_SessionRangeModeWithTheCbotDefaults_IsTheHandComputedValue()
    {
        Assert.Equal("3f0f30a3", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, Defaults()));
        Assert.Equal("377b6162", ParameterHash.Compute(new SignalParameters(), Gold, null, Defaults()));
    }

    [Fact]
    [Trait("Req", "PLT-015")]
    public void Hash_OtherModes_UnchangedByTheRangeParameters()
    {
        // The cBot defaults of the two breakout filters (TrendSlowEmaPeriod 200, BreakoutLastConfirmUtc 08:00).
        var breakout = new BreakoutParameters { TrendSlowEmaPeriod = 200, LastConfirmUtc = TimeSpan.FromHours(8.0) };

        Assert.Equal("f681dff2", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters()));
        Assert.Equal("8aa3f28a", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), breakout));
        Assert.Equal("05ee8872", ParameterHash.Compute(new SignalParameters(), Gold));
        Assert.Equal("fbdef461", ParameterHash.Compute(new SignalParameters(), Gold, breakout));
    }
}
