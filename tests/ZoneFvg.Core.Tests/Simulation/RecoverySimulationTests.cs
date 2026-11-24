using System.Text;
using Xunit;
using ZoneFvg.Core.Risk;
using ZoneFvg.Core.Simulation;
using static ZoneFvg.Core.Tests.SimData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-006: <c>--sizing recovery</c> in the challenge simulator. Each trade risks the RSK-016 amount
/// of its own symbol (owner decisions V and Z), from the same <see cref="RecoveryState"/> the bot
/// uses, placed at the trade's open with the simulated balance and closed with its money result.
/// From 200,000 with the defaults; one trade a day, so the daily budget does not decide; the risk
/// of a trade is read as the change of the final balance when it is added (it loses 1R).
/// </summary>
public class RecoverySimulationTests
{
    private static readonly FtmoChallengeParameters WithRecovery = new()
    {
        Recovery = new RiskParameters { SizingPlan = SizingPlan.Recovery },
        MinTradingDays = 100,
    };

    private static TradeRecord Day(string symbol, int day, double resultR)
    {
        return Trade(symbol, Utc(2026, 1, day, 10, 0), Utc(2026, 1, day, 12, 0), resultR);
    }

    private static double FinalBalance(IReadOnlyList<TradeRecord> trades)
    {
        ChallengePath path = FtmoChallengeSimulator.RunPath(trades, CestClock.StartOfCestDayUtc(trades[0].OpenTimeUtc), WithRecovery);
        return path.FinalBalance;
    }

    /// <summary>The risk of each trade: the fall of the final balance when the trade, a 1R loss, is added to the ones before it.</summary>
    private static double[] RisksOfLosses(IReadOnlyList<TradeRecord> trades)
    {
        var risks = new double[trades.Count];
        double before = WithRecovery.InitialCapital;
        for (int i = 0; i < trades.Count; i++)
        {
            double after = FinalBalance(trades.Take(i + 1).ToArray());
            risks[i] = before - after;
            before = after;
        }

        return risks;
    }

    [Fact]
    [Trait("Req", "SIM-006")]
    public void RunPath_TenLossesOfOneSymbol_RiskTheBaseSequenceThenStartAgain()
    {
        TradeRecord[] trades = Enumerable.Range(5, 10).Select(day => Day("EURUSD", day, -1.0)).ToArray();

        double[] risks = RisksOfLosses(trades);

        double[] expected = { 40.6027, 60.9040, 91.3561, 137.0341, 205.5511, 308.3267, 462.4901, 693.7351, 40.6027, 60.9040 };
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], risks[i], 4);
        }

        Assert.Equal(197898.4933, FinalBalance(trades), 4);
    }

    [Fact]
    [Trait("Req", "SIM-006")]
    public void RunPath_WinLossWinAtP2_RaisesCAndDAndTheNextRisks64_6557()
    {
        TradeRecord[] trades = { Day("EURUSD", 5, 2.0), Day("EURUSD", 6, -1.0), Day("EURUSD", 7, 2.0), Day("EURUSD", 8, -1.0) };

        double afterWin = FinalBalance(trades.Take(1).ToArray());
        double afterLoss = FinalBalance(trades.Take(2).ToArray());
        double afterSecondWin = FinalBalance(trades.Take(3).ToArray());
        double afterNext = FinalBalance(trades);

        Assert.Equal(81.2054, afterWin - 200000.0, 4);
        Assert.Equal(64.6557, afterWin - afterLoss, 4);
        Assert.Equal(2.0 * 60.9040, afterSecondWin - afterLoss, 4);
        Assert.Equal(64.6557, afterSecondWin - afterNext, 4);
    }

    [Fact]
    [Trait("Req", "SIM-006")]
    [Trait("Req", "SIM-007")]
    public void RunPath_TwoSymbolsInterleaved_EachKeepsItsOwnState()
    {
        // EURUSD W, GBPUSD L, then EURUSD and GBPUSD each lose 1R: 40.6027, 40.6027, 64.6557, 60.9040.
        TradeRecord[] trades = { Day("EURUSD", 5, 2.0), Day("GBPUSD", 6, -1.0), Day("EURUSD", 7, -1.0), Day("GBPUSD", 8, -1.0) };

        double b1 = FinalBalance(trades.Take(1).ToArray());
        double b2 = FinalBalance(trades.Take(2).ToArray());
        double b3 = FinalBalance(trades.Take(3).ToArray());
        double b4 = FinalBalance(trades);

        Assert.Equal(2.0 * 40.6027, b1 - 200000.0, 4);
        Assert.Equal(40.6027, b1 - b2, 4);
        Assert.Equal(64.6557, b2 - b3, 4);
        Assert.Equal(60.9040, b3 - b4, 4);
    }

    [Fact]
    [Trait("Req", "SIM-006")]
    public void Validate_RecoveryWithTheLadderOrAnInvalidValue_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (WithRecovery with { Ladder = new RiskParameters() }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (WithRecovery with { RewardRisk = 0.0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (WithRecovery with { Recovery = new RiskParameters { RecoveryRunLossPercent = 0.0 } }).Validate());
        Assert.Null(Record.Exception(() => (WithRecovery with { Ladder = new RiskParameters { LadderEnabled = false } }).Validate()));
        Assert.Equal(2.0, new FtmoChallengeParameters().RewardRisk);
        Assert.Null(new FtmoChallengeParameters().Recovery);
    }

    private static SimCommandResult Run(params string[] args)
    {
        var files = new Dictionary<string, string> { ["eur.csv"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Sim", "sample-eurusd.csv"), Encoding.UTF8) };
        return SimCommandLine.Run(args, path => files[path]);
    }

    [Fact]
    [Trait("Req", "SIM-006")]
    public void FtmoCommand_SizingRecovery_RunsBothPhasesDeterministically()
    {
        SimCommandResult first = Run("ftmo", "--log", "eur.csv", "--seed", "3", "--runs", "50", "--sizing", "recovery");
        SimCommandResult second = Run("ftmo", "--log", "eur.csv", "--seed", "3", "--runs", "50", "--sizing", "recovery");
        SimCommandResult options = Run("ftmo", "--log", "eur.csv", "--seed", "3", "--runs", "50", "--sizing", "recovery", "--recovery-run-loss", "2", "--recovery-max", "1", "--reward-risk", "3", "--growth", "1.5");

        Assert.Equal(SimCommandLine.ExitOk, first.ExitCode);
        Assert.Contains("| Challenge | recovery |", first.Output, StringComparison.Ordinal);
        Assert.Contains("| Verification | recovery |", first.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("| 0.75% |", first.Output, StringComparison.Ordinal);
        Assert.Equal(first.Output, second.Output);
        Assert.Equal(SimCommandLine.ExitOk, options.ExitCode);
        Assert.Contains("| Challenge | recovery |", options.Output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-006")]
    public void AcceptCommand_SizingRecovery_NamesItInTheChallengeRow()
    {
        var files = new Dictionary<string, string> { ["eur.csv"] = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Sim", "sample-eurusd.csv"), Encoding.UTF8) };

        SimCommandResult result = SimCommandLine.Run(new[] { "accept", "--dev", "eur.csv", "--seed", "3", "--runs", "50", "--sizing", "recovery" }, path => files[path]);

        Assert.Equal(SimCommandLine.ExitOk, result.ExitCode);
        Assert.Contains("| Challenge | recovery |", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIM-006")]
    [InlineData(new[] { "--sizing", "Recovery" }, "Option --sizing needs fixed, ladder or recovery, but was \"Recovery\"")]
    [InlineData(new[] { "--sizing", "martingale" }, "Option --sizing needs fixed, ladder or recovery, but was \"martingale\"")]
    [InlineData(new[] { "--sizing", "recovery", "--recovery-run-loss", "0" }, "Option --recovery-run-loss needs a percent greater than 0 and at most 100")]
    [InlineData(new[] { "--sizing", "recovery", "--recovery-max", "101" }, "Option --recovery-max needs a percent greater than 0 and at most 100")]
    [InlineData(new[] { "--sizing", "recovery", "--reward-risk", "0" }, "Option --reward-risk needs a number greater than 0")]
    [InlineData(new[] { "--sizing", "recovery", "--growth", "0.9" }, "Option --growth needs a number of at least 1")]
    public void FtmoCommand_InvalidRecoveryOption_IsRefusedNamingIt(string[] extra, string reason)
    {
        SimCommandResult result = Run(new[] { "ftmo", "--log", "eur.csv", "--seed", "3" }.Concat(extra).ToArray());

        Assert.Equal(SimCommandLine.ExitInvalid, result.ExitCode);
        Assert.Contains(reason, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "SIM-006")]
    [InlineData("combine")]
    [InlineData("montecarlo")]
    public void OtherCommands_RefuseTheRecoveryOptions(string command)
    {
        SimCommandResult result = Run(command, "--log", "eur.csv", "--recovery-run-loss", "1");

        Assert.Equal(SimCommandLine.ExitInvalid, result.ExitCode);
        Assert.Contains("Unknown option \"--recovery-run-loss\" for " + command, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-006")]
    public void FtmoHelp_ListsTheRecoveryOptions()
    {
        string help = Run("ftmo", "--help").Output;

        Assert.Contains("--sizing <fixed|ladder|recovery>", help, StringComparison.Ordinal);
        Assert.Contains("--recovery-run-loss <pct>", help, StringComparison.Ordinal);
        Assert.Contains("--recovery-max <pct>", help, StringComparison.Ordinal);
        Assert.Contains("--reward-risk <R>", help, StringComparison.Ordinal);
        Assert.Contains("--growth <g>", help, StringComparison.Ordinal);
    }
}
