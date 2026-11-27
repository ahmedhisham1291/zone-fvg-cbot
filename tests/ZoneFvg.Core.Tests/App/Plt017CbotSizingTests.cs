using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// PLT-017: the sizing plan, the sanity, open-risk and currency caps, the size cap and the account
/// defaults in the cBot. What can be checked without cTrader is checked here: the cBot's parameter
/// declarations and <c>BuildRisk</c> in its source, the five parameter files under
/// <c>deploy/params</c>, the text of SizingPlan (read by the core) and the core's start line.
/// </summary>
public class Plt017CbotSizingTests
{
    private static readonly string[] NewParameters =
    {
        "[Parameter(\"SizingPlan\", DefaultValue = \"Ladder\", Group = \"Risk\")]",
        "[Parameter(\"RecoveryRunLossPercent\", DefaultValue = 1.0, Group = \"Risk\")]",
        "[Parameter(\"RecoveryMaxRiskPercent\", DefaultValue = 1.28, Group = \"Risk\")]",
        "[Parameter(\"SanityCapEnabled\", DefaultValue = false, Group = \"Risk\")]",
        "[Parameter(\"SanityCapPercent\", DefaultValue = 0.55, Group = \"Risk\")]",
        "[Parameter(\"OpenRiskCapEnabled\", DefaultValue = false, Group = \"Risk\")]",
        "[Parameter(\"CurrencyCapEnabled\", DefaultValue = false, Group = \"Risk\")]",
    };

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

    [Fact]
    [Trait("Req", "PLT-017")]
    public void Bot_SevenNewParameters_InTheGroupRiskAfterMaxVolumeUnitsAndTheLadderValues()
    {
        string source = BotSource();
        int previous = source.IndexOf("[Parameter(\"MaxRiskPercentPerTrade\", DefaultValue = 0.5, Group = \"Risk\")]", StringComparison.Ordinal);
        Assert.True(source.IndexOf("[Parameter(\"MaxVolumeUnits\"", StringComparison.Ordinal) < previous);
        Assert.True(source.IndexOf("[Parameter(\"LadderEnabled\"", StringComparison.Ordinal) < previous);
        foreach (string declaration in NewParameters)
        {
            int at = source.IndexOf(declaration, StringComparison.Ordinal);
            Assert.True(at > previous, declaration);
            Assert.Equal(at, source.LastIndexOf(declaration, StringComparison.Ordinal));
            previous = at;
        }
    }

    [Theory]
    [Trait("Req", "PLT-017")]
    [InlineData("[Parameter(\"InitialCapital\", DefaultValue = 200000.0, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"DailyLossLimitPercent\", DefaultValue = 2.0, Group = \"Risk\")]")]
    [InlineData("[Parameter(\"MaxVolumeUnits\", DefaultValue = 0.0, Group = \"Risk\")]")]
    public void Bot_AccountDefaults_AreThoseOfSpecV012(string declaration)
    {
        Assert.Contains(declaration, BotSource(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "PLT-017")]
    [InlineData("SizingPlan = SizingPlanText.Parse(SizingPlan),")]
    [InlineData("RecoveryRunLossPercent = RecoveryRunLossPercent,")]
    [InlineData("RecoveryMaxRiskPercent = RecoveryMaxRiskPercent,")]
    [InlineData("SanityCapEnabled = SanityCapEnabled,")]
    [InlineData("SanityCapPercent = SanityCapPercent,")]
    [InlineData("OpenRiskCapEnabled = OpenRiskCapEnabled,")]
    [InlineData("CurrencyCapEnabled = CurrencyCapEnabled,")]
    [InlineData("InitialCapital = InitialCapital,")]
    [InlineData("DailyLossLimitPercent = DailyLossLimitPercent,")]
    [InlineData("MaxVolumeUnits = MaxVolumeUnits,")]
    public void BuildRisk_PassesTheValueToRiskParameters(string assignment)
    {
        string source = BotSource();
        int start = source.IndexOf("private RiskParameters BuildRisk(", StringComparison.Ordinal);
        int end = source.IndexOf("private NewsParameters BuildNews(", StringComparison.Ordinal);

        Assert.True(start > 0 && end > start);
        Assert.Contains(assignment, source.Substring(start, end - start), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PLT-017")]
    public void ParameterFiles_CarryTheNewLinesWithTheirDefaults()
    {
        foreach (string name in Files)
        {
            string[] lines = ParameterFile(name);
            string volume = "MaxVolumeUnits=0.0";
            string[] expected =
            {
                "InitialCapital=200000.0", "DailyLossLimitPercent=2.0", volume, "SizingPlan=Ladder", "RecoveryRunLossPercent=1.0", "RecoveryMaxRiskPercent=1.28",
                "SanityCapEnabled=false", "SanityCapPercent=0.55", "OpenRiskCapEnabled=false", "CurrencyCapEnabled=false",
            };
            foreach (string line in expected)
            {
                Assert.Single(lines, l => l == line);
            }

            int key = Array.IndexOf(lines, volume);
            Assert.Equal("LadderEnabled=false", lines[key + 1]);
            Assert.Equal("SizingPlan=Ladder", lines[key + 2]);
        }
    }

    [Theory]
    [Trait("Req", "PLT-017")]
    [InlineData("Ladder", SizingPlan.Ladder)]
    [InlineData("Recovery", SizingPlan.Recovery)]
    public void SizingPlanText_TheCBotValues_AreRead(string text, SizingPlan plan)
    {
        Assert.Equal(plan, SizingPlanText.Parse(text));
    }

    [Theory]
    [Trait("Req", "PLT-017")]
    [InlineData("recovery")]
    [InlineData("")]
    [InlineData(null)]
    public void SizingPlanText_OtherText_IsRefusedNamingTheParameterAsCTraderShowsIt(string? text)
    {
        var exception = Assert.Throws<ArgumentException>(() => SizingPlanText.Parse(text));

        Assert.Equal("SizingPlan", exception.ParamName);
        Assert.StartsWith("SizingPlan must be one of: Ladder, Recovery.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "PLT-017")]
    public void StartLine_RecoveryEndsWithSizingRecovery_LadderUnchanged()
    {
        var ladder = new BotHarness();
        ladder.Start(RecoveryData.Activation);
        var recovery = new BotHarness();
        recovery.Start(RecoveryData.Activation, RecoveryData.Parameters() with { Risk = RiskData.Parameters with { SizingPlan = SizingPlan.Recovery } });

        string ladderLine = Assert.Single(ladder.Journal.Lines("start"));
        string recoveryLine = Assert.Single(recovery.Journal.Lines("start"));

        Assert.DoesNotContain("sizing=", ladderLine, StringComparison.Ordinal);
        Assert.EndsWith(" brake=off", ladderLine, StringComparison.Ordinal);
        Assert.Equal(ladderLine[..^" brake=off".Length] + " sizing=Recovery", recoveryLine);
        Assert.Contains(" hash=" + RecoveryData.Hash + " ", recoveryLine, StringComparison.Ordinal);
    }
}
