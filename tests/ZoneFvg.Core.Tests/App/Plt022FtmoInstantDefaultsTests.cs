using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// PLT-022: the FTMO Instant defaults in the cBot (spec v0.20 rows BG to BM). What can be checked
/// without cTrader is checked here: the cBot's declarations and <c>BuildRisk</c> in its source, the
/// six parameter files under <c>deploy/params</c>, the two texts read by the core
/// (<see cref="FtmoInstantText"/>), the core's validation of the new values and the cBot's default
/// values run through the core.
/// </summary>
public class Plt022FtmoInstantDefaultsTests
{
    private static readonly string[] Files = { "audusd", "eurusd", "gbpusd", "usdjpy", "xauusd", "xauusd-overnight" };

    /// <summary>The lines of row BM, in the cBot's order.</summary>
    private static readonly string[] RowBm =
    {
        "InitialCapital=100000.0", "FtmoDailyLossPercent=3.0", "FtmoMaxLossPercent=10.0", "FtmoMaxLossMode=TrailingBalance",
        "FtmoGuardBufferPercent=0.5", "ConsistencyMaxPercent=30.0", "RewardPeriodStartDate=",
    };

    /// <summary>The declarations of PLT-022, in the cBot's order.</summary>
    private static readonly string[] Declarations =
    {
        "[Parameter(\"InitialCapital\", DefaultValue = 100000.0, Group = \"Risk\")]",
        "[Parameter(\"FtmoDailyLossPercent\", DefaultValue = 3.0, Group = \"Risk\")]",
        "[Parameter(\"FtmoMaxLossPercent\", DefaultValue = 10.0, Group = \"Risk\")]",
        "[Parameter(\"FtmoMaxLossMode\", DefaultValue = \"TrailingBalance\", Group = \"Risk\")]",
        "[Parameter(\"FtmoGuardBufferPercent\", DefaultValue = 0.5, Group = \"Risk\")]",
        "[Parameter(\"ConsistencyMaxPercent\", DefaultValue = 30.0, Group = \"Risk\")]",
        "[Parameter(\"RewardPeriodStartDate\", DefaultValue = \"\", Group = \"Risk\")]",
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

    private static string BuildRiskBody()
    {
        string source = BotSource();
        int start = source.IndexOf("private RiskParameters BuildRisk(", StringComparison.Ordinal);
        int end = source.IndexOf("private NewsParameters BuildNews(", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        return source.Substring(start, end - start);
    }

    // ------------------------------------------------------------------ the cBot source

    [Fact]
    [Trait("Req", "PLT-022")]
    public void Bot_TheDefaultsOfPlt022_DeclaredOnceEachInTheOrderOfRowBm()
    {
        string source = BotSource();
        int previous = -1;
        foreach (string declaration in Declarations)
        {
            int at = source.IndexOf(declaration, StringComparison.Ordinal);
            Assert.True(at > previous, declaration);
            Assert.Equal(at, source.LastIndexOf(declaration, StringComparison.Ordinal));
            previous = at;
        }
    }

    [Theory]
    [Trait("Req", "PLT-022")]
    [InlineData("InitialCapital")]
    [InlineData("FtmoDailyLossPercent")]
    [InlineData("FtmoMaxLossPercent")]
    [InlineData("FtmoMaxLossMode")]
    [InlineData("FtmoGuardBufferPercent")]
    [InlineData("ConsistencyMaxPercent")]
    [InlineData("RewardPeriodStartDate")]
    public void Bot_EachParameter_DeclaredOnce(string name)
    {
        Assert.Single(BotSource().Split("[Parameter(\"" + name + "\"", StringSplitOptions.None).Skip(1));
    }

    [Fact]
    [Trait("Req", "PLT-022")]
    public void Bot_TheNewParameters_FollowEachOtherAfterFtmoMaxLossPercent()
    {
        string source = BotSource();
        int from = source.IndexOf(Declarations[2], StringComparison.Ordinal);
        int to = source.IndexOf(Declarations[6], StringComparison.Ordinal);
        string between = source.Substring(from, to - from);

        Assert.Equal(4, between.Split("[Parameter(", StringSplitOptions.None).Length - 1);
        Assert.Contains("[Parameter(\"MaxRequestsPerDay\"", source.Substring(to, Math.Min(800, source.Length - to)), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PLT-022")]
    [InlineData("public string FtmoMaxLossMode { get; set; } = \"TrailingBalance\";")]
    [InlineData("public string RewardPeriodStartDate { get; set; } = string.Empty;")]
    [InlineData("public double ConsistencyMaxPercent { get; set; }")]
    public void Bot_TheNewProperties_Declared(string declaration)
    {
        Assert.Single(BotSource().Split(declaration, StringSplitOptions.None).Skip(1));
    }

    [Theory]
    [Trait("Req", "PLT-022")]
    [InlineData("InitialCapital = InitialCapital,")]
    [InlineData("FtmoDailyLossPercent = FtmoDailyLossPercent,")]
    [InlineData("FtmoMaxLossPercent = FtmoMaxLossPercent,")]
    [InlineData("FtmoMaxLossMode = FtmoInstantText.MaxLossMode(FtmoMaxLossMode),")]
    [InlineData("FtmoGuardBufferPercent = FtmoGuardBufferPercent,")]
    [InlineData("ConsistencyMaxPercent = ConsistencyMaxPercent,")]
    [InlineData("RewardPeriodStartDate = FtmoInstantText.RewardPeriodStart(RewardPeriodStartDate),")]
    public void BuildRisk_AssignsTheValueOnce(string assignment)
    {
        Assert.Single(BuildRiskBody().Split(assignment, StringSplitOptions.None).Skip(1));
    }

    // ------------------------------------------------------------------ the parameter files

    [Fact]
    [Trait("Req", "PLT-022")]
    public void ParameterFiles_CarryEachLineOfRowBmOnceInTheCbotsOrder()
    {
        foreach (string name in Files)
        {
            string[] lines = ParameterFile(name);
            int previous = -1;
            foreach (string line in RowBm)
            {
                Assert.Single(lines, l => l == line);
                string key = line.Substring(0, line.IndexOf('=') + 1);
                Assert.Single(lines, l => l.StartsWith(key, StringComparison.Ordinal));
                int at = Array.IndexOf(lines, line);
                Assert.True(at > previous, name + ": " + line);
                previous = at;
            }

            int max = Array.IndexOf(lines, "FtmoMaxLossPercent=10.0");
            Assert.Equal(RowBm.Skip(3).ToArray(), lines.Skip(max + 1).Take(4).ToArray());
            Assert.Equal("MaxRequestsPerDay=100", lines[max + 5]);
            Assert.Equal("DayResetZone=Europe/Prague", lines[Array.IndexOf(lines, "InitialCapital=100000.0") + 1]);
        }
    }

    [Fact]
    [Trait("Req", "PLT-022")]
    public void ParameterFiles_EqualApartFromTheLinesThatDifferByDesign()
    {
        string[] Without(string name) => ParameterFile(name).Where(l => !l.StartsWith("HeartbeatUrl=", StringComparison.Ordinal)).ToArray();

        string[] gold = Without("xauusd");
        foreach (string name in Files.Where(n => n != "xauusd-overnight"))
        {
            Assert.Equal(gold, Without(name));
        }

        string[] night = Without("xauusd-overnight");
        Assert.Equal(gold.Length, night.Length);
        string[] differences = night.Where((line, i) => line != gold[i]).ToArray();
        Assert.Equal(new[] { "EntryMode=Overnight", "RiskPercent=1.0" }, differences);
    }

    // ------------------------------------------------------------------ the two texts read by the core

    [Theory]
    [Trait("Req", "PLT-022")]
    [InlineData("2026-13-01")]
    [InlineData("27.11.2026")]
    [InlineData("2026-11-27 00:00")]
    [InlineData("2026-1-5")]
    [InlineData("CHANGE_ME_yyyy-MM-dd")]
    public void RewardPeriodStart_BadText_RefusedNamingIt(string text)
    {
        var exception = Assert.Throws<ArgumentException>(() => FtmoInstantText.RewardPeriodStart(text));

        Assert.Equal("RewardPeriodStartDate", exception.ParamName);
        Assert.StartsWith("RewardPeriodStartDate must be empty or a date written as yyyy-MM-dd, for example 2026-12-15.", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PLT-022")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RewardPeriodStart_Empty_IsNull(string? text)
    {
        Assert.Null(FtmoInstantText.RewardPeriodStart(text));
    }

    [Fact]
    [Trait("Req", "PLT-022")]
    public void RewardPeriodStart_ADate_IsThatDateAtMidnightKindUtc()
    {
        DateTime? date = FtmoInstantText.RewardPeriodStart("2026-12-15");

        Assert.Equal(new DateTime(2026, 12, 15, 0, 0, 0, DateTimeKind.Utc), date);
        Assert.Equal(DateTimeKind.Utc, date!.Value.Kind);
    }

    // ------------------------------------------------------------------ validation of the new values

    [Theory]
    [Trait("Req", "PLT-022")]
    [Trait("Req", "OPS-012")]
    [InlineData(-0.1)]
    [InlineData(100.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validate_ConsistencyMaxPercentOutOfRange_RefusedNamingIt(double value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => (RiskData.Parameters with { ConsistencyMaxPercent = value }).Validate());

        Assert.Equal("ConsistencyMaxPercent", exception.ParamName);
        Assert.StartsWith("ConsistencyMaxPercent must be at least 0 and at most 100 but was ", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PLT-022")]
    [Trait("Req", "OPS-012")]
    [InlineData(0.0)]
    [InlineData(30.0)]
    [InlineData(100.0)]
    public void Validate_ConsistencyMaxPercentInRange_Accepted(double value)
    {
        Assert.Null(Record.Exception(() => (RiskData.Parameters with { ConsistencyMaxPercent = value }).Validate()));
    }

    [Fact]
    [Trait("Req", "PLT-022")]
    [Trait("Req", "OPS-012")]
    public void Validate_RewardPeriodStartDateNotADateOfKindUtc_RefusedNamingIt()
    {
        RiskParameters local = RiskData.Parameters with { RewardPeriodStartDate = new DateTime(2026, 12, 15, 0, 0, 0, DateTimeKind.Local) };
        RiskParameters withTime = RiskData.Parameters with { RewardPeriodStartDate = new DateTime(2026, 12, 15, 6, 0, 0, DateTimeKind.Utc) };

        Assert.Equal("RewardPeriodStartDate", Assert.Throws<ArgumentOutOfRangeException>(() => local.Validate()).ParamName);
        Assert.Equal("RewardPeriodStartDate", Assert.Throws<ArgumentOutOfRangeException>(() => withTime.Validate()).ParamName);
        Assert.Null(Record.Exception(() => (RiskData.Parameters with { RewardPeriodStartDate = new DateTime(2026, 12, 15, 0, 0, 0, DateTimeKind.Utc) }).Validate()));
    }

    // ------------------------------------------------------------------ the defaults through the core

    private static BotParameters Defaults(double pip, string reward = "")
    {
        const EntryMode mode = EntryMode.SessionRange;
        TimeSpan startUtc = ChochText.SessionStartUtc("00:00");
        TimeSpan endUtc = ChochText.SessionEndUtc("24:00");
        SymbolProfileResult profile = SymbolProfile.Resolve("XAUUSD", "XAU", "USD", pip, 0.0, 0.0, 0.0, 0.0, 0.0);
        SessionRangeParameters sessionRange = SessionRangeText.Build("19:00", "03:00", "12:00", "16:30", 0.5, 1.0, 20);
        ChochParameters? choch = ChochText.Build("Fvg", mode, false, 32, true, false, 0.1, profile.ChochMinStopPips, 2.0);
        var execution = new ExecutionParameters { SessionStartUtc = startUtc, SessionEndUtc = endUtc, MaxSpreadPips = profile.MaxSpreadPips, MinStopPips = profile.MinStopPips };

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
                DailyLossLimitPercent = 2.0,
                MonthlyLossLimitPercent = 2.0,
                MaxDrawdownPercent = 8.0,
                InitialCapital = 100000.0,
                FtmoDailyLossPercent = 3.0,
                FtmoMaxLossPercent = 10.0,
                FtmoMaxLossMode = FtmoInstantText.MaxLossMode("TrailingBalance"),
                FtmoGuardBufferPercent = 0.5,
                ConsistencyMaxPercent = 30.0,
                RewardPeriodStartDate = FtmoInstantText.RewardPeriodStart(reward),
                LadderEnabled = false,
                SizingPlan = SizingPlanText.Parse("Ladder"),
                MaxVolumeUnits = profile.MaxVolumeUnits,
            },
            SymbolProfile = profile,
        };
        parameters.Validate(isBacktest: true);
        return parameters;
    }

    [Theory]
    [Trait("Req", "PLT-022")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void Defaults_GoldThroughTheCore_AcceptedWithTrailingBalanceAnd100000(double pip)
    {
        BotParameters parameters = Defaults(pip);

        Assert.Equal(FtmoMaxLossMode.TrailingBalance, parameters.Risk.FtmoMaxLossMode);
        Assert.Equal(100000.0, parameters.Risk.InitialCapital);
        Assert.Equal(3.0, parameters.Risk.FtmoDailyLossPercent);
        Assert.Equal(10.0, parameters.Risk.FtmoMaxLossPercent);
        Assert.Equal(0.5, parameters.Risk.FtmoGuardBufferPercent);
        Assert.Equal(30.0, parameters.Risk.ConsistencyMaxPercent);
        Assert.Null(parameters.Risk.RewardPeriodStartDate);
        Assert.Equal(EntryMode.SessionRange, parameters.Mode);
    }

    [Theory]
    [Trait("Req", "PLT-022")]
    [InlineData(1.0)]
    [InlineData(0.01)]
    public void Defaults_WithARewardDate_Accepted(double pip)
    {
        Assert.Equal(new DateTime(2026, 12, 15, 0, 0, 0, DateTimeKind.Utc), Defaults(pip, "2026-12-15").Risk.RewardPeriodStartDate);
    }

    [Theory]
    [Trait("Req", "PLT-022")]
    [InlineData("2026-13-01")]
    [InlineData("27.11.2026")]
    public void Defaults_WithABadRewardDate_RefusedNamingIt(string reward)
    {
        Assert.Equal("RewardPeriodStartDate", Assert.Throws<ArgumentException>(() => Defaults(1.0, reward)).ParamName);
    }

    [Fact]
    [Trait("Req", "PLT-022")]
    [Trait("Req", "RSK-021")]
    public void Defaults_TheGuardActsAt2500ForTheDayAndAtPeakMinus9500()
    {
        RiskEngine engine = new(Defaults(1.0).Risk);
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0, 97500.0, 97500.0));

        EmergencyDecision decision = engine.EmergencyGuard(snapshot);

        Assert.Equal(97500.0, decision.DailyFloor, 1e-6);
        Assert.Equal(90500.0, decision.OverallFloor, 1e-6);
        Assert.Equal(EmergencyAction.CloseAllAndHalt, decision.Action);
    }

    [Fact]
    [Trait("Req", "PLT-022")]
    public void Defaults_RiskValuesAreNotInTheHash()
    {
        BotParameters parameters = Defaults(1.0);

        Assert.Equal("2dea1bc3", ParameterHash.Compute(parameters.Signal, parameters.Execution, null, parameters.SessionRange, null, parameters.SymbolProfile));
    }
}
