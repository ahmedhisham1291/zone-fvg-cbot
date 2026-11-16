using Xunit;
using ZoneFvg.Core.Simulation;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-001: <c>sim extract --in &lt;saved log&gt; --out &lt;trade log&gt;</c> turns a saved backtest
/// log into a trade log file that the other commands accept.
/// </summary>
public class SimExtractCommandTests
{
    private static readonly string SavedLog = BacktestLogExtractTests.Line(1, BacktestLogExtractTests.Row1) + "\n"
        + BacktestLogExtractTests.Line(2, BacktestLogExtractTests.Row2) + "\n";

    private static SimCommandResult Run(IReadOnlyDictionary<string, string> files, params string[] args)
    {
        return SimCommandLine.Run(args, path => files.TryGetValue(path, out string? text) ? text : throw new FileNotFoundException("No such file.", path));
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Extract_SavedLog_WritesTheTradeLogThatCombineAccepts()
    {
        var files = new Dictionary<string, string> { ["log.txt"] = SavedLog };

        SimCommandResult result = Run(files, "extract", "--in", "log.txt", "--out", "eurusd.csv");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Error);
        Assert.StartsWith("Extracted 2 trades (numbers 1 to 2) into eurusd.csv.\n", result.Output, StringComparison.Ordinal);
        SimOutputFile file = Assert.Single(result.Files);
        Assert.Equal("eurusd.csv", file.Path);
        Assert.Equal(TradeLogCsv.Header + "\n" + BacktestLogExtractTests.Row1 + "\n" + BacktestLogExtractTests.Row2 + "\n", file.Content);

        files["eurusd.csv"] = file.Content;
        SimCommandResult combined = Run(files, "combine", "--log", "eurusd.csv");
        Assert.Equal(0, combined.ExitCode);
        Assert.Contains("| Trades kept | 2 |", combined.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData(new[] { "extract", "--out", "a.csv" }, "Option --in is required")]
    [InlineData(new[] { "extract", "--in", "log.txt" }, "Option --out is required")]
    [InlineData(new[] { "extract", "--in", "log.txt", "--log", "a.csv" }, "Unknown option \"--log\" for extract")]
    [InlineData(new[] { "extract", "--in", "empty.txt", "--out", "a.csv" }, "Invalid backtest log \"empty.txt\": No trade lines")]
    public void Extract_InvalidArgumentsOrLog_FailWithExitCodeOneAndAReason(string[] args, string reason)
    {
        var files = new Dictionary<string, string> { ["log.txt"] = SavedLog, ["empty.txt"] = "nothing here\n" };

        SimCommandResult result = Run(files, args);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(reason, result.Error, StringComparison.Ordinal);
        Assert.Empty(result.Files);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Extract_UnreadableFile_FailsWithExitCodeTwo()
    {
        SimCommandResult result = Run(new Dictionary<string, string>(), "extract", "--in", "missing.txt", "--out", "a.csv");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Cannot read \"missing.txt\"", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void Help_ListsExtractAndItsUsage()
    {
        var files = new Dictionary<string, string>();

        Assert.Contains("extract", Run(files, "--help").Output, StringComparison.Ordinal);
        Assert.StartsWith("Usage: sim extract ", Run(files, "extract", "--help").Output, StringComparison.Ordinal);
    }
}
