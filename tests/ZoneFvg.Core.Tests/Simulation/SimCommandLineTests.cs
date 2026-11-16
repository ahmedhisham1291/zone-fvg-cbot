using System.Text;
using Xunit;
using ZoneFvg.Core.Simulation;
using static ZoneFvg.Core.Tests.SimData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The console commands of WP10 (SIM-002 to SIM-005): <c>sim combine</c>, <c>sim montecarlo</c>,
/// <c>sim ftmo</c> and <c>sim accept</c>, each with <c>--help</c> and deterministic for a given
/// <c>--seed</c>. Files are supplied in memory or from the sample logs in TestData/Sim.
/// </summary>
public class SimCommandLineTests
{
    private const string EurUsd = "sample-eurusd.csv";
    private const string GbpUsd = "sample-gbpusd.csv";

    private static readonly Dictionary<string, string> Samples = new()
    {
        [EurUsd] = Sample(EurUsd),
        [GbpUsd] = Sample(GbpUsd),
    };

    private static string Sample(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Sim", name), Encoding.UTF8);
    }

    private static SimCommandResult Run(IReadOnlyDictionary<string, string> files, params string[] args)
    {
        return SimCommandLine.Run(args, path => files.TryGetValue(path, out string? text) ? text : throw new FileNotFoundException("No such file.", path));
    }

    private static SimCommandResult RunSamples(params string[] args)
    {
        return Run(Samples, args);
    }

    [Theory]
    [Trait("Req", "SIM-002")]
    [InlineData(null)]
    [InlineData("--help")]
    [InlineData("help")]
    public void Run_NoCommandOrHelp_PrintsTheCommands(string? argument)
    {
        SimCommandResult result = argument == null ? RunSamples() : RunSamples(argument);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage: sim <command>", result.Output);
        Assert.Contains("montecarlo", result.Output);
        Assert.Equal(string.Empty, result.Error);
    }

    [Theory]
    [Trait("Req", "SIM-002")]
    [Trait("Req", "SIM-003")]
    [Trait("Req", "SIM-004")]
    [Trait("Req", "SIM-005")]
    [InlineData("combine")]
    [InlineData("montecarlo")]
    [InlineData("ftmo")]
    [InlineData("accept")]
    public void Run_CommandHelp_PrintsItsUsage(string command)
    {
        SimCommandResult result = RunSamples(command, "--log", "x.csv", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("Usage: sim " + command + " ", result.Output);
    }

    [Theory]
    [Trait("Req", "SIM-002")]
    [InlineData(new[] { "simulate" }, "Unknown command \"simulate\"")]
    [InlineData(new[] { "combine", "--logs", EurUsd }, "Unknown option \"--logs\" for combine")]
    [InlineData(new[] { "combine", "--log" }, "Option --log needs a value")]
    [InlineData(new[] { "combine", "--log", EurUsd, "--out", "a.csv", "--out", "b.csv" }, "Option --out may be given only once")]
    [InlineData(new[] { "combine" }, "At least one --log file is required")]
    [InlineData(new[] { "combine", "--log", EurUsd, "--initial-capital", "1,000" }, "Option --initial-capital needs a number")]
    [InlineData(new[] { "combine", "--log", EurUsd, "--max-per-currency", "2.5" }, "Option --max-per-currency needs a whole number")]
    [InlineData(new[] { "combine", "--log", EurUsd, "--daily-budget", "0" }, "DailyLossLimitPercent")]
    [InlineData(new[] { "montecarlo", "--log", EurUsd }, "Option --seed is required")]
    [InlineData(new[] { "montecarlo", "--log", EurUsd, "--seed", "-1" }, "Option --seed needs a whole number")]
    [InlineData(new[] { "montecarlo", "--log", EurUsd, "--seed", "1", "--sequences", "0" }, "Sequences")]
    [InlineData(new[] { "ftmo", "--log", EurUsd, "--seed", "1", "--risk", "0" }, "RiskPercent")]
    [InlineData(new[] { "accept", "--seed", "1" }, "At least one --dev file is required")]
    public void Run_InvalidArguments_FailWithExitCodeOneAndAReason(string[] args, string reason)
    {
        SimCommandResult result = RunSamples(args);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(reason, result.Error);
        Assert.Equal(string.Empty, result.Output);
        Assert.Empty(result.Files);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Run_UnreadableFile_FailsWithExitCodeTwoAndThePath()
    {
        SimCommandResult result = RunSamples("combine", "--log", "missing.csv");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Cannot read \"missing.csv\"", result.Error);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    [Trait("Req", "SIM-002")]
    public void Run_InvalidTradeLog_FailsWithTheFileAndRowNumber()
    {
        var files = new Dictionary<string, string> { ["bad.csv"] = TradeLogCsv.Header + "\nEURUSD,Z\n" };

        SimCommandResult result = Run(files, "combine", "--log", "bad.csv");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Invalid trade log \"bad.csv\": Row 2: expected 16 columns", result.Error);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_TwoLogsWithOutputFiles_WritesTheKeptTradesAndTheEquityCurve()
    {
        TradeRecord win = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 9, 0), 2.0);
        TradeRecord loss = Trade("USDJPY", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 11, 30), -1.0);
        var files = new Dictionary<string, string> { ["a.csv"] = TradeLogCsv.Write(new[] { win }), ["b.csv"] = TradeLogCsv.Write(new[] { loss }) };

        SimCommandResult result = Run(files, "combine", "--log", "b.csv", "--log", "a.csv", "--out", "combined.csv", "--equity", "equity.csv");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.Contains("| Trades kept | 2 |", result.Output);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal("combined.csv", result.Files[0].Path);
        Assert.Equal(TradeLogCsv.Write(new[] { win, loss }), result.Files[0].Content);
        Assert.Equal("equity.csv", result.Files[1].Path);
        Assert.Equal("time_utc,balance\n2026-01-06T09:00:00Z,101000\n2026-01-06T11:30:00Z,100500\n", result.Files[1].Content);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_CapOptions_AreUsed()
    {
        // With an open-risk cap of 0.5% of 100,000 the second overlapping trade (500 + 500) is refused.
        TradeRecord first = Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 12, 0), 1.0);
        TradeRecord second = Trade("USDJPY", Utc(2026, 1, 6, 9, 0), Utc(2026, 1, 6, 12, 0), 1.0);
        var files = new Dictionary<string, string> { ["a.csv"] = TradeLogCsv.Write(new[] { first, second }) };

        SimCommandResult result = Run(files, "combine", "--log", "a.csv", "--max-open-risk", "0.5");

        Assert.Contains("| Skipped by open risk cap (RSK-004) | 1 |", result.Output);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Combine_SampleLogs_RunsAndCombiningAgainChangesNothing()
    {
        SimCommandResult first = RunSamples("combine", "--log", EurUsd, "--log", GbpUsd, "--out", "combined.csv");
        var again = new Dictionary<string, string> { ["combined.csv"] = first.Files[0].Content };
        SimCommandResult second = Run(again, "combine", "--log", "combined.csv", "--out", "combined.csv");

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("| Trades in the logs | 80 |", first.Output);
        Assert.Equal(first.Files[0].Content, second.Files[0].Content);
    }

    [Fact]
    [Trait("Req", "SIM-003")]
    public void MonteCarlo_SampleLogs_IsDeterministicAndMatchesTheLibrary()
    {
        SimCommandResult first = RunSamples("montecarlo", "--log", EurUsd, "--log", GbpUsd, "--seed", "5", "--sequences", "300");
        SimCommandResult second = RunSamples("montecarlo", "--log", EurUsd, "--log", GbpUsd, "--seed", "5", "--sequences", "300");
        PortfolioResult portfolio = PortfolioCombiner.Combine(new[] { TradeLogCsv.Read(Samples[EurUsd]).Records, TradeLogCsv.Read(Samples[GbpUsd]).Records }, new PortfolioParameters());
        MonteCarloResult direct = MonteCarlo.Run(portfolio.Trades.Select(t => t.ResultR).ToArray(), new MonteCarloParameters { Sequences = 300 }, 5UL);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("Seed: 5", first.Output);
        Assert.Equal(first.Output, second.Output);
        Assert.Equal(direct.ToMarkdown(), first.Output);
    }

    [Fact]
    [Trait("Req", "SIM-004")]
    public void Ftmo_SampleLogs_PrintsBothPhasesAtThreeRisksDeterministically()
    {
        SimCommandResult first = RunSamples("ftmo", "--log", EurUsd, "--log", GbpUsd, "--seed", "11", "--runs", "200");
        SimCommandResult second = RunSamples("ftmo", "--log", EurUsd, "--log", GbpUsd, "--seed", "11", "--runs", "200");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(first.Output, second.Output);
        Assert.Contains("Seed: 11", first.Output);
        Assert.Contains("| Challenge | 0.5% | 1.5% |", first.Output);
        Assert.Contains("| Challenge | 0.75% | 2.25% |", first.Output);
        Assert.Contains("| Verification | 1% | 3% |", first.Output);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Accept_SampleLogs_PrintsTheReportAndTheChallengeRun()
    {
        SimCommandResult result = RunSamples("accept", "--dev", EurUsd, "--dev", GbpUsd, "--stress", EurUsd, "--seed", "3", "--runs", "200");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("# Acceptance report, spec 13.1 (SIM-005)", result.Output);
        Assert.Contains("| Trades, all symbols | Development |", result.Output);
        Assert.Contains("| Trades, all symbols | Holdout | no holdout log | 60 or more | NOT EVALUATED |", result.Output);
        Assert.Contains("Overall: FAIL", result.Output);
        Assert.Contains("| Challenge | 0.5% | 1.5% |", result.Output);
        Assert.Equal(result.Output, RunSamples("accept", "--dev", EurUsd, "--dev", GbpUsd, "--stress", EurUsd, "--seed", "3", "--runs", "200").Output);
    }

    [Fact]
    [Trait("Req", "SIM-005")]
    public void Accept_AllLogGroups_AreReadAndCombined()
    {
        SimCommandResult result = RunSamples("accept", "--dev", EurUsd, "--holdout", GbpUsd, "--benchmark", GbpUsd, "--benchmark-holdout", EurUsd, "--seed", "3", "--runs", "50");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("| Trades, all symbols | Holdout | 40 | 60 or more | FAIL |", result.Output);
        Assert.DoesNotContain("no benchmark log", result.Output);
    }

    [Fact]
    [Trait("Req", "SIM-002")]
    public void Run_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => SimCommandLine.Run(null!, _ => string.Empty));
        Assert.Throws<ArgumentNullException>(() => SimCommandLine.Run(new[] { "combine" }, null!));
    }
}
