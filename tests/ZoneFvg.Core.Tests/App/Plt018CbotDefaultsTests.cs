using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// PLT-018 (and the changed texts of PLT-015, PLT-016, PLT-017): the defaults of the owner's request
/// of 2026-11-24 in the cBot. What can be checked without cTrader is checked here: the cBot's
/// parameter declarations and <c>BuildParameters</c> in its source, the five parameter files under
/// <c>deploy/params</c>, and the cBot's default values run through the core's own functions.
/// </summary>
public class Plt018CbotDefaultsTests
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

    private static string Slice(string source, string from, string to)
    {
        int start = source.IndexOf(from, StringComparison.Ordinal);
        int end = source.IndexOf(to, start + 1, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, from);
        return source.Substring(start, end - start);
    }

    [Theory]
    [Trait("Req", "PLT-018")]
    [Trait("Req", "PLT-015")]
    [Trait("Req", "PLT-016")]
    [Trait("Req", "PLT-017")]
    [InlineData("[Parameter(\"EntryMode\", DefaultValue = \"SessionRange\", Group = \"Entry mode\")]")]
    [InlineData("[Parameter(\"EntryModel\", DefaultValue = \"Fvg\", Group = \"Entry model\")]")]
    [InlineData("[Parameter(\"ChochUseBias\", DefaultValue = true, Group = \"Entry model\")]")]
    [InlineData("[Parameter(\"ChochMinStopPips\", DefaultValue = 0.0, Group = \"Entry model\")]")]
    [InlineData("[Parameter(\"SessionStartUtc\", DefaultValue = \"00:00\", Group = \"Execution\")]")]
    [InlineData("[Parameter(\"SessionEndUtc\", DefaultValue = \"24:00\", Group = \"Execution\")]")]
    [InlineData("[Parameter(\"MinStopPips\", DefaultValue = 0.0, Group = \"Execution\")]")]
    [InlineData("[Parameter(\"MaxSpreadPips\", DefaultValue = 0.0, Group = \"Execution\")]")]
    [InlineData("[Parameter(\"MaxVolumeUnits\", DefaultValue = 0.0, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"LadderEnabled\", DefaultValue = false, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"RiskPercent\", DefaultValue = 0.75, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"SizingPlan\", DefaultValue = \"Ladder\", Group = \"Risk\")]")]
    [InlineData("[Parameter(\"InitialCapital\", DefaultValue = 100000.0, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"NewsEnabled\", DefaultValue = true, Group = \"News\")]")]
    public void Bot_Defaults_AreThoseOfSpecV014(string declaration)
    {
        string source = BotSource();
        int at = source.IndexOf(declaration, StringComparison.Ordinal);

        Assert.True(at > 0, declaration);
        Assert.Equal(at, source.LastIndexOf(declaration, StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Req", "PLT-018")]
    [InlineData("public string EntryMode { get; set; } = \"SessionRange\";")]
    [InlineData("public string EntryModel { get; set; } = \"Fvg\";")]
    [InlineData("public string SessionStartUtc { get; set; } = \"00:00\";")]
    [InlineData("public string SessionEndUtc { get; set; } = \"24:00\";")]
    public void Bot_TextDefaults_AlsoInTheInitialisers(string declaration)
    {
        Assert.Contains(declaration, BotSource(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PLT-018")]
    [InlineData("SymbolProfileResult profile = SymbolProfile.Resolve(SymbolName, Symbol.BaseAsset.Name, Symbol.QuoteAsset.Name, Symbol.PipSize, Symbol.Spread, MaxSpreadPips, MinStopPips, ChochMinStopPips, MaxVolumeUnits);")]
    [InlineData("MaxSpreadPips = profile.MaxSpreadPips;")]
    [InlineData("MinStopPips = profile.MinStopPips;")]
    [InlineData("ChochMinStopPips = profile.ChochMinStopPips;")]
    [InlineData("MaxVolumeUnits = profile.MaxVolumeUnits;")]
    public void BuildParameters_ResolvesTheProfileBeforeBuildSignal(string statement)
    {
        string head = Slice(BotSource(), "private BotParameters BuildParameters(", "SignalParameters signal = BuildSignal();");

        Assert.Contains(statement, head, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PLT-018")]
    [InlineData("private BotParameters BuildParameters(", "private SignalParameters BuildSignal(", "SymbolProfile = profile,")]
    [InlineData("private ExecutionParameters BuildExecution(", "private RiskParameters BuildRisk(", "MaxSpreadPips = MaxSpreadPips,")]
    [InlineData("private ExecutionParameters BuildExecution(", "private RiskParameters BuildRisk(", "MinStopPips = MinStopPips,")]
    [InlineData("private RiskParameters BuildRisk(", "private NewsParameters BuildNews(", "MaxVolumeUnits = MaxVolumeUnits,")]
    [InlineData("private RiskParameters BuildRisk(", "private NewsParameters BuildNews(", "LadderEnabled = LadderEnabled,")]
    [InlineData("private BotParameters BuildParameters(", "private SignalParameters BuildSignal(", "ChochTargetBufferAtr, ChochMinStopPips, MinRewardRisk);")]
    [InlineData("protected override void OnStart()", "private void LogWarmup(", "parameters.SymbolProfile?.SpreadNote")]
    public void Bot_EffectiveValuesReachTheCore(string from, string to, string text)
    {
        Assert.Contains(text, Slice(BotSource(), from, to), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PLT-018")]
    [Trait("Req", "PLT-016")]
    [Trait("Req", "PLT-017")]
    public void ParameterFiles_CarryTheDefaultsOfSpecV014()
    {
        string[] expected =
        {
            "EntryMode=SessionRange", "EntryModel=Fvg", "ChochUseBias=true", "SessionStartUtc=00:00", "SessionEndUtc=24:00", "MaxSpreadPips=0.0",
            "MinStopPips=0.0", "ChochMinStopPips=0.0", "MaxVolumeUnits=0.0", "LadderEnabled=false",
        };
        foreach (string name in Files)
        {
            string[] lines = ParameterFile(name);
            foreach (string line in expected)
            {
                Assert.Single(lines, l => l == line);
            }

            Assert.Equal("LadderEnabled=false", lines[Array.IndexOf(lines, "MaxVolumeUnits=0.0") + 1]);
        }
    }

    [Fact]
    [Trait("Req", "PLT-018")]
    public void ParameterFiles_GoldEqualsTheForexFilesApartFromTheHeartbeat()
    {
        string[] Without(string name) => ParameterFile(name).Where(l => !l.StartsWith("HeartbeatUrl=", StringComparison.Ordinal)).ToArray();

        string[] eurusd = Without("eurusd");
        foreach (string name in Files)
        {
            Assert.Equal(eurusd, Without(name));
        }
    }

    // ------------------------------------------------------------------ the defaults through the core

    private static BotParameters Defaults(string symbol, string baseAsset, string quote, double pip, EntryMode mode = EntryMode.ZoneFvg, string model = "Choch", string start = "00:00", string end = "24:00")
    {
        TimeSpan startUtc = ChochText.SessionStartUtc(start);
        TimeSpan endUtc = ChochText.SessionEndUtc(end);
        SymbolProfileResult profile = SymbolProfile.Resolve(symbol, baseAsset, quote, pip, 0.0, 0.0, 0.0, 0.0, 0.0);
        ChochParameters? choch = ChochText.Build(model, mode, false, 32, true, false, 0.1, profile.ChochMinStopPips, 2.0);
        var execution = new ExecutionParameters { SessionStartUtc = startUtc, SessionEndUtc = endUtc, MaxSpreadPips = profile.MaxSpreadPips, MinStopPips = profile.MinStopPips };
        var breakout = new BreakoutParameters { WindowStartUtc = startUtc, WindowEndUtc = endUtc, TrendSlowEmaPeriod = 200, LastConfirmUtc = TimeSpan.FromHours(8.0) };
        if (mode != EntryMode.Breakout)
        {
            ChochText.ForEveryModeCheck(breakout, mode).Validate();
        }

        var parameters = new BotParameters
        {
            Symbol = symbol,
            Execution = execution,
            Breakout = mode == EntryMode.Breakout ? breakout : null,
            Choch = choch,
            Risk = RiskData.Parameters with { LadderEnabled = false, SizingPlan = SizingPlan.Ladder, InitialCapital = 200000.0, MaxVolumeUnits = profile.MaxVolumeUnits },
            SymbolProfile = profile,
        };
        parameters.Validate(isBacktest: true);
        return parameters;
    }

    [Theory]
    [Trait("Req", "PLT-018")]
    [InlineData("EURUSD", "EUR", "USD", 0.0001)]
    [InlineData("USDJPY", "USD", "JPY", 0.01)]
    [InlineData("XAUUSD", "XAU", "USD", 1.0)]
    [InlineData("XAUUSD", "XAU", "USD", 0.01)]
    public void Defaults_RunThroughTheCore_Accepted(string symbol, string baseAsset, string quote, double pip)
    {
        BotParameters parameters = Defaults(symbol, baseAsset, quote, pip);

        Assert.Equal(EntryMode.ZoneFvg, parameters.Mode);
        Assert.True(parameters.Choch!.ChochUseBias);
        Assert.False(parameters.Risk.LadderEnabled);
        Assert.Equal(SizingPlan.Ladder, parameters.Risk.SizingPlan);
        Assert.Equal(TimeSpan.FromHours(24.0), parameters.Execution.SessionEndUtc);
    }

    [Fact]
    [Trait("Req", "PLT-018")]
    public void Defaults_WithEntryModeBreakout_RefusedNamingEntryModel()
    {
        var exception = Assert.Throws<ArgumentException>(() => Defaults("EURUSD", "EUR", "USD", 0.0001, EntryMode.Breakout));

        Assert.Equal("EntryModel", exception.ParamName);
    }

    [Fact]
    [Trait("Req", "PLT-018")]
    public void Defaults_BreakoutWithFvgAllDay_RefusedForTheWindowEnd()
    {
        // The adapter names WindowEndUtc as SessionEndUtc (ParameterMessage, PLT-012).
        var exception = Assert.ThrowsAny<ArgumentException>(() => Defaults("EURUSD", "EUR", "USD", 0.0001, EntryMode.Breakout, "Fvg"));

        Assert.Equal("WindowEndUtc", exception.ParamName);
    }

    [Fact]
    [Trait("Req", "PLT-018")]
    public void Defaults_BreakoutWithFvgAnd0700To1700_Accepted()
    {
        BotParameters parameters = Defaults("EURUSD", "EUR", "USD", 0.0001, EntryMode.Breakout, "Fvg", "07:00", "17:00");

        Assert.Equal(EntryMode.Breakout, parameters.Mode);
        Assert.Null(parameters.Choch);
    }
}
