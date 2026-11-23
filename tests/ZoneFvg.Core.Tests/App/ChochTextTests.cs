using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// PLT-016: the cBot texts of the Entry model group and of the trading window, turned into the
/// core's values by <see cref="ChochText"/>: defaults, every refusal with the cBot's names, 24:00
/// accepted only for SessionEndUtc, the every-mode check of the breakout values with an all-day
/// session, and the hashes (hand-computed with Python's hashlib, see <c>ParameterHashChochTests</c>).
/// </summary>
public class ChochTextTests
{
    private static ChochParameters? Build(string? model = "Fvg", EntryMode mode = EntryMode.ZoneFvg, int window = 32, double buffer = 0.1, double minStop = 5.0, double minRr = 2.0)
    {
        return ChochText.Build(model, mode, false, window, false, false, buffer, minStop, minRr);
    }

    private static void AssertRefusal(ArgumentException exception, string name, string text)
    {
        Assert.Equal(name, exception.ParamName);
        Assert.Equal(text + " (Parameter '" + name + "')", exception.Message);
    }

    [Fact]
    [Trait("Req", "PLT-016")]
    public void Build_TheCbotDefaults_FvgGivesNull_ChochGivesTheCoreDefaults()
    {
        Assert.Null(Build());
        Assert.Equal(new ChochParameters(), Build("Choch"));
    }

    [Fact]
    [Trait("Req", "PLT-016")]
    public void Build_OtherValues_TakenAsTheyAre()
    {
        ChochParameters parameters = ChochText.Build("Choch", EntryMode.ZoneFvg, true, 48, true, true, 0.25, 3.0, 1.5)!;

        Assert.Equal(new ChochParameters { RequireSweep = true, ChochWindowBars = 48, ChochUseBias = true, ChochStopAtDistal = true, ChochTargetBufferAtr = 0.25, ChochMinStopPips = 3.0, MinRewardRisk = 1.5 }, parameters);
    }

    [Theory]
    [Trait("Req", "PLT-016")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fvg")]
    [InlineData("CHOCH")]
    [InlineData("Choch ")]
    [InlineData("1")]
    public void Build_EntryModelNotExactlyFvgOrChoch_Refused(string? model)
    {
        AssertRefusal(Assert.Throws<ArgumentException>(() => Build(model)), "EntryModel", "EntryModel must be one of: Fvg, Choch.");
    }

    [Theory]
    [Trait("Req", "PLT-016")]
    [InlineData(EntryMode.Breakout)]
    [InlineData(EntryMode.SessionRange)]
    public void Build_ChochWithAnotherEntryMode_RefusedNamingBothParameters(EntryMode mode)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Build("Choch", mode));

        AssertRefusal(exception, "EntryModel", "EntryModel Choch needs EntryMode ZoneFvg, but EntryMode is " + mode + ".");
        Assert.Null(Build("Fvg", mode));
    }

    [Theory]
    [Trait("Req", "PLT-016")]
    [InlineData("Fvg", EntryMode.Breakout)]
    [InlineData("Fvg", EntryMode.ZoneFvg)]
    [InlineData("Choch", EntryMode.ZoneFvg)]
    public void Build_InvalidValues_RefusedInEveryModeWithTheCbotNames(string model, EntryMode mode)
    {
        AssertRefusal(Assert.Throws<ArgumentOutOfRangeException>(() => Build(model, mode, window: 3)), "ChochWindowBars", "ChochWindowBars must be a whole number from 4 to 96 but was 3.");
        AssertRefusal(Assert.Throws<ArgumentOutOfRangeException>(() => Build(model, mode, buffer: 1.0001)), "ChochTargetBufferAtr", "ChochTargetBufferAtr must be a finite number from 0 to 1 but was 1.0001.");
        AssertRefusal(Assert.Throws<ArgumentOutOfRangeException>(() => Build(model, mode, minStop: 0.0)), "ChochMinStopPips", "ChochMinStopPips must be a finite number greater than zero but was 0.");
        AssertRefusal(Assert.Throws<ArgumentOutOfRangeException>(() => Build(model, mode, minRr: 0.9999)), "MinRewardRisk", "MinRewardRisk must be a finite number from 1 to 10 but was 0.9999.");
    }

    [Fact]
    [Trait("Req", "PLT-016")]
    public void Build_InvalidValueAndModeConflict_TheValueIsRefusedFirst()
    {
        Assert.Equal("ChochWindowBars", Assert.Throws<ArgumentOutOfRangeException>(() => Build("Choch", EntryMode.Breakout, window: 97)).ParamName);
    }

    [Theory]
    [Trait("Req", "PLT-016")]
    [InlineData("00:00", 0, 0)]
    [InlineData("07:00", 7, 0)]
    [InlineData("23:59", 23, 59)]
    public void SessionStartUtc_TimesOfDay_Accepted(string text, int hours, int minutes)
    {
        Assert.Equal(new TimeSpan(hours, minutes, 0), ChochText.SessionStartUtc(text));
    }

    [Theory]
    [Trait("Req", "PLT-016")]
    [InlineData("24:00")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7:00")]
    [InlineData("07.00")]
    [InlineData("25:00")]
    [InlineData("1.00:00:00")]
    public void SessionStartUtc_24OrNotHhMm_Refused(string? text)
    {
        AssertRefusal(Assert.Throws<ArgumentException>(() => ChochText.SessionStartUtc(text)), "SessionStartUtc", "SessionStartUtc must be a UTC time of day written as HH:mm, for example 07:00.");
    }

    [Theory]
    [Trait("Req", "PLT-016")]
    [InlineData("17:00", 17, 0)]
    [InlineData("00:15", 0, 15)]
    [InlineData("24:00", 24, 0)]
    public void SessionEndUtc_TimesOfDayAnd2400_Accepted(string text, int hours, int minutes)
    {
        Assert.Equal(TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes), ChochText.SessionEndUtc(text));
    }

    [Theory]
    [Trait("Req", "PLT-016")]
    [InlineData("24:01")]
    [InlineData("24:00:00")]
    [InlineData("1.00:00:00")]
    [InlineData(null)]
    [InlineData("5pm")]
    public void SessionEndUtc_NotHhMmOr2400_Refused(string? text)
    {
        AssertRefusal(Assert.Throws<ArgumentException>(() => ChochText.SessionEndUtc(text)), "SessionEndUtc", "SessionEndUtc must be a UTC time of day written as HH:mm, for example 17:00, or 24:00 for the end of the day.");
    }

    [Fact]
    [Trait("Req", "PLT-016")]
    public void ForEveryModeCheck_AllDaySessionOutsideBreakout_Passes_InBreakoutRefused()
    {
        var allDay = new BreakoutParameters { WindowStartUtc = TimeSpan.Zero, WindowEndUtc = TimeSpan.FromHours(24.0), LastConfirmUtc = TimeSpan.FromHours(8.0) };

        ChochText.ForEveryModeCheck(allDay, EntryMode.ZoneFvg).Validate();
        ChochText.ForEveryModeCheck(allDay, EntryMode.SessionRange).Validate();
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => ChochText.ForEveryModeCheck(allDay, EntryMode.Breakout).Validate());

        Assert.Equal("WindowEndUtc", refused.ParamName);
        Assert.Same(allDay, ChochText.ForEveryModeCheck(allDay, EntryMode.Breakout));
    }

    [Fact]
    [Trait("Req", "PLT-016")]
    public void ForEveryModeCheck_OtherSessions_Unchanged_StillChecked()
    {
        var normal = new BreakoutParameters { LastConfirmUtc = TimeSpan.FromHours(8.0) };
        var badStop = new BreakoutParameters { WindowStartUtc = TimeSpan.Zero, WindowEndUtc = TimeSpan.FromHours(24.0), StopAtrMult = 0.0, LastConfirmUtc = TimeSpan.FromHours(8.0) };

        Assert.Same(normal, ChochText.ForEveryModeCheck(normal, EntryMode.ZoneFvg));
        Assert.Equal("StopAtrMult", Assert.Throws<ArgumentOutOfRangeException>(() => ChochText.ForEveryModeCheck(badStop, EntryMode.ZoneFvg).Validate()).ParamName);
        Assert.Throws<ArgumentNullException>(() => ChochText.ForEveryModeCheck(null!, EntryMode.ZoneFvg));
    }

    [Fact]
    [Trait("Req", "PLT-016")]
    public void Hash_TheCbotDefaultsOfEachModelAndMode()
    {
        var signal = new SignalParameters();
        var session = new ExecutionParameters();
        var allDay = new ExecutionParameters { SessionStartUtc = ChochText.SessionStartUtc("00:00"), SessionEndUtc = ChochText.SessionEndUtc("24:00") };
        var gold = new ExecutionParameters { MinStopPips = 8.0, MaxSpreadPips = 0.4 };

        Assert.Equal("f681dff2", ParameterHash.Compute(signal, session, null, null, Build()));
        Assert.Equal("88051907", ParameterHash.Compute(signal, session, null, null, Build("Choch")));
        Assert.Equal("0df0b3e7", ParameterHash.Compute(signal, allDay, null, null, Build("Choch")));
        Assert.Equal("ac941dbe", ParameterHash.Compute(signal, gold, null, null, Build("Choch", minStop: 3.0)));
        Assert.Equal(BreakoutRecoveryData.Hash, ParameterHash.Compute(signal, session, new BreakoutParameters(), null, Build("Fvg", EntryMode.Breakout)));
    }
}
