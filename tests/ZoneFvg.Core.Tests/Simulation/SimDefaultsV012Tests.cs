using Xunit;
using ZoneFvg.Core.Simulation;
using static ZoneFvg.Core.Tests.SimData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-007: the simulation tools default to the 200,000 USD account and a 2% daily budget, and the
/// combiner's open-risk and currency caps are off unless their option is given (owner decisions P,
/// R and W of 2026-11-24). Trades from <see cref="SimData.Trade"/>: risk 500 each, no commission.
/// </summary>
public class SimDefaultsV012Tests
{
    private static SimCommandResult Run(IReadOnlyDictionary<string, string> files, params string[] args)
    {
        return SimCommandLine.Run(args, path => files.TryGetValue(path, out string? text) ? text : throw new FileNotFoundException("No such file.", path));
    }

    // Four overlapping EURUSD buys on Tuesday 6 January 2026, each +1R, all closing at 20:00.
    private static Dictionary<string, string> FourEurUsdBuys()
    {
        TradeRecord[] trades =
        {
            Trade("EURUSD", Utc(2026, 1, 6, 8, 0), Utc(2026, 1, 6, 20, 0), 1.0),
            Trade("EURUSD", Utc(2026, 1, 6, 9, 0), Utc(2026, 1, 6, 20, 0), 1.0),
            Trade("EURUSD", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 20, 0), 1.0),
            Trade("EURUSD", Utc(2026, 1, 6, 11, 0), Utc(2026, 1, 6, 20, 0), 1.0),
        };
        return new Dictionary<string, string> { ["eur.csv"] = TradeLogCsv.Write(trades) };
    }

    [Fact]
    [Trait("Req", "SIM-007")]
    public void Defaults_OfTheThreeParameterObjects_Are200000And2PercentAndTheCapsOff()
    {
        var portfolio = new PortfolioParameters();
        var challenge = new FtmoChallengeParameters();
        var acceptance = new AcceptanceInputs(Array.Empty<TradeRecord>());

        Assert.Equal(200000.0, portfolio.InitialCapital);
        Assert.Equal(2.0, portfolio.DailyLossLimitPercent);
        Assert.False(portfolio.OpenRiskCapEnabled);
        Assert.False(portfolio.CurrencyCapEnabled);
        Assert.Equal(200000.0, challenge.InitialCapital);
        Assert.Equal(2.0, challenge.DailyBudgetPercent);
        Assert.Equal(200000.0, acceptance.InitialCapital);
    }

    [Theory]
    [Trait("Req", "SIM-007")]
    [InlineData("combine")]
    [InlineData("montecarlo")]
    [InlineData("ftmo")]
    [InlineData("accept")]
    public void Help_ShowsTheNewDefaults(string command)
    {
        string help = Run(new Dictionary<string, string>(), command, "--help").Output;

        Assert.Contains("  --initial-capital <n>   Initial capital (default 200000)\n", help, StringComparison.Ordinal);
        Assert.Contains("  --daily-budget <pct>    Daily risk budget, RSK-006 (default 2)\n", help, StringComparison.Ordinal);
        Assert.Contains("  --max-open-risk <pct>   Open risk cap, RSK-004: off unless given (for example 1.5)\n", help, StringComparison.Ordinal);
        Assert.Contains("  --max-per-currency <n>  Same-side trades per currency, RSK-005: off unless given (for example 2)\n", help, StringComparison.Ordinal);
        Assert.DoesNotContain("default 100000", help, StringComparison.Ordinal);
        Assert.DoesNotContain("(default 1.5)", help, StringComparison.Ordinal);
    }

    // The report and the equity curve that main (901235f, before WP38) printed for FourEurUsdBuys with
    // its defaults: 100,000, a budget of 1.5%, both caps on. EUR long: the third and fourth would be 3 > 2.
    private const string OldReport =
        "# Combined portfolio (SIM-002)\n\n"
        + "Assumptions:\n\n"
        + "- The logs are a sample of the past: past trade distributions may not repeat.\n"
        + "- Money is taken as logged. Each trade is checked when it opens against the kept trades still open, with the caps of RSK-004, RSK-005 and RSK-006 in that order; a refused trade is left out.\n"
        + "- The balance changes only when a trade closes; open-trade swings are not in the logs, so the drawdown is measured on closed balances.\n\n"
        + "| Measure | Value |\n"
        + "|---|---|\n"
        + "| Trades in the logs | 4 |\n"
        + "| Trades kept | 2 |\n"
        + "| Skipped by open risk cap (RSK-004) | 0 |\n"
        + "| Skipped by currency exposure cap (RSK-005) | 2 |\n"
        + "| Skipped by daily risk budget (RSK-006) | 0 |\n"
        + "| Initial capital | 100000.00 |\n"
        + "| Final balance | 101000.00 |\n"
        + "| Maximum drawdown | 0.00 (0.00%) |\n\n"
        + "| Symbol | Trades kept | Net result |\n"
        + "|---|---|---|\n"
        + "| EURUSD | 2 | 1000.00 |\n";

    private const string OldEquity = "time_utc,balance\n2026-01-06T20:00:00Z,100500\n2026-01-06T20:00:00Z,101000\n";

    [Fact]
    [Trait("Req", "SIM-007")]
    public void Combine_OldValuesGivenAsOptions_SameOutputAsMainPrintedByteForByte()
    {
        SimCommandResult result = Run(FourEurUsdBuys(), "combine", "--log", "eur.csv", "--initial-capital", "100000", "--daily-budget", "1.5", "--max-open-risk", "1.5", "--max-per-currency", "2", "--equity", "eq.csv");

        Assert.Equal(OldReport, result.Output);
        Assert.Equal(OldEquity, Assert.Single(result.Files).Content);
    }

    [Fact]
    [Trait("Req", "SIM-007")]
    public void Combine_ACapOff_AddsTheSentenceThatItRefusesNothing()
    {
        SimCommandResult oneOff = Run(FourEurUsdBuys(), "combine", "--log", "eur.csv", "--max-open-risk", "1.5");

        Assert.Contains("a refused trade is left out. A cap shown as off refuses nothing.\n", oneOff.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("A cap shown as off", OldReport, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-007")]
    public void Combine_WithoutTheCapOptions_KeepsTheTradesTheCapsRefusedAndSaysTheyAreOff()
    {
        SimCommandResult result = Run(FourEurUsdBuys(), "combine", "--log", "eur.csv", "--initial-capital", "100000", "--daily-budget", "1.5");

        // Open risk 2,000 and EUR long 4: both caps off; the daily budget (1,500) refuses the fourth (M - W = 2,000).
        Assert.Contains("| Trades kept | 3 |", result.Output, StringComparison.Ordinal);
        Assert.Contains("| Skipped by open risk cap (RSK-004) | off |", result.Output, StringComparison.Ordinal);
        Assert.Contains("| Skipped by currency exposure cap (RSK-005) | off |", result.Output, StringComparison.Ordinal);
        Assert.Contains("| Skipped by daily risk budget (RSK-006) | 1 |", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-007")]
    public void Combine_TheDefaults_KeepAllFourTradesAt200000And2Percent()
    {
        SimCommandResult result = Run(FourEurUsdBuys(), "combine", "--log", "eur.csv");

        Assert.Contains("| Trades kept | 4 |", result.Output, StringComparison.Ordinal);
        Assert.Contains("| Initial capital | 200000.00 |", result.Output, StringComparison.Ordinal);
        Assert.Contains("| Skipped by daily risk budget (RSK-006) | 0 |", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIM-007")]
    [InlineData("--max-open-risk", "1.5", 3, "| Skipped by open risk cap (RSK-004) | 1 |", "| Skipped by currency exposure cap (RSK-005) | off |")]
    [InlineData("--max-per-currency", "2", 2, "| Skipped by open risk cap (RSK-004) | off |", "| Skipped by currency exposure cap (RSK-005) | 2 |")]
    public void Combine_OneCapOption_SwitchesOnlyThatCapOn(string option, string value, int kept, string openRiskRow, string currencyRow)
    {
        // On 100,000 with a budget of 10%: open risk 1.5% = 1,500 refuses the fourth trade (2,000).
        SimCommandResult result = Run(FourEurUsdBuys(), "combine", "--log", "eur.csv", "--initial-capital", "100000", "--daily-budget", "10", option, value);

        Assert.Contains("| Trades kept | " + kept.ToString(System.Globalization.CultureInfo.InvariantCulture) + " |", result.Output, StringComparison.Ordinal);
        Assert.Contains(openRiskRow, result.Output, StringComparison.Ordinal);
        Assert.Contains(currencyRow, result.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-007")]
    public void Ftmo_TheDefaults_UseADailyBudgetOf2PercentAtHalfAPercentRisk()
    {
        TradeRecord[] trades =
        {
            Trade("EURUSD", Utc(2026, 1, 5, 10, 0), Utc(2026, 1, 5, 12, 0), 1.0),
            Trade("EURUSD", Utc(2026, 1, 6, 10, 0), Utc(2026, 1, 6, 12, 0), -1.0),
        };
        var files = new Dictionary<string, string> { ["eur.csv"] = TradeLogCsv.Write(trades) };

        SimCommandResult result = Run(files, "ftmo", "--log", "eur.csv", "--seed", "1", "--runs", "10");

        Assert.Equal(SimCommandLine.ExitOk, result.ExitCode);
        Assert.Contains("| Challenge | 0.5% | 2% |", result.Output, StringComparison.Ordinal);
        Assert.Contains("| Challenge | 0.75% | 3% |", result.Output, StringComparison.Ordinal);
        Assert.Contains("| Verification | 1% | 4% |", result.Output, StringComparison.Ordinal);
    }
}
