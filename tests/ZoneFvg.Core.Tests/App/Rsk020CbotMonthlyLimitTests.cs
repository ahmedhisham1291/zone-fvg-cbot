using Xunit;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-020 in the cBot and the parameter files (spec v0.19, row BF): the declaration of
/// MonthlyLossLimitPercent with default 2.0 in the Risk group directly after
/// DailyLossLimitPercent, its assignment in <c>BuildRisk</c>, and the line
/// <c>MonthlyLossLimitPercent=2.0</c> once in every file under <c>deploy/params</c>, in the cBot's
/// order.
/// </summary>
public class Rsk020CbotMonthlyLimitTests
{
    private const string Declaration = "[Parameter(\"MonthlyLossLimitPercent\", DefaultValue = 2.0, Group = \"Risk\")]";

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
    [Trait("Req", "RSK-020")]
    public void Bot_DeclaresTheLimitOnceWithDefault2_DirectlyAfterDailyLossLimitPercent()
    {
        string source = BotSource();
        int at = source.IndexOf(Declaration, StringComparison.Ordinal);
        int daily = source.IndexOf("[Parameter(\"DailyLossLimitPercent\"", StringComparison.Ordinal);
        int next = source.IndexOf("[Parameter(\"MaxDrawdownPercent\"", StringComparison.Ordinal);

        Assert.True(at > 0);
        Assert.Equal(at, source.LastIndexOf(Declaration, StringComparison.Ordinal));
        Assert.Single(source.Split("[Parameter(\"MonthlyLossLimitPercent\"", StringSplitOptions.None).Skip(1));
        Assert.True(daily > 0 && daily < at && at < next);
        Assert.DoesNotContain("[Parameter(", source.Substring(daily + 1, at - daily - 1), StringComparison.Ordinal);
        Assert.DoesNotContain("[Parameter(", source.Substring(at + 1, next - at - 1), StringComparison.Ordinal);
        Assert.Contains("public double MonthlyLossLimitPercent { get; set; }", source.Substring(at, next - at), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void BuildRisk_PassesTheLimitToTheCore()
    {
        string source = BotSource();
        int start = source.IndexOf("private RiskParameters BuildRisk(", StringComparison.Ordinal);
        int end = source.IndexOf("private NewsParameters BuildNews(", StringComparison.Ordinal);

        Assert.True(start > 0 && end > start);
        string body = source.Substring(start, end - start);
        Assert.Contains("MonthlyLossLimitPercent = MonthlyLossLimitPercent,", body, StringComparison.Ordinal);
        Assert.Single(body.Split("MonthlyLossLimitPercent = ", StringSplitOptions.None).Skip(1));
    }

    [Fact]
    [Trait("Req", "RSK-020")]
    public void ParameterFiles_CarryTheLineOnceDirectlyAfterDailyLossLimitPercent()
    {
        foreach (string name in Files)
        {
            string[] lines = ParameterFile(name);

            Assert.Single(lines, l => l.StartsWith("MonthlyLossLimitPercent=", StringComparison.Ordinal));
            int at = Array.IndexOf(lines, "MonthlyLossLimitPercent=2.0");
            Assert.True(at > 0, name);
            Assert.Equal("DailyLossLimitPercent=2.0", lines[at - 1]);
            Assert.Equal("MaxDrawdownPercent=8.0", lines[at + 1]);
        }
    }
}
