using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// PLT-020: the defaults of the owner request of 2026-11-27 in the cBot (spec v0.16, rows AS, AT,
/// AU and AW): SessionRange with the Fvg model, a fixed 0.75% and the drawdown brake 3 / 0.5.
/// What can be checked without cTrader is checked here: the cBot's parameter declarations and
/// <c>BuildRisk</c> in its source, the five parameter files under <c>deploy/params</c>, the
/// cBot's default values run through the core's own functions, the core's start line and the
/// parameter hash. Every expected hash was computed outside the code under test with Python's
/// hashlib (SHA-256 of the UTF-8 canonical text, first eight hex characters).
/// </summary>
public class Plt020SessionRangeDefaultsTests
{
    private static readonly string[] Files = { "audusd", "eurusd", "gbpusd", "usdjpy", "xauusd" };

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

    private static void AssertOnce(string source, string text)
    {
        int at = source.IndexOf(text, StringComparison.Ordinal);
        Assert.True(at > 0, text);
        Assert.Equal(at, source.LastIndexOf(text, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the cBot source

    [Theory]
    [Trait("Req", "PLT-020")]
    [InlineData("[Parameter(\"EntryMode\", DefaultValue = \"SessionRange\", Group = \"Entry mode\")]")]
    [InlineData("[Parameter(\"EntryModel\", DefaultValue = \"Fvg\", Group = \"Entry model\")]")]
    [InlineData("[Parameter(\"RiskPercent\", DefaultValue = 0.75, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"DrawdownBrakePercent\", DefaultValue = 0.0, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"DrawdownBrakeFactor\", DefaultValue = 0.5, Group = \"Risk\")]")]
    [InlineData("public string EntryMode { get; set; } = \"SessionRange\";")]
    [InlineData("public string EntryModel { get; set; } = \"Fvg\";")]
    public void Bot_TheNewDefaults_DeclaredOnce(string declaration)
    {
        AssertOnce(BotSource(), declaration);
    }

    [Theory]
    [Trait("Req", "PLT-020")]
    [InlineData("[Parameter(\"LadderEnabled\", DefaultValue = false, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"SizingPlan\", DefaultValue = \"Ladder\", Group = \"Risk\")]")]
    [InlineData("[Parameter(\"SessionStartUtc\", DefaultValue = \"00:00\", Group = \"Execution\")]")]
    [InlineData("[Parameter(\"SessionEndUtc\", DefaultValue = \"24:00\", Group = \"Execution\")]")]
    [InlineData("[Parameter(\"InitialCapital\", DefaultValue = 200000.0, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"MaxSpreadPips\", DefaultValue = 0.0, Group = \"Execution\")]")]
    [InlineData("[Parameter(\"MinStopPips\", DefaultValue = 0.0, Group = \"Execution\")]")]
    [InlineData("[Parameter(\"ChochMinStopPips\", DefaultValue = 0.0, Group = \"Entry model\")]")]
    [InlineData("[Parameter(\"MaxVolumeUnits\", DefaultValue = 0.0, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"MaxDrawdownPercent\", DefaultValue = 8.0, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"DailyLossLimitPercent\", DefaultValue = 2.0, Group = \"Risk\")]")]
    public void Bot_EveryOtherDefault_Unchanged(string declaration)
    {
        AssertOnce(BotSource(), declaration);
    }

    [Fact]
    [Trait("Req", "PLT-020")]
    public void Bot_TheBrakeParameters_DirectlyAfterRiskPercent()
    {
        string source = BotSource();
        int risk = source.IndexOf("[Parameter(\"RiskPercent\"", StringComparison.Ordinal);
        int percent = source.IndexOf("[Parameter(\"DrawdownBrakePercent\"", StringComparison.Ordinal);
        int factor = source.IndexOf("[Parameter(\"DrawdownBrakeFactor\"", StringComparison.Ordinal);
        int next = source.IndexOf("[Parameter(\"MaxOpenRiskPercent\"", StringComparison.Ordinal);

        Assert.True(risk > 0 && risk < percent && percent < factor && factor < next);
        Assert.DoesNotContain("[Parameter(", source.Substring(risk + 1, percent - risk - 1), StringComparison.Ordinal);
        Assert.DoesNotContain("[Parameter(", source.Substring(percent + 1, factor - percent - 1), StringComparison.Ordinal);
        Assert.DoesNotContain("[Parameter(", source.Substring(factor + 1, next - factor - 1), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PLT-020")]
    [InlineData("DrawdownBrakePercent = DrawdownBrakePercent,")]
    [InlineData("DrawdownBrakeFactor = DrawdownBrakeFactor,")]
    [InlineData("RiskPercent = RiskPercent,")]
    [InlineData("LadderEnabled = LadderEnabled,")]
    public void BuildRisk_PassesTheBrakeToRiskParameters(string assignment)
    {
        string source = BotSource();
        int start = source.IndexOf("private RiskParameters BuildRisk(", StringComparison.Ordinal);
        int end = source.IndexOf("private NewsParameters BuildNews(", StringComparison.Ordinal);

        Assert.True(start > 0 && end > start);
        Assert.Contains(assignment, source.Substring(start, end - start), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the parameter files

    [Fact]
    [Trait("Req", "PLT-020")]
    public void ParameterFiles_CarryTheNewDefaultsOnceInTheCbotsOrder()
    {
        string[] expected = { "EntryMode=SessionRange", "EntryModel=Fvg", "RiskPercent=0.75", "DrawdownBrakePercent=0.0", "DrawdownBrakeFactor=0.5" };
        foreach (string name in Files)
        {
            string[] lines = ParameterFile(name);
            foreach (string line in expected)
            {
                Assert.Single(lines, l => l == line);
            }

            Assert.DoesNotContain(lines, l => l.StartsWith("EntryMode=", StringComparison.Ordinal) && l != "EntryMode=SessionRange");
            Assert.DoesNotContain(lines, l => l.StartsWith("RiskPercent=", StringComparison.Ordinal) && l != "RiskPercent=0.75");
            Assert.Equal("EntryMode=SessionRange", lines[0]);
            int risk = Array.IndexOf(lines, "RiskPercent=0.75");
            Assert.Equal("DrawdownBrakePercent=0.0", lines[risk + 1]);
            Assert.Equal("DrawdownBrakeFactor=0.5", lines[risk + 2]);
            Assert.Equal("MaxOpenRiskPercent=1.5", lines[risk + 3]);
            Assert.Equal("LadderEnabled=false", lines[Array.IndexOf(lines, "MaxVolumeUnits=0.0") + 1]);
            Assert.Single(lines, l => l == "SizingPlan=Ladder");
        }
    }

    [Fact]
    [Trait("Req", "PLT-020")]
    public void ParameterFiles_EqualApartFromTheHeartbeat()
    {
        string[] Without(string name) => ParameterFile(name).Where(l => !l.StartsWith("HeartbeatUrl=", StringComparison.Ordinal)).ToArray();

        string[] gold = Without("xauusd");
        foreach (string name in Files)
        {
            Assert.Equal(gold, Without(name));
        }
    }

    // ------------------------------------------------------------------ the defaults through the core

    private static BotParameters Defaults(double pip, string model = "Fvg")
    {
        const EntryMode mode = EntryMode.SessionRange;
        TimeSpan startUtc = ChochText.SessionStartUtc("00:00");
        TimeSpan endUtc = ChochText.SessionEndUtc("24:00");
        SymbolProfileResult profile = SymbolProfile.Resolve("XAUUSD", "XAU", "USD", pip, 0.0, 0.0, 0.0, 0.0, 0.0);
        SessionRangeParameters sessionRange = SessionRangeText.Build("19:00", "03:00", "12:00", "16:30", 0.5, 1.0, 20);
        ChochParameters? choch = ChochText.Build(model, mode, false, 32, true, false, 0.1, profile.ChochMinStopPips, 2.0);
        var execution = new ExecutionParameters { SessionStartUtc = startUtc, SessionEndUtc = endUtc, MaxSpreadPips = profile.MaxSpreadPips, MinStopPips = profile.MinStopPips };
        var breakout = new BreakoutParameters { WindowStartUtc = startUtc, WindowEndUtc = endUtc, TrendSlowEmaPeriod = 200, LastConfirmUtc = TimeSpan.FromHours(8.0) };
        ChochText.ForEveryModeCheck(breakout, mode).Validate();

        var parameters = new BotParameters
        {
            Symbol = "XAUUSD",
            Execution = execution,
            SessionRange = sessionRange,
            Choch = choch,
            Risk = new RiskParameters
            {
                RiskEpochDate = RiskData.Epoch,
                RiskPercent = 0.75,
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
    [Trait("Req", "PLT-020")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void Defaults_GoldThroughTheCore_Accepted(double pip)
    {
        BotParameters parameters = Defaults(pip);

        Assert.Equal(EntryMode.SessionRange, parameters.Mode);
        Assert.NotNull(parameters.SessionRange);
        Assert.Null(parameters.Choch);
        Assert.Null(parameters.Breakout);
        Assert.Equal(0.75, parameters.Risk.RiskPercent);
        Assert.False(parameters.Risk.LadderEnabled);
        Assert.Equal(SizingPlan.Ladder, parameters.Risk.SizingPlan);
        Assert.Equal(0.0, parameters.Risk.DrawdownBrakePercent);
        Assert.Equal(0.5, parameters.Risk.DrawdownBrakeFactor);
        Assert.Equal(TimeSpan.FromHours(24.0), parameters.Execution.SessionEndUtc);
        Assert.Equal(SymbolProfileKind.Gold, parameters.SymbolProfile!.Profile);
    }

    [Theory]
    [Trait("Req", "PLT-020")]
    [Trait("Req", "SIG-035")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void Defaults_WithEntryModelChoch_RefusedNamingEntryModel(double pip)
    {
        var exception = Assert.Throws<ArgumentException>(() => Defaults(pip, "Choch"));

        Assert.Equal("EntryModel", exception.ParamName);
    }

    [Theory]
    [Trait("Req", "PLT-020")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void Defaults_GoldHash_TheHashOfDeployReadme(double pip)
    {
        BotParameters parameters = Defaults(pip);

        Assert.Equal("2dea1bc3", ParameterHash.Compute(parameters.Signal, parameters.Execution, null, parameters.SessionRange, null, parameters.SymbolProfile));
    }

    [Fact]
    [Trait("Req", "PLT-020")]
    public void Defaults_ForexFileHash_TheHashOfDeployReadme()
    {
        SymbolProfileResult profile = SymbolProfile.Resolve("EURUSD", "EUR", "USD", 0.0001, 0.0, 0.0, 0.0, 0.0, 0.0);
        var execution = new ExecutionParameters { SessionStartUtc = TimeSpan.Zero, SessionEndUtc = TimeSpan.FromHours(24.0), MaxSpreadPips = profile.MaxSpreadPips, MinStopPips = profile.MinStopPips };

        Assert.Equal("3280d399", ParameterHash.Compute(new SignalParameters(), execution, null, new SessionRangeParameters { RangeTrendFastEma = 20 }, null, profile));
    }

    // ------------------------------------------------------------------ the start line and the hash

    private static string StartLine(Func<RiskParameters, RiskParameters> risk)
    {
        BotParameters parameters = SessionRangeBotData.Parameters();
        BotHarness h = SessionRangeBotData.Started(SessionRangeBotData.Utc(2026, 3, 4, 6, 0), SessionRangeBotData.Bars(SessionRangeBotData.Winter), parameters: parameters with { Risk = risk(parameters.Risk) });
        return Assert.Single(h.Journal.Lines("start"));
    }

    private static RiskParameters NewDefaults(RiskParameters risk)
    {
        return risk with { RiskPercent = 0.75, DrawdownBrakePercent = 3.0, DrawdownBrakeFactor = 0.5, LadderEnabled = false, SizingPlan = SizingPlan.Ladder };
    }

    [Fact]
    [Trait("Req", "PLT-020")]
    [Trait("Req", "RSK-019")]
    public void StartLine_FixedSizingWithTheBrake_EndsWithBrake3Slash0point5()
    {
        string line = StartLine(NewDefaults);

        Assert.EndsWith(" mode=SessionRange brake=3/0.5", line, StringComparison.Ordinal);
        Assert.Single(line.Split(' '), part => part.StartsWith("brake=", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "PLT-020")]
    [Trait("Req", "RSK-019")]
    public void StartLine_FixedSizingBrakeOff_EndsWithBrakeOff()
    {
        string line = StartLine(r => NewDefaults(r) with { DrawdownBrakePercent = 0.0 });

        Assert.EndsWith(" mode=SessionRange brake=off", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PLT-020")]
    public void StartLine_OtherBrakeValues_WrittenInvariantR()
    {
        string line = StartLine(r => NewDefaults(r) with { DrawdownBrakePercent = 2.5, DrawdownBrakeFactor = 0.25 });

        Assert.EndsWith(" brake=2.5/0.25", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PLT-020")]
    public void StartLine_LadderOnAndRecovery_NoBrakePart_TheFixedLineIsTheLadderLinePlusTheBrake()
    {
        string fixedLine = StartLine(NewDefaults);
        string ladder = StartLine(r => NewDefaults(r) with { LadderEnabled = true });
        string recovery = StartLine(r => NewDefaults(r) with { SizingPlan = SizingPlan.Recovery });

        Assert.DoesNotContain("brake=", ladder, StringComparison.Ordinal);
        Assert.DoesNotContain("brake=", recovery, StringComparison.Ordinal);
        Assert.EndsWith(" mode=SessionRange", ladder, StringComparison.Ordinal);
        Assert.EndsWith(" mode=SessionRange sizing=Recovery", recovery, StringComparison.Ordinal);
        Assert.Equal(ladder + " brake=3/0.5", fixedLine);
        Assert.Equal(ladder + " sizing=Recovery", recovery);
    }

    [Fact]
    [Trait("Req", "PLT-020")]
    public void Hash_SessionRangeWithTheNewRiskDefaults_Unchanged3f0f30a3()
    {
        string line = StartLine(NewDefaults);

        Assert.Contains(" hash=" + SessionRangeBotData.Hash + " ", line, StringComparison.Ordinal);
        Assert.Equal("3f0f30a3", SessionRangeBotData.Hash);
        Assert.Equal("3f0f30a3", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, new SessionRangeParameters()));
    }
}
