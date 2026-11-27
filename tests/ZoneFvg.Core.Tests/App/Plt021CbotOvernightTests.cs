using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// PLT-021: Overnight in the cBot and the drawdown brake off by default (spec v0.18 rows AZ, BB,
/// BC). What can be checked without cTrader is checked here: the cBot's parameter declarations and
/// <c>BuildParameters</c> in its source, the six parameter files under <c>deploy/params</c>, the
/// cBot's default values run through the core's own functions, the core's start line, and the
/// pinned hashes (computed outside the code under test with Python's hashlib).
/// </summary>
public class Plt021CbotOvernightTests
{
    private static readonly string[] Files = { "audusd", "eurusd", "gbpusd", "usdjpy", "xauusd" };

    private static readonly string[] NightLines =
    {
        "NightEntryNy=18:15", "NightLatestNy=19:00", "NightExitNy=02:45", "NightEntryBufferAtr=0.03", "NightStopAtr=1.0", "NightTargetR=5.0", "NightOrderMinutes=60",
    };

    private static readonly string[] NightDeclarations =
    {
        "[Parameter(\"NightEntryNy\", DefaultValue = \"18:15\", Group = \"Overnight\")]",
        "[Parameter(\"NightLatestNy\", DefaultValue = \"19:00\", Group = \"Overnight\")]",
        "[Parameter(\"NightExitNy\", DefaultValue = \"02:45\", Group = \"Overnight\")]",
        "[Parameter(\"NightEntryBufferAtr\", DefaultValue = 0.03, Group = \"Overnight\")]",
        "[Parameter(\"NightStopAtr\", DefaultValue = 1.0, Group = \"Overnight\")]",
        "[Parameter(\"NightTargetR\", DefaultValue = 5.0, Group = \"Overnight\")]",
        "[Parameter(\"NightOrderMinutes\", DefaultValue = 60, Group = \"Overnight\")]",
    };

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            if (Directory.Exists(Path.Combine(folder.FullName, "src", "ZoneFvg.Bot")) && Directory.Exists(Path.Combine(folder.FullName, "deploy", "params")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("src/ZoneFvg.Bot and deploy/params were not found above " + AppContext.BaseDirectory);
    }

    private static string BotSource()
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ZoneFvg.Bot", "ZoneFvgBot.cs"));
    }

    private static string[] ParameterFile(string name)
    {
        return File.ReadAllLines(Path.Combine(RepositoryRoot(), "deploy", "params", name + ".cbotset.example"));
    }

    private static int Once(string source, string text)
    {
        int at = source.IndexOf(text, StringComparison.Ordinal);
        Assert.True(at > 0, text);
        Assert.Equal(at, source.LastIndexOf(text, StringComparison.Ordinal));
        return at;
    }

    // ------------------------------------------------------------------ the cBot source

    [Fact]
    [Trait("Req", "PLT-021")]
    public void Bot_TheSevenNightParameters_DeclaredOnceInOrderDirectlyAfterRangeTrendFastEma()
    {
        string source = BotSource();
        int previous = Once(source, "[Parameter(\"RangeTrendFastEma\", DefaultValue = 20, Group = \"Entry mode\")]");
        foreach (string declaration in NightDeclarations)
        {
            int at = Once(source, declaration);
            Assert.True(at > previous, declaration);
            Assert.DoesNotContain("[Parameter(", source.Substring(previous + 1, at - previous - 1), StringComparison.Ordinal);
            previous = at;
        }

        int next = Once(source, "[Parameter(\"EntryModel\"");
        Assert.DoesNotContain("[Parameter(", source.Substring(previous + 1, next - previous - 1), StringComparison.Ordinal);
        Assert.Equal(7, source.Split("Group = \"Overnight\")]", StringSplitOptions.None).Length - 1);
    }

    [Theory]
    [Trait("Req", "PLT-021")]
    [InlineData("public string NightEntryNy { get; set; } = \"18:15\";")]
    [InlineData("public string NightLatestNy { get; set; } = \"19:00\";")]
    [InlineData("public string NightExitNy { get; set; } = \"02:45\";")]
    [InlineData("public double NightEntryBufferAtr { get; set; }")]
    [InlineData("public double NightStopAtr { get; set; }")]
    [InlineData("public double NightTargetR { get; set; }")]
    [InlineData("public int NightOrderMinutes { get; set; }")]
    [InlineData("[Parameter(\"DrawdownBrakePercent\", DefaultValue = 0.0, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"EntryMode\", DefaultValue = \"SessionRange\", Group = \"Entry mode\")]")]
    [InlineData("[Parameter(\"RiskPercent\", DefaultValue = 0.75, Group = \"Risk\")]")]
    public void Bot_PropertiesAndDefaults_DeclaredOnce(string declaration)
    {
        Once(BotSource(), declaration);
    }

    [Fact]
    [Trait("Req", "PLT-021")]
    public void Bot_BuildParameters_ChecksTheNightValuesInEveryModeAndPassesThemInOvernightModeOnly()
    {
        string source = BotSource();
        int start = Once(source, "private BotParameters BuildParameters(");
        int end = source.IndexOf("private SignalParameters BuildSignal(", StringComparison.Ordinal);
        string build = source.Substring(start, end - start);

        Assert.Contains("OvernightParameters overnight = OvernightText.Build(NightEntryNy, NightLatestNy, NightExitNy, NightEntryBufferAtr, NightStopAtr, NightTargetR, NightOrderMinutes);", build, StringComparison.Ordinal);
        Assert.Contains("Overnight = mode == CoreEntryMode.Overnight ? overnight : null,", build, StringComparison.Ordinal);
        Assert.Contains("CoreEntryMode.Overnight => null,", build, StringComparison.Ordinal);
        Assert.Contains("\" must be one of: ZoneFvg, Breakout, SessionRange, Overnight.\"", build, StringComparison.Ordinal);
        Assert.True(build.IndexOf("OvernightText.Build(", StringComparison.Ordinal) < build.IndexOf("return new BotParameters", StringComparison.Ordinal));
        Assert.Contains("parameters.Choch, parameters.Overnight);", source, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the cBot texts through the core

    [Fact]
    [Trait("Req", "PLT-021")]
    public void OvernightText_TheDefaults_GiveTheCoreDefaults()
    {
        Assert.Equal(new OvernightParameters(), OvernightText.Build("18:15", "19:00", "02:45", 0.03, 1.0, 5.0, 60));
    }

    [Theory]
    [Trait("Req", "PLT-021")]
    [InlineData("1815", "19:00", "02:45", "NightEntryNy")]
    [InlineData("18:15", "24:00", "02:45", "NightLatestNy")]
    [InlineData("18:15", "19:00", "", "NightExitNy")]
    [InlineData(null, "19:00", "02:45", "NightEntryNy")]
    [InlineData("18:15", "18:15", "02:45", "NightLatestNy")]
    [InlineData("18:15", "19:00", "18:30", "NightExitNy")]
    public void OvernightText_BadTime_RefusedNamingTheCbotParameter(string? entry, string? latest, string? exit, string name)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => OvernightText.Build(entry, latest, exit, 0.03, 1.0, 5.0, 60));

        Assert.Equal(name, exception.ParamName);
        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PLT-021")]
    [InlineData(0.0, 1.0, 5.0, 60, "NightEntryBufferAtr")]
    [InlineData(0.03, -1.0, 5.0, 60, "NightStopAtr")]
    [InlineData(0.03, 1.0, double.NaN, 60, "NightTargetR")]
    [InlineData(0.03, 1.0, 5.0, 0, "NightOrderMinutes")]
    [InlineData(0.03, 1.0, 5.0, 181, "NightOrderMinutes")]
    public void OvernightText_BadNumber_RefusedNamingTheCbotParameter(double buffer, double stop, double target, int minutes, string name)
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => OvernightText.Build("18:15", "19:00", "02:45", buffer, stop, target, minutes));

        Assert.Equal(name, exception.ParamName);
    }

    private static BotParameters Defaults(double pip, EntryMode mode = EntryMode.Overnight, string model = "Fvg", double riskPercent = 1.0)
    {
        TimeSpan startUtc = ChochText.SessionStartUtc("00:00");
        TimeSpan endUtc = ChochText.SessionEndUtc("24:00");
        SymbolProfileResult profile = SymbolProfile.Resolve("XAUUSD", "XAU", "USD", pip, 0.0, 0.0, 0.0, 0.0, 0.0);
        SessionRangeParameters sessionRange = SessionRangeText.Build("19:00", "03:00", "12:00", "16:30", 0.5, 1.0, 20);
        OvernightParameters overnight = OvernightText.Build("18:15", "19:00", "02:45", 0.03, 1.0, 5.0, 60);
        ChochParameters? choch = ChochText.Build(model, mode, false, 32, true, false, 0.1, profile.ChochMinStopPips, 2.0);
        var execution = new ExecutionParameters { SessionStartUtc = startUtc, SessionEndUtc = endUtc, MaxSpreadPips = profile.MaxSpreadPips, MinStopPips = profile.MinStopPips };
        var breakout = new BreakoutParameters { WindowStartUtc = startUtc, WindowEndUtc = endUtc, TrendSlowEmaPeriod = 200, LastConfirmUtc = TimeSpan.FromHours(8.0) };
        ChochText.ForEveryModeCheck(breakout, mode).Validate();

        var parameters = new BotParameters
        {
            Symbol = "XAUUSD",
            Execution = execution,
            SessionRange = mode == EntryMode.SessionRange ? sessionRange : null,
            Overnight = mode == EntryMode.Overnight ? overnight : null,
            Choch = choch,
            Risk = new RiskParameters
            {
                RiskEpochDate = RiskData.Epoch,
                RiskPercent = riskPercent,
                DrawdownBrakePercent = 0.0,
                DrawdownBrakeFactor = 0.5,
                LadderEnabled = false,
                SizingPlan = SizingPlan.Ladder,
                InitialCapital = 200000.0,
                MaxVolumeUnits = profile.MaxVolumeUnits,
            },
            SymbolProfile = profile,
        };
        parameters.Validate(isBacktest: true);
        return parameters;
    }

    [Theory]
    [Trait("Req", "PLT-021")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void Defaults_OvernightGoldThroughTheCore_Accepted(double pip)
    {
        BotParameters parameters = Defaults(pip);

        Assert.Equal(EntryMode.Overnight, parameters.Mode);
        Assert.Equal(new OvernightParameters(), parameters.Overnight);
        Assert.Null(parameters.SessionRange);
        Assert.Null(parameters.Breakout);
        Assert.Null(parameters.Choch);
        Assert.Equal(1.0, parameters.Risk.RiskPercent);
        Assert.Equal(0.0, parameters.Risk.DrawdownBrakePercent);
        Assert.Equal("826c617f", ParameterHash.Compute(parameters.Signal, parameters.Execution, null, null, null, parameters.SymbolProfile, parameters.Overnight));
    }

    [Theory]
    [Trait("Req", "PLT-021")]
    [Trait("Req", "SIG-035")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void Defaults_OvernightWithEntryModelChoch_RefusedNamingEntryModel(double pip)
    {
        var exception = Assert.Throws<ArgumentException>(() => Defaults(pip, model: "Choch"));

        Assert.Equal("EntryModel", exception.ParamName);
        Assert.Contains("EntryMode is Overnight", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PLT-021")]
    public void Defaults_SessionRangeGold_KeepsItsHash2dea1bc3AndTheBrakeOff()
    {
        BotParameters parameters = Defaults(1.0, EntryMode.SessionRange, riskPercent: 0.75);

        Assert.Equal("2dea1bc3", ParameterHash.Compute(parameters.Signal, parameters.Execution, null, parameters.SessionRange, null, parameters.SymbolProfile));
        Assert.Equal(0.0, parameters.Risk.DrawdownBrakePercent);
    }

    [Fact]
    [Trait("Req", "PLT-021")]
    [Trait("Req", "SIG-043")]
    public void BotParameters_OvernightWithAnotherMode_RefusedNamingOvernight()
    {
        BotParameters both = OvernightBotData.Parameters() with { SessionRange = new SessionRangeParameters() };
        BotParameters breakout = OvernightBotData.Parameters() with { Breakout = new BreakoutParameters { WindowStartUtc = TimeSpan.Zero, WindowEndUtc = TimeSpan.FromHours(23.0) }, Execution = OvernightBotData.Execution() with { SessionEndUtc = TimeSpan.FromHours(23.0) } };
        BotParameters choch = OvernightBotData.Parameters() with { Choch = new ChochParameters() };
        BotParameters invalid = OvernightBotData.Parameters() with { Overnight = new OvernightParameters { NightOrderMinutes = 0 } };

        Assert.Equal("Overnight", Assert.Throws<ArgumentException>(() => both.Validate(true)).ParamName);
        Assert.Equal("Overnight", Assert.Throws<ArgumentException>(() => breakout.Validate(true)).ParamName);
        Assert.Equal("Choch", Assert.Throws<ArgumentException>(() => choch.Validate(true)).ParamName);
        Assert.Equal("NightOrderMinutes", Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Validate(true)).ParamName);
        Assert.Equal(EntryMode.SessionRange, both.Mode);
    }

    // ------------------------------------------------------------------ the parameter files

    [Fact]
    [Trait("Req", "PLT-021")]
    public void ParameterFiles_TheSixFilesCarryTheNightLinesOnceInTheCbotsOrderAndTheBrakeOff()
    {
        foreach (string name in Files.Append("xauusd-overnight"))
        {
            string[] lines = ParameterFile(name);
            int at = Array.IndexOf(lines, "RangeTrendFastEma=20");
            Assert.Equal(11, at);
            for (int i = 0; i < NightLines.Length; i++)
            {
                Assert.Single(lines, l => l == NightLines[i]);
                Assert.Equal(NightLines[i], lines[at + 1 + i]);
            }

            Assert.Equal("EntryModel=Fvg", lines[at + 1 + NightLines.Length]);
            Assert.Single(lines, l => l == "DrawdownBrakePercent=0.0");
            Assert.DoesNotContain(lines, l => l.StartsWith("DrawdownBrakePercent=", StringComparison.Ordinal) && l != "DrawdownBrakePercent=0.0");
            Assert.Equal(7, lines.Count(l => l.StartsWith("Night", StringComparison.Ordinal)));
        }
    }

    [Fact]
    [Trait("Req", "PLT-021")]
    public void ParameterFiles_TheOvernightFileDiffersFromTheGoldFileOnlyInModeRiskAndHeartbeat()
    {
        string[] gold = ParameterFile("xauusd");
        string[] night = ParameterFile("xauusd-overnight");

        Assert.Equal(gold.Length, night.Length);
        var differences = Enumerable.Range(0, gold.Length).Where(i => gold[i] != night[i]).Select(i => night[i]).ToArray();
        Assert.Equal(new[] { "EntryMode=Overnight", "RiskPercent=1.0", "HeartbeatUrl=CHANGE_ME_heartbeat_address_for_xauusd_overnight" }, differences);
        Assert.Equal("EntryMode=SessionRange", gold[0]);
        Assert.Equal("EntryMode=Overnight", night[0]);
        Assert.Single(night, l => l == "EntryModel=Fvg");
        Assert.Single(night, l => l == "RiskPercent=1.0");
        Assert.Single(gold, l => l == "RiskPercent=0.75");
    }
}
