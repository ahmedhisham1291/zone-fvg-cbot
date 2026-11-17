using System.Text;
using Xunit;
using ZoneFvg.Core.Simulation;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-005, owner answer of 2026-11-17 on pull request 50, through <c>sim accept</c>: N is every
/// symbol whose log is given to the tool. A log without trades (only the header row) counts as one
/// symbol that is not profitable; development logs without any trade are refused with a clear
/// message instead of a report. The sample logs hold 40 EURUSD and 40 GBPUSD trades.
/// </summary>
public class AcceptCommandSymbolsTests
{
    private const string EurUsd = "sample-eurusd.csv";
    private const string GbpUsd = "sample-gbpusd.csv";
    private const string EmptyXau = "empty-xauusd.csv";
    private const string EmptyUsdJpy = "empty-usdjpy.csv";
    private const string EmptyAudUsd = "empty-audusd.csv";

    private static readonly Dictionary<string, string> Files = new()
    {
        [EurUsd] = Sample(EurUsd),
        [GbpUsd] = Sample(GbpUsd),
        [EmptyXau] = TradeLogCsv.Header + "\n",
        [EmptyUsdJpy] = TradeLogCsv.Header + "\n",
        [EmptyAudUsd] = TradeLogCsv.Header + "\n",
    };

    private static string Sample(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Sim", name), Encoding.UTF8);
    }

    private static SimCommandResult Run(params string[] args)
    {
        return SimCommandLine.Run(args, path => Files.TryGetValue(path, out string? text) ? text : throw new FileNotFoundException("No such file.", path));
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Accept_TwoLogsWithTradesAndOneEmpty_NIsThree()
    {
        SimCommandResult result = Run("accept", "--dev", EurUsd, "--dev", GbpUsd, "--dev", EmptyXau, "--seed", "3", "--runs", "50");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"\| Profitable symbols \| Development \| [0-2] of 3 \| 3 of 3 \| FAIL \|", result.Output);
        Assert.Contains("| Profitable symbols | Holdout | no holdout log | 3 of 3 | NOT EVALUATED |", result.Output);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Accept_FiveLogsThreeOfThemEmpty_NIsFiveAndTheEmptyOnesAreNotProfitable()
    {
        SimCommandResult result = Run("accept", "--dev", EurUsd, "--dev", GbpUsd, "--dev", EmptyUsdJpy, "--dev", EmptyAudUsd, "--dev", EmptyXau, "--seed", "3", "--runs", "50");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"\| Profitable symbols \| Development \| [0-2] of 5 \| 4 of 5 \| FAIL \|", result.Output);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Accept_HoldoutLogs_CountTheirOwnSymbolsAgainstTheDevelopmentThreshold()
    {
        SimCommandResult result = Run("accept", "--dev", EurUsd, "--dev", GbpUsd, "--dev", EmptyUsdJpy, "--dev", EmptyXau, "--holdout", GbpUsd, "--holdout", EmptyXau, "--seed", "3", "--runs", "50");

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"\| Profitable symbols \| Holdout \| [0-1] of 2 \| 3 of 4 \| FAIL \|", result.Output);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Accept_DevelopmentLogsWithoutAnyTrade_AreRefusedWithAClearMessage()
    {
        SimCommandResult result = Run("accept", "--dev", EmptyXau, "--dev", EmptyUsdJpy, "--seed", "3");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("The --dev logs contain no trade, so there is nothing to evaluate", result.Error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Output);
        Assert.Empty(result.Files);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void AcceptHelp_ExplainsHowSymbolsAreCounted()
    {
        SimCommandResult result = Run("accept", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("a log without", result.Output, StringComparison.Ordinal);
        Assert.Contains("trades counts as one symbol that is not profitable", result.Output, StringComparison.Ordinal);
    }
}
